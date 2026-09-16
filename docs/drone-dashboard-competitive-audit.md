# Drone Dashboard Competitive Audit

Date: 2026-09-15

> This document records the pre-Phase-0 baseline. The trust and safety findings
> in Phase 0 have since been implemented; see `docs/phase-0-live-trust.md` for
> current behavior, verification, and required aircraft-side validation.

## Scope and method

This audit compares the implemented project, not the proposals in
`Whats_Next.md`, against five public references.

| Reference | Product class | Why it is useful here |
|---|---|---|
| QGroundControl | Open-source ground control station | Baseline for live flight, mission planning, vehicle setup, and analysis |
| Auterion Mission Control | Safety-focused ground control station | Reference for state-aware controls, warnings, map/video hierarchy, and preflight workflow |
| FlytBase | Cloud fleet and dock operations | Reference for fleet triage, command status, alerts, roles, and replay |
| DJI FlightHub 2 | Cloud remote operations | Reference for route scheduling, livestreams, remote operation, and team workflows |
| DroneDeploy | Mission capture and operations data | Reference for checklists, automatic logs, compliance, and post-flight value |

Evidence used:

- Source review of the Unity dashboard, receiver, map, graph, and MAVLink bridge.
- A successful Windows standalone build and simulator-driven runtime exercise.
- Runtime confirmation of both the 3D and map states in the player log.
- Current first-party product and documentation pages, accessed 2026-09-15.
- Screenshots were captured at 1920x1080, but automated visual inspection was unavailable. Visual findings are therefore based on source dimensions, hierarchy, interaction code, and runtime logs rather than unverified screenshot interpretation.

## Executive assessment

The project is already a credible **live, local, single-aircraft telemetry
prototype**. It is not yet a complete ground-control dashboard. Its strongest
differentiator is the synchronized 3D aircraft view and hardware-free simulator.
Its largest gap is operational trust: the UI can show inferred health as real
health, sends commands without knowing their outcome, and has no persistent
record of what happened.

The best direction is not a smaller FlightHub or FlytBase clone. It is a
**digital-twin flight lab** with professional GCS safety semantics:

1. Make every state truthful and every command traceable.
2. Record flights and events so the twin can replay them.
3. Add mission intent so planned and actual behavior can be compared.
4. Make the 3D view operationally useful, not only a pose visualization.
5. Add fleet and collaboration only if multi-aircraft operation becomes a real requirement.

## What is already strong

- Real telemetry drives position, attitude, battery, electrical, vibration, GPS, mode, armed state, and link values. The protocol is documented and tested.
- The center viewport supports a live 3D pose view and a geographic map with street, satellite, and hybrid basemaps.
- The map supports pan, zoom, recentering, a recent path, and local/GPS readouts.
- Connection, battery, GPS, latency, and vibration have explicit presentation thresholds.
- Force disarm requires confirmation.
- The local simulator exercises the full 20-field transport without hardware.
- The local-first architecture has a useful offline/privacy story and avoids cloud dependence.
- The restrained dark palette and semantic green/amber/red model are directionally appropriate for an operations UI.

These are meaningful strengths. Many portfolio dashboards begin with static
cards and fake values; this one has a functioning telemetry pipeline and a real
interactive viewport.

## Market comparison

