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

## Mirroring-Demo in Unity

1. Dienst mit dem Befehl oben starten. Die Webcam bleibt bis zur Verbindung aus.
2. In Unity 6000.3.12f1 `Assets/_Project/Scenes/MirroringDemo.unity` oeffnen.
3. Play starten und der Kamera frontal gegenueberstehen. Die erste erkannte
   Pose dient als Kalibrierung; danach den rechten Arm bewegen. Standardmaessig
   folgt der linke Avatararm als visuelles Spiegelbild.
4. Den sichtbaren Regler bewegen: `0 %` zeigt nur die normale EVA-Animation,
   `50 %` mischt beide Bewegungen, `100 %` nutzt die volle berechnete
   Armspiegelung. Der Startwert ist bei jedem Play `70 %`. Ober- und Unterarm
   verwenden denselben Wert. Der Status unter dem Regler zeigt Verbindung,
   Kamera- oder Trackingfehler an.

Die Testszene enthaelt keinen Sprachbackend-Launcher und keine Gespraechs-UI.
`Main.unity` bleibt die normale EVA-Anwendung. Ausfuehrungsreihenfolge:
Animator-Basispose, Gaze, dann Arm-Mirroring in `LateUpdate`. Bei Trackingverlust
blendet der Arm zur Basisanimation zurueck. Kamera und WebSocket werden beim
Verlassen von Play freigegeben; der separat gestartete Pose-Dienst bleibt an.
Eine echte Webcam-/Unity-Laufzeitpruefung steht noch aus.

## Hinweise
- **CPU-Delegat**: Standardmaeßig CPU-basiert (optimiert fuer EVA 8GB VRAM).
- **Unity Retargeting**: Ein Arm ist als erster Prototyp implementiert.
- **Referenzen**:
  - Bazarevsky et al. / MediaPipe (https://arxiv.org/abs/2006.10204, https://arxiv.org/abs/2206.11678)
  - Google Docs: https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker/python
