# EVA in Unity spiegeln

Die Szene `Assets/_Project/Scenes/MirroringDemo.unity` spiegelt beide Arme, Rumpf und Kopf auf EVA. Rechts und links beziehen sich auf den jeweiligen Körper: Hebst du deinen rechten Arm, hebt EVA **ihren linken Arm**, im Game-Fenster auf der rechten Bildseite.

## Start mit Webcam

1. Öffne PowerShell im Projektordner `EVA\Mirroring`. Führe beim ersten Start `.\setup_mirroring.ps1` aus (Python 3.11, Windows x64).
2. Starte den lokalen Pose-Dienst und lasse das Terminal geöffnet.

   ```powershell
   .\.venv\Scripts\python.exe .\pose_server.py
   ```

3. Öffne in Unity 6000.3.12f1 die Szene `Assets/_Project/Scenes/MirroringDemo.unity` und wechsle zum **Game**-Fenster. Stelle dich frontal vor die Kamera. Kopf, beide Handgelenke, Schultern und Hüften sollen im Bild liegen. Schaue geradeaus und lasse die Arme locker hängen. Drücke **Play** und halte die Haltung kurz, bis unter dem Regler **EVA · Arm L · Arm R · Rumpf · Kopf** steht. Halte beide Ellbogen leicht gebeugt. Die Kalibrierung braucht eine zusammenhängende Sekunde mit erkannten Armen, Rumpf und Kopf. Bei einer Lücke beginnt die Aufnahme erneut.
4. Hebe die Arme einzeln und dann gemeinsam. Drehe oder neige den Oberkörper. Halte dann die Schultern ruhig und drehe den Kopf, nicke oder neige ihn zur Seite. Die Liste unter dem Regler zeigt die aktuell erkannten Regionen auf **EVAs** Seite.
5. Vergleiche dieselbe Bewegung bei **0 %** (Grundanimation), **50 %** (halbe Stärke) und **100 %** (volle berechnete Drehung). Alle vier Regionen teilen den Regler. Jeder Start beginnt bei **70 %**.
6. Verdecke ein Handgelenk bei sichtbaren Schultern und sichtbarem Gesicht. Der zugehörige Arm blendet zur Grundanimation, die übrigen sichtbaren Regionen folgen weiter. Bei erneut sichtbarem Handgelenk setzt die Bewegung weich ein. Für eine neue Referenzhaltung drücke **Neu kalibrieren**.

Der zweite Regler **Glättung** lässt sich zwischen 0 und 250 ms einstellen. Er startet bei 100 ms. Kleine Werte reagieren schneller, große Werte dämpfen unruhige Kamerapunkte stärker. Die Angabe beschreibt die Zeitkonstante des Filters. Zum Vergleichen dieselbe Bewegung bei gleicher Spiegelungsstärke wiederholen.

Beim Beenden von Play gibt der Dienst die Kamera frei. Mit **Strg+C** beendest du den Python-Dienst.

## Landmarks, Skelett und Tiefe ansehen

Im Game-Fenster zeigt das Feld oben rechts links die empfangenen MediaPipe-Punkte mit türkisen Verbindungen. Ein Punkt erscheint, sobald seine Sichtbarkeit und Präsenz jeweils mindestens 0,5 erreichen. Rechts zeigt das orange Skelett die aktuellen Gelenkpositionen von EVA nach der Animation. Beide Ansichten sind um die Hüfte zentriert. Beim Playback sind nur die Punkte der Referenzfolge sichtbar.

Klicke auf **zur Seitenansicht wechseln** und strecke einen leicht seitlich gehaltenen Arm langsam in Richtung Kamera. Nach rechts im Diagramm bedeutet nach vorne. Beobachte zuerst, ob sich dein Handgelenk relativ zur Schulter im türkisen Skelett bewegt, dann vergleiche EVA. Ein unruhiges Eingangssignal lässt sich in der Avatarbewegung durch Glättung dämpfen. Bleibt die geschätzte Bewegung schon am Eingang zu klein, stelle das Handy stabil auf und halte Schulter, Ellbogen und Handgelenk auch beim Vorstrecken getrennt sichtbar. Ein leicht schräger Arm ist dafür leichter zu beobachten als eine Hand direkt vor dem Ellbogen.

MediaPipe schätzt die räumlichen Punkte aus einem Kamerabild relativ zur Hüftmitte. Für eine unabhängige Tiefenmessung wären zum Beispiel eine Tiefenkamera oder zwei synchronisierte und kalibrierte Kameras mit gemeinsamer Auswertung erforderlich. [MediaPipe Pose Landmarker](https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker?tab=Python)

## Vorführung ohne Kamera

Starte im selben Ordner den Dienst mit Referenzdaten und öffne dieselbe Szene. Pro Port kann ein Dienst laufen.