| Area | Market pattern | Current project | Practical takeaway |
|---|---|---|---|
| Live hierarchy | QGroundControl and Auterion keep the map or video primary, put high-level vehicle state in the top bar, and expose detail contextually. | The map/3D viewport is primary, but both side rails and four charts remain permanently dense. | Preserve the viewport, but make secondary telemetry configurable or collapsible. |
| State-aware actions | Auterion only presents valid actions for the current vehicle state and uses hold-to-confirm for flight actions. QGroundControl also varies actions by state and vehicle type. | Mode, land, and force-disarm remain available regardless of connection, armed state, or capability. Only force disarm has confirmation. | Gate actions by connectivity, arm state, vehicle type, and capability. |
| Command feedback | FlytBase documents in-progress, completed, failed, and timestamped command states. MAVLink defines `COMMAND_ACK`. | A click emits a UDP word and logs that it was sent. The UI cannot distinguish sent, accepted, rejected, completed, or timed out. | Add an end-to-end command lifecycle before adding more commands. |
| Warnings | Auterion warnings explain the problem and, where possible, the action to take. FlytBase separates active alerts from notifications and retains history. | A flashing banner concatenates warning labels. It has no cause detail, age, source, acknowledgement, history, or remediation. | Build an alert model and alert center, not a larger banner. |
| Mission workflow | QGroundControl provides Plan, Fly, and Analyze views, waypoint editing, geofences, terrain profiles, upload/download state, and mission statistics. Auterion adds start/pause/resume and progress. | No mission data enters the bridge or UI. The map shows where the vehicle has been, not where it should go. | Mission intent is the largest functional gap after safety truth. |
| Post-flight workflow | FlytBase provides flight overviews, playback, media, reports, and downloads. DroneDeploy automatically collects logs and connects them to pilots, equipment, checklists, and compliance. | Graphs and path buffers are memory-only and disappear when the application closes. | Persist sessions and events, then build synchronized replay. |
| Fleet awareness | FlytBase uses a fleet list plus video and 3D map, with pinning, diagnostics, live viewers, and batch safety operations. QGroundControl can show multiple vehicles. | One receiver, one latest snapshot, one drone model, and no vehicle identity. | Add `vehicle_id` to the model now, but defer fleet UI until needed. |
| Video and payload | QGroundControl and Auterion let map and vehicle video swap priority. DJI and FlytBase support remote livestream and payload workflows. | The camera viewport is a rendered third-person twin, not the payload/FPV stream. | If video is in scope, pair real video with the twin rather than replacing it. |
| Preflight and readiness | QGroundControl and Auterion expose preflight checks and ready/not-ready state. DroneDeploy records checklist responses with the flight. | Individual values are present, but there is no readiness decision, checklist, or blocker drill-down. | Add a readiness summary backed by real health bits and validity states. |
| Environmental context | FlytBase documents weather and airspace alerts. QGroundControl and Auterion support geofence and nearby-aircraft context. | No weather, terrain clearance, geofence, no-fly zone, or traffic layer exists. | Add geofence and terrain with mission work; leave weather/traffic for later. |
| Collaboration | DJI, FlytBase, and DroneDeploy organize around users, roles, sites/projects, viewers, equipment, and audit records. | The application is a local single-user process. | This is acceptable for a flight lab and a gap only if remote/fleet operation is the target. |

## Highest-priority gaps

### 1. Some displayed state is not operationally truthful

> **Resolved in Phase 0 (validity scope):** Protocol v2 now carries per-source
> age, sensor health, GPS fix, and home validity, and the UI renders stale or
> unknown values accordingly. The original findings remain as historical context.

This is more important than adding another panel.

- `Unity/Assets/Scripts/UI/DroneUIUpdater.cs:266-275` reports IMU and barometer as `OK` whenever the telemetry socket is connected. The 20-field protocol contains no IMU-health or barometer-health field. These should be `UNKNOWN` until MAVLink `SYS_STATUS` health bits are transported.
- `Tools/MAVLink/mavlink_bridge.py:212-221` initializes battery to `100` and electrical/GPS values to zero while waiting for source messages. The dashboard can therefore show a fully charged battery and numeric sensor readings that were never measured.
- `Tools/MAVLink/mavlink_bridge.py:220-221` uses `satellites = -1` but initializes latitude and longitude to `0.0` before a GPS fix. Its GPS branch at lines 268-273 also keeps the last good values when fix quality drops. `Unity/Assets/Scripts/UI/DroneMapView.cs:41-48` treats `0,0` as valid because `Unity/Assets/Scripts/UI/OpenStreetMapTileLayer.cs:451-456` only checks numeric bounds. No-fix data can therefore look real, and a lost fix can look current.
- `Unity/Assets/Scripts/UI/DroneMapView.cs:137-144` draws the oldest buffered path point as "home" rather than consuming the autopilot's actual home position.
- `Unity/Assets/Scripts/UI/DroneUIUpdater.cs:277` returns when no new packet exists, before the alarm calculation at lines 402-439. Packet stoppage cannot newly activate the `LINK LOST` alarm banner.
- `Unity/Assets/Scripts/UI/DroneDashboardUI.cs:1375-1404` places the center viewport over the entire canvas in fullscreen mode, covering the connection, mode, armed, battery, and GPS top bar.
- `Unity/Assets/Scripts/Networking/DroneDataReceiver.cs:178-192` accepts telemetry from any sender on any network interface and does not validate the source endpoint. Vibration at lines 320-339 and battery handling in `Unity/Assets/Scripts/UI/DroneUIUpdater.cs:379-384` can turn those received values into a `LAND` command. A stray or spoofed datagram must not be able to initiate a flight-critical action.

