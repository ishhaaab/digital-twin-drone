
# Ground Control Dashboard Navigation & UX Recommendations

More tabs would make the dashboard feel like a complete ground-control application rather than a single monitoring screen. Keep the tab count limited and give each one a distinct operational purpose.

## Recommended Navigation

| Tab | Purpose |
|---|---|
| **LIVE** | The dashboard already built: 3D/map viewport, flight controls, telemetry, and status |
| **MISSION** | Waypoints, geofence, route ordering, altitude targets, ETA, and mission upload |
| **REPLAY** | Select a recorded flight and replay it with synchronized map position, 3D movement, and graphs |
| **LOGS** | Searchable events, commands, warnings, connection changes, and raw packets |
| **SYSTEM** | Optional later: sensor health, UDP configuration, calibration, and diagnostics |

I would rename **"main dashboard"** to **LIVE**. It immediately communicates that this is the current aircraft state.

---

## Navigation Layout

Use one persistent top-level navigation row:

```text
DRONE TWIN    LIVE    MISSION    REPLAY    LOGS
```

Keep the following visible globally:

* Connection status
* Flight mode
* Armed state
* Link quality
* Settings

The existing **3D VIEW / MAP VIEW** control should remain inside **LIVE** because it changes the live viewport, not the entire application.

The camera/map buttons in the global top-right area are currently redundant with those viewport tabs. Removing that duplication would make room for the primary navigation.

---

# LOGS Tab

Avoid making it a wall of console text. A better layout would be:

```text
┌ Filters ─────┬ Event timeline ──────────────┬ Event details ┐
│ All          │ 14:32:08  Mode: ALT HOLD     │ Source        │
│ Commands     │ 14:32:11  GPS fix acquired   │ Payload       │
│ Warnings     │ 14:32:19  LAND acknowledged  │ Timestamp     │
│ Connection   │ 14:32:24  Vibration warning  │ Raw packet    │
└──────────────┴───────────────────────────────┴───────────────┘
```

### Useful Features

* Severity filters: `info`, `warning`, `critical`
* Categories:

  * Telemetry
  * Command
  * Connection
  * Safety
* Search by message or timestamp
* Pause automatic scrolling
* Copy/export selected entries
* Human-readable events by default
* Expandable raw UDP/MAVLink payloads for debugging
* Clear separation between a command being **sent** and **acknowledged**

---

# REPLAY Tab

This would be one of the strongest portfolio features.

### Features

* Flight session list with:

  * Date
  * Duration
  * Maximum altitude
  * Distance
* Playback timeline with:

  * Pause
  * Playback speed
  * Frame stepping
* Synchronized 3D/map position
* Graph cursor that follows playback time
* Markers for:

  * Arming
  * Mode changes
  * Warnings
  * Landing
* CSV or JSON export

Keep **LOGS** for discrete events and **REPLAY** for continuous historical telemetry.

---

# MISSION Tab

Use a map-dominant layout rather than reproducing the dashboard.

### Features

* Click to place and reorder waypoints
* Waypoint altitude and speed editor
* Route distance and estimated duration
* Geofence visualization
* Return-to-home marker
* Validation summary before upload
* Clear distinction between:

  * Draft
  * Uploaded
  * Active missions

> **Do not add this as an empty placeholder before mission commands are supported.**

---

# Improvements to LIVE

The current screen is visually strong but dense. These changes would improve hierarchy:

* Keep only **two priority graphs** visible by default, such as altitude and speed.
* Put the remaining graphs in an expandable telemetry tray.
* Add a narrow alert ribbon above the viewport for current warnings.
* Show the current flight phase:

  * `DISARMED`
  * `TAKEOFF`
  * `CRUISE`
  * `RETURNING`
  * `LANDING`
* Reduce letter spacing on the smallest labels for readability at `1366×768`.
* Flatten some sidebar cards so every panel does not receive equal visual weight.
* Make neutral telemetry values **white**.
* Reserve **cyan** for interaction and chart traces.
* Add subtle transitions when changing tabs rather than instantly replacing the entire screen.
* Add intentional states for:

  * Disconnected
  * Waiting for GPS
  * Loading
  * No flight history

---

# Best Next Step

I would build this in the following order:

1. **Add the navigation shell with LIVE and LOGS.**
2. **Record human-readable events** such as connection, mode, arming, commands, and warnings.
3. **Add persistent flight recording and the REPLAY tab.**
4. **Add MISSION** once waypoint commands and upload behavior are available.

This gives you a useful new screen immediately without creating decorative tabs that do nothing.
