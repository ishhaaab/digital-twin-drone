## Flight Replay Page — Prototype Logic

A historical drone flight is stored as an immutable **Flight Record**.

The page lets users:

* Replay the flight path on a map with timestamp scrubbing.
* View synchronized telemetry: lat/long, altitude, speed, heading, battery, GPS, signal, flight mode, pitch/roll, gimbal, warnings.
* View flight events and captured media at the exact timestamp/location.
* Export data as Telemetry CSV, Summary CSV, KML, GeoJSON, or original flight log.
* Compare an original flight with later re-flights using route deviation, altitude deviation, duration, battery use, etc.

### Core distinction

**Replay ≠ Re-fly**

Replay = inspect what actually happened.

Re-fly = convert the recorded path into a reusable **Mission Template**.

Use CTA:

**Create Mission from Flight**

Do not directly repeat historical telemetry.

The reusable mission should contain relevant:

* Waypoints
* Altitudes
* Speeds
* Heading
* Camera actions
* Gimbal actions
* Takeoff/landing behaviour

The same or another compatible drone can then execute the mission and produce completely new telemetry.

### Re-flight flow

Flight Record
→ Create Mission from Flight
→ Mission Draft
→ Review/Edit
→ Assign Drone
→ Mission Readiness Check
→ Fly
→ New Flight Record

Readiness should validate things like drone connection, battery, GPS, geofence, route compatibility, weather/wind, home point, estimated duration and battery requirement.

### Main UI

Header:
Flight ID, date/time, city/country, coordinates, drone, mission, status.

Summary:
Duration, distance, max altitude, max speed, battery usage, GPS quality.

Main area:
Large flight-path map + drone position + playback timeline.

Tabs:
**Overview | Telemetry | Events | Media | Compare**

Important architecture:

**Mission Template → Mission Runs → Flight Records**

Never edit or overwrite the original Flight Record. Any route changes create/update a Mission Template instead.