Recommended correction:

- Represent values as valid, stale, unknown, warning, or critical, not only as primitives with defaults.
- Transport MAVLink sensor presence, enabled, and health bitmaps from `SYS_STATUS`.
- Transport GPS fix type and accuracy; do not render a geographic point until the fix is valid.
- Transport `HOME_POSITION` or the appropriate autopilot home/origin message.
- Evaluate connection and safety state every frame, independently of new telemetry arrival.
- Keep a compact critical status strip above fullscreen content.
- Bind to or authenticate a trusted telemetry source, validate message integrity, and keep automatic flight failsafes on the autopilot. The dashboard may surface and acknowledge a failsafe; it should not be the only safety controller.

### 2. Commands are fire-and-forget

> **Resolved in Phase 0:** Commands now carry request IDs, route correlated
> `COMMAND_ACK` states back to Unity, and require telemetry-confirmed completion
> with explicit timeout and failure states. The original finding remains as
> historical context.

`Unity/Assets/Scripts/Networking/DroneDataReceiver.cs:399-410` only sends a UDP
datagram. `Tools/MAVLink/mavlink_bridge.py:119-166` forwards a MAVLink command but
does not route `COMMAND_ACK` back to Unity. A console message saying `CMD sent` is
transport activity, not vehicle acceptance.

| State | Operator meaning |
|---|---|
| Requested | The user intentionally initiated the action. |
| Sent | The local bridge received and forwarded it. |
| Accepted or rejected | The autopilot returned `COMMAND_ACK`. |
| In progress | A long-running command is still executing. |
| Completed or timed out | Observed state confirms completion, or no confirmation arrived. |

Each command should carry an ID, target vehicle, timestamp, requested parameters,
and result. Show pending state on the initiating control, give a specific failure
reason, and retain the event in the log. Do not optimistically select a mode until
telemetry confirms the mode changed.

### 3. The transport model blocks the next product features

The positional CSV contract has no schema version, vehicle identity, packet
sequence, per-field validity, event type, command correlation, or capability
description. This is why packet loss is `--` and why fleet, truthful health,
command results, and mission state cannot be added cleanly.

Recommended next contract:

- Keep telemetry datagrams lightweight, but make them a versioned named-field envelope.
- Include `schema_version`, `vehicle_id`, `system_id`, `component_id`, `sequence`, source time, receive time, and validity metadata.
- Separate continuous telemetry from discrete events such as mode changes, arming, alerts, command results, and mission progress.
- Preserve raw MAVLink identifiers so later diagnostics are explainable.
- Keep unknown values unknown rather than substituting zero.

JSON is adequate at the current 10 Hz and is easier to evolve than positional CSV.
If rate or fleet size later matters, the same schema can move to a compact binary
encoding without changing the domain model.

### 4. There is no mission intent or flight lifecycle

The dashboard can answer "where is it now?" but not:

- What was it supposed to do?
- Which waypoint or flight phase is active?
- Is the actual path diverging from the plan?
- Is there enough battery to finish and return?
- What happened before the current 60-second graph window?
- Who or what issued the last command?

The proposed `LIVE`, `MISSION`, `REPLAY`, and `LOGS` navigation in
`Whats_Next.md` is directionally correct. The missing prerequisite is a shared
session/event model. Building tabs first would create navigation around no durable
data.

### 5. The "digital twin" is currently a pose twin

`Unity/Assets/Scripts/Core/DroneController.cs:54-108` maps measured position and
Euler angles onto a 3D model. This is useful visualization, but it does not yet
model intent, prediction, subsystem state, or environment interaction.

