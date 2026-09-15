# Digital Twin Drone

Unity digital-twin visualizer fed by a Python MAVLink bridge.

- Unity editor: `6000.3.11f1` (see `Unity/ProjectSettings/ProjectVersion.txt`)
- Bridge: serial MAVLink (`COM3`, 57600) -> UDP `127.0.0.1:5055` protocol-v2 CSV, see
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
2. `python Tools/MAVLink/mavlink_bridge.py` — defaults to `COM3` @ 57600,
   ArduCopter. Override on the command line:
   `python Tools/MAVLink/mavlink_bridge.py --port COM7 --baud 115200 --firmware PLANE`
3. Open `Unity/` in Unity and play `SampleScene`
4. Bridge console shows `Sending UDP packets: N/sec` when telemetry flows; the
   dashboard shows **Connected** in the top-left

## Test

```bash
cd Tools/MAVLink
.venv\Scripts\python -m unittest test_mavlink -v
```

Covers the shared 37-field protocol-v2 contract, source-validity metadata,
send timestamps, command validation, MAVLink dispatch, and correlated command
acknowledgements. No drone hardware is needed.

## No drone? Use the simulator

`udp_simulator.py` feeds fake telemetry on the exact same ports. Every Unity
widget (position, attitude, battery ring, vibration, latency, GPS, mode/armed)
and the acknowledged command flow comes alive with zero hardware:

```bash
python Tools/MAVLink/udp_simulator.py               # 10 Hz, 5 m / 15 s circle until Ctrl+C
python Tools/MAVLink/udp_simulator.py --seconds 30  # optional fixed run
python Tools/MAVLink/udp_simulator.py --radius 10 --period 30
python Tools/MAVLink/udp_simulator.py --spike-after 0  # disable vibration spike
python Tools/MAVLink/udp_simulator.py --spike-after 5   # trip the vibration alert
```

Run it *instead of* `mavlink_bridge.py` when testing Unity behavior. It listens
on the command port and returns the same correlated acknowledgement events as
the bridge.

## Map view

Map View renders a live raster basemap behind the GPS track. Drag to pan, use
the mouse wheel or the `-` / `+` controls to zoom, and use the target control
to recenter on the aircraft. The layers control switches between OpenStreetMap
street tiles, Esri World Imagery, and a hybrid imagery/labels view. Only visible
tiles are requested. OSM tiles are cached locally for seven days; Esri imagery
is kept in memory only. Attribution updates with the active provider. Tile URLs
and the application user agent are configurable on `DroneDashboardUI`.

## UDP Protocol

**Telemetry -> Unity (bridge -> `127.0.0.1:5055`)** uses 37 comma-separated
fields. `Tools/MAVLink/telemetry_protocol.py` is the canonical ordered field
list.

| # | Field | Units | MAVLink source |
|---|---|---|---|
| 0-2 | x, y, z | m, local NED with z up | `LOCAL_POSITION_NED` |
| 3-5 | roll, pitch, yaw | degrees | `ATTITUDE` |
| 6 | status | flag | reserved (`1`) |
| 7 | battery | %; `-1` unknown | `SYS_STATUS` |
| 8-10 | vibration x/y/z | m/s^2 | `VIBRATION` |
| 11 | send timestamp | unix s | bridge clock |
| 12-13 | flight mode, armed | string, 0/1 | `HEARTBEAT` |
| 14-15 | voltage, current | V, A; `-1` unknown | `SYS_STATUS` |
| 16-19 | satellites, lat, lon, GPS alt | count/degrees/m MSL | `GPS_RAW_INT` |
| 20 | protocol version | integer (`2`) | bridge |
| 21-26 | source ages | seconds; `-1` never seen | bridge receive clock |
| 27 | GPS fix type | MAVLink enum | `GPS_RAW_INT` |
| 28-30 | sensor present/enabled/health | bitmasks | `SYS_STATUS` |
| 31-36 | home valid, lat/lon/alt/north/east | flag/degrees/m | `HOME_POSITION` |

Unity rejects wrong-version, wrong-length, non-finite, or untrusted-source
datagrams. Values become stale independently according to their MAVLink source
age; a live UDP socket alone never implies that a sensor is healthy.

**Commands <- Unity (bridge on `127.0.0.1:5056`)** are local-only datagrams:

```text
CMD,2,<request-id>,<command>
```

| Command      | MAVLink action                              |
|--------------|---------------------------------------------|
| `LAND`       | `MAV_CMD_NAV_LAND`                          |
| `STABILIZE`  | `MAV_CMD_DO_SET_MODE` → mode `STABILIZE`    |
| `ALT_HOLD`   | `MAV_CMD_DO_SET_MODE` → mode `ALT_HOLD`     |
| `POSHOLD`    | `MAV_CMD_DO_SET_MODE` → mode `POSHOLD`      |
| `FORCE_DISARM`| `MAV_CMD_COMPONENT_ARM_DISARM` (forced)    |

Flight-mode numbers are resolved through the selected firmware table. The
bridge correlates MAVLink `COMMAND_ACK` and returns:

```text
ACK,2,<request-id>,<command>,<result>,<progress>,<result-param-2>
```

The dashboard exposes sent, accepted, in-progress, completed, rejected, failed,
and timed-out states. Completion requires matching heartbeat telemetry, not just
a successful UDP send.

## Safety

- The dashboard never issues an automatic flight command from received
  telemetry. Battery and vibration thresholds produce operator alerts only.
- Applicable automatic actions such as battery, radio-loss, and geofence
  failsafes belong on the flight controller, where they continue to work if
  Unity or UDP fails. Use only actions supported by the autopilot vendor.
- Telemetry and commands bind to loopback by default. Remote command transport
  is intentionally unsupported until an authenticated channel exists.
- Mode controls require fresh heartbeat and relevant healthy sensors. `LAND`
  and `FORCE_DISARM` also require fresh armed state; force disarm retains its
  confirmation dialog.

## Known limitations

- Real GPS fields require a fresh `GPS_RAW_INT` fix type of 3 or better. No fix
  is shown as unknown rather than inferred from latency or satellite count.
- The map draws home only after a valid `HOME_POSITION` message is received.
- Telemetry data rate is set by the message intervals requested in the bridge
  (10 Hz position/attitude/vibration, 5 Hz battery, 1 Hz GPS).
