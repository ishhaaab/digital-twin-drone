# Digital Twin Drone

Unity visualizer for a drone, fed over UDP by a Python MAVLink bridge. The drone talks MAVLink over serial, the bridge converts it to CSV, and Unity renders position, attitude, battery, vibration, and latency in a dashboard. Everything runs on one machine, no cloud, no server.

## Data path

```
Serial (COM3 @ 57600) -> mavlink_bridge.py -> UDP 5055 -> DroneDataReceiver.cs -> UI
```

| Piece | Where it lives |
| --- | --- |
| Bridge | `Tools/MAVLink/mavlink_bridge.py` |
| Receiver | `Unity/Assets/Scripts/Networking/DroneDataReceiver.cs` |
| Scene to play | `Unity/Assets/Scenes/DroneDemo.unity` |
| Unity editor | 6000.3.11f1 (`Unity/ProjectSettings/ProjectVersion.txt`) |

The top of `mavlink_bridge.py` sets the serial port, baud, and UDP target. Unity listens on UDP 5055 and replies with text commands on 5056. The reply socket exists in the receiver, but the bridge does not act on those commands yet, so LAND gets sent into a void.

## Packet format

The bridge sends 8 comma-separated fields:

```
x,y,z,roll,pitch,yaw,1,battery
```

x, y, z are meters, NED with z flipped up. roll, pitch, yaw are degrees. `1` is a fixed status flag that says nothing yet. battery is percent remaining.

The receiver also accepts optional trailing fields: vibration x/y/z, a Unix send timestamp (it measures latency against this), flight mode, armed, voltage, current, and a satellite count. The bridge does not send these yet, but a growing CSV is cheap and the receiver already knows how to read it.

## Layout

```text
Unity/                  Unity project, open this folder in the editor
  Assets/
    Drone/Art/          Model, Materials, Animations
    Drone/Prefabs/      Drone + Variants
    Scenes/             DroneDemo, SampleScene
    Scripts/
      Core/             Controller, camera, tail
      Networking/       DroneDataReceiver (UDP in: 5055, out: 5056)
      UI/               Button, dashboard, FX, updater
    RenderTextures/
    Settings/
Tools/
  MAVLink/
    mavlink_bridge.py   Serial → UDP bridge
    requirements.txt    pymavlink
    .venv/              installed virtualenv
```

## Run

1. Activate the venv: `Tools\MAVLink\.venv\Scripts\Activate.ps1`
2. Install deps (first time only): `pip install -r Tools\MAVLink\requirements.txt`
3. Run the bridge: `python Tools\MAVLink\mavlink_bridge.py`. Change `PORT` and `BAUD` at the top of the file to match your flight controller.
4. Open `Unity/` in Unity editor 6000.3.11f1, open `DroneDemo`, and press play.

Without a real flight controller you can still see the dashboard move by faking packets:

```powershell
# PowerShell, in a separate shell
$i = 0
while ($true) {
  $pkt = "{0},{1},2,0,0,{2},1,87" -f ($i / 100.0), 1.5, ($i % 360)
  $u = New-Object System.Net.Sockets.UdpClient
  $u.Send([Text.Encoding]::UTF8.GetBytes($pkt), $pkt.Length, "127.0.0.1", 5055)
  $u.Close(); Start-Sleep -Milliseconds 50; $i++
}
```

## Requirements

- Python 3.10 or newer
- Unity 6000.3.11f1
- A flight controller on serial that emits MAVLink `LOCAL_POSITION_NED`, `ATTITUDE`, and `SYS_STATUS` at a set interval. The bridge asks for all three on startup.