Make 3D answer questions that a normal map cannot:

- Planned path versus actual path and cross-track deviation.
- Predicted position or stopping envelope a few seconds ahead.
- Home, geofence, altitude ceiling, terrain, and return corridor.
- Payload camera frustum and current sensor footprint.
- Component overlays only when real motor, battery, IMU, or temperature data exists.
- Replay with event markers and a ghost of the planned trajectory.

This is the most defensible differentiator. A prettier drone model without these
overlays will not close the gap with established GCS products.

### 6. The live screen needs progressive disclosure

- `Unity/Assets/Scripts/UI/DroneDashboardUI.cs:1488-1525` permanently allocates four same-weight charts to altitude, speed, battery, and latency.
- `Unity/Assets/Scripts/UI/DroneDashboardUI.cs:242-244` duplicates camera/map selection already present at lines 887-905.
- The "Settings" panel at lines 1407-1431 is read-only connection information, so its name promises editing that it does not provide.
- Auto-scaled charts have no threshold line or operating band, making harmless fluctuation and dangerous excursion visually similar.
- Map and side panels repeat latitude, longitude, altitude, and local position without giving mission context higher priority.

Recommended live hierarchy:

1. Readiness, active phase, mode, armed state, battery-to-home, link, and GPS quality.
2. Map/video/3D workspace with actual path, planned path, home, and active alerts.
3. Current task and the next operator action.
4. Two user-selected live charts by default.
5. Detailed telemetry in a collapsible inspector or tray.

### 7. Small text and controls will not scale down safely

The canvas targets 1920x1080 in
`Unity/Assets/Scripts/UI/DroneDashboardUI.cs:141-144`, while many labels are
8.5-11 px with large tracking. At 1366x768 the configured scale is roughly 0.71,
making an 8.5 px label roughly 6 px on screen. Several controls are only 30-34 px
high.

The dim text color in `Unity/Assets/Scripts/UI/DroneUIFX.cs:291-300` has an
approximate 3.71:1 contrast ratio against the card color, below the usual 4.5:1
target for small text.

Recommended correction:

- Define explicit layouts for 1920x1080 and 1366x768 instead of shrinking the entire cockpit.
- At smaller widths, collapse detailed side cards and the chart rail into drawers.
- Keep operational text at a readable rendered size and reduce small-label tracking.
- Use tabular or monospaced numerals so changing values do not shift visually.
- Raise pointer/touch targets to about 40-44 px where practical.
- Add visible keyboard focus and alternatives for drag/scroll-only 3D interactions.
- Add labels or tooltips for icon-only controls.
- Provide a reduced-motion option for smoothing, pulsing, and alarm animation.

## Recommended target experience

```text
DRONE TWIN | LIVE | MISSION | REPLAY | LOGS | SYSTEM
Vehicle | READY / NOT READY | Mode | Armed | Battery-to-home | Link | GPS
-----------------------------------------------------------------------
Active alerts / cause / age / recommended action / acknowledge
-----------------------------------------------------------------------
Task and next action |     MAP / VIDEO / 3D WORKSPACE      | Inspector
Mission progress     | planned + actual + home + geofence | key values
-----------------------------------------------------------------------
Two selected live charts | expandable telemetry tray
```

The 3D view should be available as a main workspace and as a picture-in-picture
companion to map or payload video. Top-level navigation should change the
operator's job; map/3D/video switches should remain local to `LIVE`.

## Implementation order

### Phase 0: Trustworthy live operation

- Fix false health, GPS validity, actual home, connection-loss alarm evaluation, and fullscreen critical status.
- Remove unauthenticated dashboard-triggered auto-land behavior or place it behind a verified control path; configure equivalent safety behavior on the autopilot.
- Add explicit freshness/unknown handling per telemetry group.
- Disable or hide invalid actions based on connection and vehicle state.
- Route MAVLink `COMMAND_ACK` and expose pending/success/failure/timeout states.
- Fix the standalone skybox shader inclusion issue reported by the runtime player log.

### Phase 1: Flight memory