```powershell
.\.venv\Scripts\python.exe .\pose_server.py --playback
```

Die Folge läuft über den regulären WebSocket und wiederholt sich etwa alle 27 Sekunden.

| Abschnitt | Was im Game-Fenster zu sehen ist |
| --- | --- |
| Referenz und Arme | Neutrale Haltung, dann EVAs linker Arm, ihr rechter Arm und beide Arme oben |
| Rumpf | Drehung, Vorbeugen und seitliches Neigen. Die Beine bleiben in der Grundanimation. |
| Kopf | Kopfdrehen, Nicken und seitliches Neigen bei ruhigem Rumpf |
| Kombination | Beide Arme oben, Rumpf gedreht und Kopf zusätzlich nach vorne geneigt |
| Teilweiser Ausfall | Wenn Schulter und Ellbogen denselben Messpunkt haben, blendet EVAs linker Arm aus. Danach kehrt der Arm zurück und die Gesichtspunkte fallen aus. |
| Unterbrechung | 1,5 Sekunden Sendepause. Danach folgen beide Arme wieder. |
| Langsames Heben | Ein gestreckter Arm steigt bis zur Schulterhöhe. Danach folgen abwechselnde kleine Tiefenabweichungen und ein einzelner größerer Ausreißer. |

## Wie die Bewegung entsteht

Der Python-Dienst verarbeitet die Webcam lokal mit MediaPipe Pose Landmarker auf der CPU. Ein Client verbindet sich mit `ws://127.0.0.1:8766`. Unity sendet beim Öffnen `{"type":"start","cameraIndex":0}`. Mit `{"type":"stop"}` quittiert der Dienst `idle` und der Empfänger verwirft die letzte Pose. Bei erneutem Start auf derselben Verbindung läuft die Sequenznummer weiter.

Eine Pose liefert `schemaVersion: 1`, `coordinateSpace: "mediapipe_world_m"`, `sequence`, `sourceTimestampMs`, `valid` und bei erfolgreicher Erkennung 33 Weltpunkte in Metern mit `x`, `y`, `z`, `visibility` und `presence`. Übertragen werden ausschließlich Landmark-Daten und Statusmeldungen.

| EVA-Bereich | Pose-Landmarks | Anwendung im Rocketbox-Skelett |
| --- | --- | --- |
| Arme | Schultern 11/12, Ellbogen 13/14, Handgelenke 15/16 | Das angepasste Handziel und die Ellbogenrichtung steuern die Zwei-Knochen-IK. Die Schlüsselbeine heben sich bei hohen Armpositionen mit. |
| Rumpf | Schultern 11/12, Hüften 23/24 | Schulterlinie und Richtung von der Hüftmitte zur Schultermitte bestimmen die Rumpfdrehung auf `Spine1` und `Spine2` (40/60 %). |
| Kopf | Nase 0, Ohren 7/8 | Ohrachse und Richtung zur Nase bestimmen die zusätzliche Drehung von Hals und Kopf (30/70 %) relativ zum Rumpf. |

Nach der Animator-Grundpose legt Unity zuerst die Rumpfdrehung und danach die verbleibende Kopf- und Armbewegung auf das aktive Skelett. Die automatische Blicksteuerung ist in der Demo deaktiviert. Eine Region fällt aus, wenn ein benötigter Punkt weniger als 0,5 Konfidenz hat oder seine Richtung geometrisch unbrauchbar ist. Das Gewicht fährt über die volle Skala in 0,3 Sekunden zurück. Bleiben Frames ganz aus, erkennt Unity dies nach 0,5 Sekunden. Das Sprachbackend wird für die Demo nicht benötigt.

## Stand vom 19. November 2026

Bao hat den Empfang aller vier Regionen und die Spiegelrichtung mit DroidCam beim vorherigen Stand bestätigt. Der persönliche Vergleich der neuen Gelenksteuerung und die Messung der gesamten Verzögerung vom Handybild bis zur Avatarbewegung stehen aus.

## Frühere Abnahme am 18. November 2026

- **Referenzdaten in Unity:** Alle 15 Abschnitte der Folge bei 0, 50 und 100 % über den lokalen WebSocket geprüft. Gemessen wurden die Drehungen am aktiven Skelett sowie die Seite der gehobenen Hand im Kamerabild. Bei 35° Rumpfdrehung ergaben sich 0°, 17,5° und 35°. Bei der kombinierten Pose kam bei voller Stärke ein eigenständiges Nicken von 20° hinzu. Nach einer Sendepause und nach `stop`/`start` folgte EVA wieder.
- **Webcam:** Das Pose-Modell wurde geladen. OpenCV konnte keine Kamera an Index 0, 1 oder 2 öffnen. Der Test mit einer Person vor der Kamera steht aus.
