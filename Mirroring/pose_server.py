"""Stream webcam or reference poses to the local Unity mirroring demo."""
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
import numpy as np
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
        self.thread = threading.Thread(target=self.run, daemon=True)

    def publish(self, **message):
        with self.lock:
            self.snapshot = message

    def latest(self):
        with self.lock:
            return self.snapshot

    def publish_pose(self, landmarks, timestamp):
        valid = len(landmarks) == 33 and all(
            isinstance(value, (int, float)) and math.isfinite(value)
            for point in landmarks for value in point.values())
        self.publish(type="pose", schemaVersion=1, coordinateSpace="mediapipe_world_m",
                     sourceTimestampMs=timestamp, timestampMs=timestamp,
                     valid=valid, landmarks=landmarks if valid else [])

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
                self.publish_pose(landmarks, timestamp)
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


class PlaybackWorker(CaptureWorker):
    """Repeats isolated, combined and partially occluded upper-body poses."""
    def __init__(self, index):
        super().__init__(index, None)

    @staticmethod
    def pose(*, left=False, right=False, trunk=(0, 0, 0), head=(0, 0, 0),
             invalid_arm=False, hidden_face=False):
        # Rotation vectors in degrees in MediaPipe space. Rotate the face about
        # the ears, then the entire upper body about the hip midpoint.
        vertices = {0: (0, -.68, -.11), 7: (.075, -.70, 0), 8: (-.075, -.70, 0)}
        for side, raised, sign in ((11, left, 1), (12, right, -1)):
            vertices[side] = (.18 * sign, -.46, 0)
            vertices[side + 2] = ((.44 if raised else .21) * sign, -.56 if raised else -.18, 0)
            vertices[side + 4] = ((.45 if raised else .23) * sign, -.80 if raised else .06, 0)
        if invalid_arm:
            vertices[14] = vertices[12]
        head_rotation = cv2.Rodrigues(np.radians(head))[0]
        torso_rotation = cv2.Rodrigues(np.radians(trunk))[0]
        ears = np.array([0, -.70, 0])
        landmarks = [dict(x=0., y=0., z=0., visibility=0., presence=0.) for _ in range(33)]
        for index, position in vertices.items():
            if index in (0, 7, 8):
                position = ears + head_rotation @ (position - ears)
            x, y, z = torso_rotation @ position
            confidence = 0. if hidden_face and index in (0, 7, 8) else 1.
            landmarks[index] = dict(x=float(x), y=float(y), z=float(z),
                                    visibility=confidence, presence=confidence)
        for index, x in ((23, .14), (24, -.14)):
            landmarks[index] = dict(x=x, y=0., z=0., visibility=1., presence=1.)
        return landmarks

    @classmethod
    def sequence(cls):
        sequence = [
            (cls.pose(), 2.5),
            (cls.pose(right=True), 1.5),
            (cls.pose(left=True), 1.5),
            (cls.pose(left=True, right=True), 1.5),
            (cls.pose(trunk=(0, 35, 0)), 1.5),
            (cls.pose(trunk=(25, 0, 0)), 1.5),
            (cls.pose(trunk=(0, 0, 20)), 1.5),
            (cls.pose(head=(0, 30, 0)), 1.5),
            (cls.pose(head=(20, 0, 0)), 1.5),
            (cls.pose(head=(0, 0, 20)), 1.5),
            (cls.pose(left=True, right=True, trunk=(0, 35, 0), head=(20, 0, 0)), 1.5),
            (cls.pose(left=True, right=True, head=(20, 0, 0), invalid_arm=True), 1.5),
            (cls.pose(left=True, right=True, hidden_face=True), 1.5),
            (None, 1.5),
            (cls.pose(left=True, right=True), 1.5),
        ]
        # Slow reach with fixed bone lengths, then a stationary arm with
        # deterministic jitter and one erroneous wrist measurement.
        for i in range(91):
            pose = cls.pose()
            angle = math.radians(min(i, 60) * 1.5)
            jitter = .012 * (-1)**i if i >= 60 else 0
            for index, length in ((14, .28), (16, .52)):
                pose[index].update(x=-.18-length*math.sin(angle),
                                   y=-.46+length*math.cos(angle), z=jitter)
            if i == 76:
                pose[16]["z"] += .18
            sequence.append((pose, 1/30))
        return sequence

    def run(self):
        sequence = self.sequence()
        self.publish(type="status", state="capturing", message="Referenzdaten aktiv")
        while not self.stop_event.is_set():
            for landmarks, duration in sequence:
                if landmarks is None:
                    self.stop_event.wait(duration)
                elif self.play_pose(landmarks, duration):
                    return
                if self.stop_event.is_set():
                    return

    def play_pose(self, landmarks, seconds):
        deadline = time.monotonic() + seconds
        while not self.stop_event.is_set() and time.monotonic() < deadline:
            self.publish_pose(landmarks, time.monotonic_ns() // 1_000_000)
            if self.stop_event.wait(1 / 30):
                return True
        return self.stop_event.is_set()


class PoseService:
    def __init__(self, create_worker):
        self.create_worker = create_worker
        self.connected = False
        self.worker = None

    async def handler(self, socket):
        # All lease transitions run on one asyncio loop, including reconnects.
        if self.connected or (self.worker and self.worker.thread.is_alive()):
            await socket.close(1008, "Kamera besetzt oder wird noch freigegeben")
            return
        self.connected = True

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
                        self.worker = self.create_worker(index)
                        self.worker.thread.start()
                        await socket.send(json.dumps(dict(type="status", state="starting", message="Pose-Quelle startet")))
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
            last_snapshot = None
            sequence = 0
            while True:
                await asyncio.sleep(1 / 30)
                worker = self.worker
                if worker is None or worker.stop_event.is_set():
                    continue
                snapshot = worker.latest()
                if snapshot is None or snapshot is last_snapshot:
                    continue
                last_snapshot = snapshot
                if snapshot["type"] == "pose":
                    # Sequence belongs to the connection, which can outlive multiple workers.
                    sequence += 1
                    snapshot = dict(snapshot, sequence=sequence)
                await socket.send(json.dumps(snapshot, allow_nan=False))

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
    parser.add_argument("--playback", action="store_true",
                        help="Synthetische Referenzfolge abspielen; keine Webcam verwenden.")
    args = parser.parse_args()
    if not args.playback and not args.model.is_file():
        parser.error("Pose-Modell fehlt. Zuerst setup_mirroring.ps1 ausfuehren.")
    model = args.model.resolve()
    service = PoseService(
        PlaybackWorker if args.playback else lambda index: CaptureWorker(index, model))
    source = "Referenzdaten" if args.playback else "Webcam"
    print(f"Pose-Dienst: ws://127.0.0.1:{args.port}; {source} wartet auf Start", flush=True)
    async with serve(service.handler, "127.0.0.1", args.port, max_size=1024, max_queue=4):
        await asyncio.Future()


if __name__ == "__main__":
    with contextlib.suppress(KeyboardInterrupt):
        asyncio.run(main())