- Introduce a flight session ID and append-only telemetry/event recording.
- Record connection, mode, arm, warning, command, acknowledgement, and safety events.
- Add `LOGS` as a searchable event timeline with severity/category filters and raw detail.
- Add `REPLAY` with synchronized map, 3D pose, charts, and event markers.

### Phase 2: Mission intent

- Transport the MAVLink mission protocol rather than inventing a parallel waypoint format.
- Add planned home, waypoints, altitude/speed, geofence, rally/return points, and terrain profile.
- Distinguish draft, saved, uploaded, active, paused, completed, and failed states.
- Validate route, GPS, battery reserve, altitude, geofence, and capabilities before upload.
- Show mission progress and planned-versus-actual deviation in map and 3D views.

### Phase 3: Live UX and twin differentiation

- Replace four permanent charts with two configurable charts and an expandable tray.
- Replace the concatenated banner with an alert center and actionable alert cards.
- Remove duplicate map/camera controls and rename read-only settings to diagnostics.
- Add payload video only if a real stream is available.
- Add camera footprint, terrain clearance, prediction, and component overlays to 3D.
- Add a dedicated 1366x768 layout and keyboard/tooltips support.

### Phase 4: Optional operations scale

- Add vehicle identity and a selector before building fleet views.
- Add fleet triage, sites, roles, viewers, audit history, weather, and airspace only if the goal moves beyond a local flight lab.
- Do not add batch commands until single-command acknowledgement and authorization are reliable.

## What not to copy

- Do not copy enterprise AI, compliance, and organization breadth merely to make navigation look full.
- Do not add empty `MISSION`, `REPLAY`, or `LOGS` tabs.
- Do not add more always-visible gauges; improve prioritization and drill-down.
- Do not represent inferred connectivity as sensor health.
- Do not let the 3D viewport become decorative. Every overlay should answer an operator question.

## Suggested success test

A strong next version should answer these without a console or raw packets:

1. Is this aircraft safe and ready, and what blocks it if not?
2. What is it doing now, what should it do next, and who requested it?
3. Did the aircraft accept the last command?
4. Where are home, the planned route, the actual route, and any boundary conflict?
5. What is the most urgent alert, why did it occur, and what action is recommended?
6. Can the aircraft finish the task and return with reserve?
7. Can the entire flight be reconstructed after the application restarts?

## Primary sources

- QGroundControl Fly View: https://docs.qgroundcontrol.com/master/en/qgc-user-guide/fly_view/fly_view.html
- QGroundControl Plan View: https://docs.qgroundcontrol.com/master/en/qgc-user-guide/plan_view/plan_view.html
- QGroundControl overview: https://docs.qgroundcontrol.com/master/en/index.html
- Auterion Mission Control: https://docs.auterion.com/vehicle-operation/auterion-mission-control
- Auterion flight monitoring: https://docs.auterion.com/vehicle-operation/auterion-mission-control/ui-breakdown/fly/monitoring-the-flight
- Auterion Fly View: https://docs.auterion.com/vehicle-operation/auterion-mission-control/ui-breakdown/fly/fly-view-ui-overview
- FlytBase Multi View: https://docs.flytbase.com/in-flight-modules/how-to-manage-your-flight-operations/multi-view-dashboard
- FlytBase fleet management: https://docs.flytbase.com/in-flight-modules/how-to-manage-your-flight-operations/fleet-management
- FlytBase alerts: https://docs.flytbase.com/in-flight-modules/alerts-and-notifications
- FlytBase flight logs: https://docs.flytbase.com/post-flight-modules/reviewing-your-flight-logs
- DJI FlightHub 2: https://enterprise.dji.com/flighthub-2
- DJI FlightHub 2 FAQ: https://enterprise.dji.com/flighthub-2/faq
- DroneDeploy operations management: https://help.dronedeploy.com/hc/en-us/articles/1500004861321-Drone-Operations-Management-Overview
- DroneDeploy Live Map: https://help.dronedeploy.com/hc/en-us/articles/1500004861121-Live-Map
- MAVLink common messages: https://mavlink.io/en/messages/common.html
- MAVLink command protocol: https://mavlink.io/en/services/command.html

All external sources were accessed on 2026-09-15. Product availability and
commercial tier restrictions can change; this compares documented product
patterns, not license entitlements or implementation quality.
