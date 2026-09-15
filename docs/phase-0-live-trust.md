# Phase 0: Trustworthy Live Operation

Date: 2026-09-15

## Status

The software implementation of Phase 0 is complete. Hardware validation and
flight-controller failsafe configuration remain operational prerequisites.

## Delivered

- Protocol v2 carries source ages, GPS fix type, `SYS_STATUS` sensor masks, and
  real `HOME_POSITION` data. Unknown battery and electrical values use explicit
  sentinels instead of plausible defaults. The bridge emits partial snapshots,
  so a live link remains visible even before position or attitude is available.
- Unity rejects malformed, non-finite, wrong-version, wrong-length, and
  untrusted-source telemetry. Telemetry listens on trusted loopback by default.
- Position, attitude, heartbeat, system status, vibration, and GPS become stale
  independently. The UI shows `UNKNOWN`, `NO FIX`, `STALE`, or `--` instead of
  treating socket connectivity as sensor health.
- IMU, barometer, and GPS status use MAVLink presence, enabled, and health masks.
  The map draws a home marker only after `HOME_POSITION` is valid.
- Connection loss and alert state are evaluated every frame, including when no
  new telemetry arrives.
- Battery and vibration thresholds are alert-only. Received telemetry can no
  longer trigger `LAND`; automatic failsafes remain the flight controller's job.
- Mode controls require a live connection, fresh heartbeat, and mode-specific
  sensor prerequisites. Landing and force-disarm also require an armed vehicle.
- Commands carry request IDs. The bridge routes `COMMAND_ACK` results back to
  Unity, which exposes sent, accepted, in-progress, completed, rejected, failed,
  and timed-out states. Completion requires matching heartbeat telemetry.
- The fullscreen viewport reserves the critical top status bar.
- The panoramic sky uses a Resources-backed shader so standalone builds do not
  strip it.

The full transport contract and run instructions are in `README.md`.

## Verification

- `python -m unittest test_mavlink -v`: 18 passed, 0 failed, 0 skipped with the
  pinned `pymavlink` and `pyserial` dependencies.
- Python bytecode compilation completed without errors.
- Unity `6000.3.11f1` Windows standalone build completed with 0 errors and no C#
  or shader warnings.
- Standalone runtime accepted protocol v2 with 37 fields.
- A simulator vibration spike raised and cleared the critical alert without any
  command arriving at the simulator.
- The standalone player log contained no missing panoramic-shader error.

## Required Before Aircraft Use

1. Configure battery, radio/link-loss, geofence, and other applicable failsafes
   in the autopilot using the vehicle manufacturer's guidance. Do not depend on
   Unity, Python, UDP, or a laptop for an automatic safety action.
2. Bench test with propellers removed. Confirm the bridge reports the expected
   firmware, system/component IDs, sensor masks, GPS fix, and home position.
3. Issue each supported command during the bench test and verify both
   `COMMAND_ACK` and the subsequent heartbeat-confirmed completion state.
4. Disconnect telemetry deliberately and confirm controls lock, values become
   stale, and `LINK LOST` remains visible in normal and fullscreen views.
5. Keep Unity and the bridge on the same computer. Remote commands are
   intentionally unavailable until an authenticated transport is implemented.
