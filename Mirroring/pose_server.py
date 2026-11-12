"""Local opt-in CPU pose service. Gemini draft, reviewed/corrected by Codex."""
import argparse
import asyncio
import contextlib
import json
import math
from pathlib import Path
import threading
import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision
from websockets.asyncio.server import serve
from websockets.exceptions import ConnectionClosed


class CaptureWorker:
    """Owns camera/model; publishes a single immutable latest snapshot."""
    def __init__(self, index, model):
        self.index, self.model = index, model
        self.stop_event = threading.Event()
        self.lock = threading.Lock()
        self.snapshot = None
        self.sequence = 0
        self.thread = threading.Thread(target=self.run, daemon=True)

    def publish(self, **message):
        with self.lock:
            self.sequence += 1
            self.snapshot = dict(message, sequence=self.sequence)

    def latest(self):
        with self.lock:
            return self.snapshot

    def run(self):
        cap, detector = None, None
        try:
            options = vision.PoseLandmarkerOptions(
                base_options=mp_python.BaseOptions(
                    model_asset_path=str(self.model),
                    delegate=mp_python.BaseOptions.Delegate.CPU),
                running_mode=vision.RunningMode.VIDEO, num_poses=1,
                output_segmentation_masks=False)
            detector = vision.PoseLandmarker.create_from_options(options)
            if self.stop_event.is_set():
                return
            cap = cv2.VideoCapture(self.index)
            if not cap.isOpened():
                raise RuntimeError(f"Kamera {self.index} nicht verfuegbar")
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            cap.set(cv2.CAP_PROP_FPS, 30)
            self.publish(type="status", state="capturing", message="Kamera aktiv")
            timestamp = 0
            while not self.stop_event.is_set():
                ok, frame = cap.read()
                if not ok:
                    raise RuntimeError("Kamera liefert kein Bild")
                timestamp = max(timestamp + 1, time.monotonic_ns() // 1_000_000)
                image = mp.Image(image_format=mp.ImageFormat.SRGB,
                                 data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                result = detector.detect_for_video(image, timestamp)
                points = result.pose_world_landmarks
                landmarks = [dict(x=p.x, y=p.y, z=p.z,
                                  visibility=p.visibility, presence=p.presence)
                             for p in points[0]] if points else []
                valid = len(landmarks) == 33 and all(
                    isinstance(value, (int, float)) and math.isfinite(value)
                    for point in landmarks for value in point.values())
                if not valid:
                    landmarks = []
                self.publish(type="pose", schemaVersion=1,
                             coordinateSpace="mediapipe_world_m",
                             sourceTimestampMs=timestamp, timestampMs=timestamp,
                             valid=valid, landmarks=landmarks)
        except Exception as error:
            self.publish(type="error", message=str(error), valid=False)
        finally:
            try:
                if cap is not None:
                    cap.release()
            finally:
                if detector is not None:
                    detector.close()

    async def stop(self):
        self.stop_event.set()
        await asyncio.to_thread(self.thread.join, 5.0)
        return not self.thread.is_alive()


class PoseService:
    def __init__(self, model):
        self.model = model
        self.connected = False
        self.worker = None

    async def handler(self, socket):
        # All lease transitions run on one asyncio loop, including reconnects.
        if self.connected or (self.worker and self.worker.thread.is_alive()):
            await socket.close(1008, "Kamera besetzt oder wird noch freigegeben")
            return
        self.connected = True
        last_worker, last_sequence = None, -1

        async def receive():
            async for raw in socket:
                try:
                    data = json.loads(raw)
                    if not isinstance(data, dict):
                        raise ValueError("JSON-Objekt erforderlich")
                    command = data.get("type")
                    if command == "start":
                        index = data.get("cameraIndex", 0)
                        if type(index) is not int or not 0 <= index <= 10:
                            raise ValueError("cameraIndex muss eine Ganzzahl von 0 bis 10 sein")
                        if self.worker and self.worker.thread.is_alive():
                            raise ValueError("Kamera bereits aktiv oder wird noch freigegeben")
                        self.worker = CaptureWorker(index, self.model)
                        self.worker.thread.start()
                        await socket.send(json.dumps(dict(type="status", state="starting", message="Kamera startet")))
                    elif command == "stop":
                        if self.worker and not await self.worker.stop():
                            raise ValueError("Kamera wird noch freigegeben; neuer Start ist gesperrt")
                        self.worker = None
                        await socket.send(json.dumps(dict(type="status", state="idle", message="Kamera aus")))
                    else:
                        raise ValueError("Unbekannter Befehl; start oder stop erwartet")
                except (ValueError, TypeError) as error:
                    await socket.send(json.dumps(dict(type="error", message=str(error))))

        async def send():
            nonlocal last_worker, last_sequence
            while True:
                await asyncio.sleep(1 / 30)
                worker = self.worker
                if worker is not last_worker:
                    last_worker, last_sequence = worker, -1
                snapshot = worker.latest() if worker else None
                if snapshot and snapshot["sequence"] != last_sequence:
                    await socket.send(json.dumps(snapshot, allow_nan=False))
                    last_sequence = snapshot["sequence"]

        tasks = []
        try:
            await socket.send(json.dumps(dict(type="status", state="idle", message="Verbunden; Kamera aus")))
            tasks = [asyncio.create_task(receive()), asyncio.create_task(send())]
            done, _ = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                task.result()
        except ConnectionClosed:
            pass
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            try:
                if self.worker and not await self.worker.stop():
                    print("Kamera-Thread antwortet nicht; neue Verbindungen bleiben gesperrt", flush=True)
            finally:
                self.connected = False


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--model", type=Path, default=Path(__file__).parent / "models/pose_landmarker_lite.task")
    args = parser.parse_args()
    if not args.model.is_file():
        parser.error("Pose-Modell fehlt. Zuerst setup_mirroring.ps1 ausfuehren.")
    service = PoseService(args.model.resolve())
    print(f"Pose-Dienst: ws://127.0.0.1:{args.port}; Kamera aus", flush=True)
    async with serve(service.handler, "127.0.0.1", args.port, max_size=1024, max_queue=4):
        await asyncio.Future()


if __name__ == "__main__":
    with contextlib.suppress(KeyboardInterrupt):
        asyncio.run(main())
