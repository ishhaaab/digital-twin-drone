#!/usr/bin/env python3
"""Shared wire-format helpers for the MAVLink bridge and local simulator."""

PROTOCOL_VERSION = 2

# The first 20 fields retain the original transport order. Version 2 appends
# source freshness and validity metadata so Unity never has to infer validity
# from a plausible-looking numeric default.
FIELD_NAMES = (
    "x", "y", "z",
    "roll", "pitch", "yaw",
    "status",
    "battery",
    "vibration_x", "vibration_y", "vibration_z",
    "send_ts",
    "flight_mode",
    "armed",
    "voltage", "current",
    "satellites",
    "lat", "lon", "gps_alt",
    "protocol_version",
    "position_age", "attitude_age", "system_status_age",
    "vibration_age", "heartbeat_age", "gps_age",
    "gps_fix_type",
    "sensors_present", "sensors_enabled", "sensors_health",
    "home_valid", "home_lat", "home_lon", "home_alt",
    "home_north", "home_east",
)

TELEMETRY_FIELD_COUNT = len(FIELD_NAMES)


def encode_telemetry(values):
    """Encode one complete protocol-v2 telemetry snapshot."""
    missing = [name for name in FIELD_NAMES if name not in values]
    if missing:
        raise ValueError("missing telemetry fields: " + ", ".join(missing))

    encoded = []
    for name in FIELD_NAMES:
        value = str(values[name])
        if "," in value or "\r" in value or "\n" in value:
            raise ValueError("invalid telemetry value for %s" % name)
        encoded.append(value)
    return ",".join(encoded)


def decode_command(payload):
    """Return ``(request_id, command)`` for a valid v2 command datagram."""
    parts = (payload or "").strip().split(",")
    if len(parts) != 4 or parts[0] != "CMD" or parts[1] != str(PROTOCOL_VERSION):
        return None

    request_id = parts[2].strip()
    command = parts[3].strip().upper()
    if not request_id or len(request_id) > 64 or not command or len(command) > 32:
        return None
    if not all(ch.isalnum() or ch in "-_" for ch in request_id):
        return None
    if not all(ch.isalnum() or ch == "_" for ch in command):
        return None
    return request_id, command


def encode_command_ack(request_id, command, result, progress=-1, result_param2=0):
    """Encode a discrete command acknowledgement event for Unity."""
    return "ACK,%d,%s,%s,%d,%d,%d" % (
        PROTOCOL_VERSION,
        request_id,
        command,
        int(result),
        int(progress),
        int(result_param2),
    )
