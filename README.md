# Digital Twin Drone

Unity digital-twin visualizer fed by a Python MAVLink bridge.

- Unity editor: `6000.3.11f1` (see `Unity/ProjectSettings/ProjectVersion.txt`)
- Bridge: serial MAVLink (`COM3`, 57600) → UDP `127.0.0.1:5055` CSV, see
  `Tools/MAVLink/mavlink_bridge.py`
- Unity listens on UDP 5055 (`Assets/Scripts/Networking/DroneDataReceiver.cs`)
  and sends commands back to the bridge on UDP 5056

## Layout

```text
Unity/                  Unity project (open this folder in Unity)
  Assets/
    Drone/Art/          Model, Materials, Animations
    Drone/Prefabs/      Drone + Variants
    Scenes/
      SampleScene.unity   THE working scene — fully wired, use this one
      DroneDemo.unity     Minimal static scene (no scripts) — safe to delete
    Scripts/
      Core/             DroneController, Drone_Camera, DroneTail
      Networking/       DroneDataReceiver (UDP listener)
      UI/               Button, DroneDashboardUI, DroneUIFX, DroneUIUpdater
    RenderTextures/
    Settings/
Tools/
  MAVLink/
    mavlink_bridge.py   Serial → UDP bridge (telemetry out, commands in)
    udp_simulator.py    Fake telemetry sender — test without a drone
    requirements.txt
```

> ⚠ **Scene note:** open and play **`SampleScene`**, not `DroneDemo`.
> `DroneDemo` is an old static scene with no scripts attached. `SampleScene`
> is the only one wired (drone prefab + controller, camera view render
> texture, dashboard, command buttons, event system).

## Run

1. `pip install -r Tools/MAVLink/requirements.txt`
2. `python Tools/MAVLink/mavlink_bridge.py` (adjust `PORT`/`BAUD` at the top,
   and `FIRMWARE = "COPTER"`/`"PLANE"` if you fly a plane)
3. Open `Unity/` in Unity and play `SampleScene`
4. Bridge console shows `Sending UDP packets: N/sec` when telemetry flows; the
   dashboard shows **Connected** in the top-left

## No drone? Use the simulator

`udp_simulator.py` feeds fake telemetry on the exact same ports — every Unity
widget (position, attitude, battery ring, vibration, latency, GPS, mode/armed)
and the command buttons come alive with zero hardware:

```bash
python Tools/MAVLink/udp_simulator.py            # 10 Hz circle, 30 s
python Tools/MAVLink/udp_simulator.py --seconds 0   # run forever
python Tools/MAVLink/udp_simulator.py --spike-after 5   # trip vibration auto-land
```

Run it *instead of* `mavlink_bridge.py` when you're testing that Unity
behaviours work. It listens on the command port too, so LAND/mode buttons do something.

## UDP protocol

**Telemetry → Unity (bridge → `127.0.0.1:5055`)**, 20 comma-separated fields:

| # | Field         | Units      | MAVLink source     |
|---|---------------|------------|--------------------|
| 0 | x             | m (NED N)  | `LOCAL_POSITION_NED` |
| 1 | y             | m (NED E)  | `LOCAL_POSITION_NED` |
| 2 | z             | m (up+)    | `LOCAL_POSITION_NED` (sign-flipped) |
| 3 | roll          | degrees    | `ATTITUDE`           |
| 4 | pitch         | degrees    | `ATTITUDE`           |
| 5 | yaw           | degrees    | `ATTITUDE`           |
| 6 | status        | —          | reserved (`1`)       |
| 7 | battery       | %          | `SYS_STATUS`         |
| 8 | vibration_x   | m/s²       | `VIBRATION`          |
| 9 | vibration_y   | m/s²       | `VIBRATION`          |
| 10| vibration_z   | m/s²       | `VIBRATION`          |
| 11| send timestamp| unix s     | local time           |
| 12| flight mode   | string     | `HEARTBEAT`          |
| 13| armed         | 0/1        | `HEARTBEAT`          |
| 14| voltage       | V          | `SYS_STATUS`         |
| 15| current       | A          | `SYS_STATUS`         |
| 16| satellites    | count      | `GPS_RAW_INT`        |
| 17| lat           | degrees    | `GPS_RAW_INT`        |
| 18| lon           | degrees    | `GPS_RAW_INT`        |
| 19| gps_alt       | m (MSL)    | `GPS_RAW_INT`        |

Gaps in the packet (e.g. a bridge running an older/lighter format) are fine —
the receiver parses positionally and keeps unadvertised fields at their defaults
(`vibration 0`, `flight_mode UNKNOWN`, `voltage/current 0`, `satellites -1`).

**Commands ← Unity (bridge ← `127.0.0.1:5056`)**, plain-text words, newline optional:

| Command      | MAVLink action                              |
|--------------|---------------------------------------------|
| `LAND`       | `MAV_CMD_NAV_LAND`                          |
| `STABILIZE`  | `MAV_CMD_DO_SET_MODE` → mode `STABILIZE`    |
| `ALT_HOLD`   | `MAV_CMD_DO_SET_MODE` → mode `ALT_HOLD`     |
| `POSHOLD`    | `MAV_CMD_DO_SET_MODE` → mode `POSHOLD`      |
| `FORCE_DISARM`| `MAV_CMD_COMPONENT_ARM_DISARM` (forced)    |

Flight-mode numbers are resolved through the `FIRMWARE` table at the top of
`mavlink_bridge.py` (ArduCopter by default).

## Safety

- **Vibration auto-land:** if any vibration axis exceeds the threshold
  (`DroneDataReceiver.vibrationThreshold`, 60 m/s²) the bridge is asked to
  `LAND` and the alarm banner flashes. Only meaningful if the flight controller
  supports the `VIBRATION` message.
- **Battery critical:** at ≤ 15 % battery a `LAND` is sent once
  (`DroneUIUpdater`).
- Auto-land commands only take effect while `mavlink_bridge.py` is running and
  the vehicle is reachable. Always exercise manual override near the vehicle.

## Known limitations

- Real GPS fields rely on a GPS receiver + `GPS_RAW_INT`; without one,
  satellites stay `-1` and the signal bars fall back to a latency proxy.
- Telegram data rate is set by the message intervals requested in the bridge
  (10 Hz position/attitude/vibration, 5 Hz battery, 1 Hz GPS).