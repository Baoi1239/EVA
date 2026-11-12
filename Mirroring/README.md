# EVA Webcam Pose Service

Lokaler Webcam-Pose-Erkennungsdienst fuer EVA (Python 3.11, Windows x64).

## Setup
Fuehren Sie das Setup-Skript in PowerShell aus:
```powershell
.\setup_mirroring.ps1
```

## Start
```powershell
.\.venv\Scripts\python.exe pose_server.py --port 8766
```

## Protokoll & States
- Verbindung ueber WebSocket auf `ws://127.0.0.1:8766` (Single-Client).
- Kamera starten: `{"type": "start", "cameraIndex": 0}`
- Kamera stoppen: `{"type": "stop"}`
- States: Gestoppt, Gestartet, Fehler.

Pose-Nachrichten enthalten `type: "pose"`, `schemaVersion: 1`,
`coordinateSpace: "mediapipe_world_m"`, eine aufsteigende `sequence`,
`sourceTimestampMs`, `valid` und bei gueltigem Tracking 33 `landmarks` mit
`x`, `y`, `z`, `visibility` und `presence`. Die Koordinaten sind MediaPipe
World-Landmarks in Metern relativ zur Huefte. Bilder werden nicht gesendet.
Unity verwirft falsche Versionen, alte Frames und nicht-endliche Werte.

## Erster Unity-Armtest

1. Dienst mit dem Befehl oben starten. Die Webcam bleibt bis zur Verbindung aus.
2. In Unity 6000.3.12f1 `Assets/_Project/Scenes/Main.unity` oeffnen.
3. Am EVA-Objekt, das `EVAAnimationController` traegt, die Komponenten
   `EVAPoseReceiver` und `EVAOneArmMirroring` hinzufuegen. Beide finden ihre
   Referenzen auf demselben Objekt automatisch. Der Receiver startet Kamera 0.
4. Play starten und der Kamera frontal gegenueberstehen. Die erste erkannte
   Pose dient als Kalibrierung; danach den rechten Arm bewegen. Standardmaessig
   folgt der linke Avatararm als visuelles Spiegelbild. Der Tracking-Status
   ist ueber `EVAPoseReceiver.LastStatus` abfragbar.

Der Armtest ist eine erste technische Integration, noch keine abgenommene
Standalone-Demo. `Main.unity` startet weiterhin das separate Sprachbackend.
Ausfuehrungsreihenfolge: Animator-Basispose, Gaze, dann Arm-Mirroring in
`LateUpdate`. Kamera und WebSocket werden beim Verlassen von Play
freigegeben; der separat gestartete Dienst bleibt an. Eine echte
Webcam-/Unity-Laufzeitpruefung steht noch aus.

## Hinweise
- **CPU-Delegat**: Standardmaeßig CPU-basiert (optimiert fuer EVA 8GB VRAM).
- **Unity Retargeting**: Ein Arm ist als erster Prototyp implementiert.
- **Referenzen**:
  - Bazarevsky et al. / MediaPipe (https://arxiv.org/abs/2006.10204, https://arxiv.org/abs/2206.11678)
  - Google Docs: https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker/python
