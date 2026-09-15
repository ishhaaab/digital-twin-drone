#!/usr/bin/env python3
"""Serial MAVLink -> UDP CSV bridge for the Unity digital-twin visualizer.

Reads MAVLink telemetry from a serial port and forwards it to Unity as comma-
separated values over UDP. It also listens on a loopback-only UDP port for
versioned, correlated commands from Unity and forwards them over MAVLink.

This module is import-safe: nothing touches hardware or sockets until main()
runs, so handle_command()/drain_commands() can be unit-tested with a fake
master/rx socket and no serial port.

CSV layout (37 fields, indices 0..36):
    0  x            NED X (north, m)         LOCAL_POSITION_NED
    1  y            NED Y (east,  m)         LOCAL_POSITION_NED
    2  z            altitude (m, +up)        LOCAL_POSITION_NED (sign-flipped)
    3  roll         degrees                  ATTITUDE
    4  pitch        degrees                  ATTITUDE
    5  yaw          degrees                  ATTITUDE
    6  status       always "1"               (reserved)
    7  battery      % (0-100)                SYS_STATUS
    8  vibration_x  m/s^2                    VIBRATION
    9  vibration_y  m/s^2                    VIBRATION
   10  vibration_z  m/s^2                    VIBRATION
   11  send ts      unix seconds (float)     local time at send
   12  flight mode  string (e.g. "LOITER")   HEARTBEAT
   13  armed        0 or 1                   HEARTBEAT
   14  voltage      V                        SYS_STATUS
   15  current      A                        SYS_STATUS
   16  satellites   count                    GPS_RAW_INT
   17  lat          degrees                  GPS_RAW_INT
   18  lon          degrees                  GPS_RAW_INT
    19  gps_alt      m (MSL)                  GPS_RAW_INT
    20  version      protocol version         local bridge
    21..26 source age seconds                 MAVLink receive times
    27  GPS fix type                          GPS_RAW_INT
    28..30 sensor present/enabled/health masks SYS_STATUS
    31  home valid                            HOME_POSITION
    32..36 home lat/lon/alt/north/east        HOME_POSITION

UDP endpoints:
    TX -> 127.0.0.1:5055   telemetry out to Unity
    RX <- 127.0.0.1:5056   commands from Unity

Usage:
    python mavlink_bridge.py
    python mavlink_bridge.py --port COM5 --baud 115200
    python mavlink_bridge.py --firmware PLANE
    python mavlink_bridge.py --ip 127.0.0.1 --tx-port 5055 --rx-port 5056
"""

import argparse
import math
import socket
import time

from pymavlink import mavutil
from telemetry_protocol import (
    PROTOCOL_VERSION,
    decode_command,
    encode_command_ack,
    encode_telemetry,
)

# ================= CONFIG =================
# Command-line defaults (override with --port/--baud/--firmware).
PORT = 'COM3'
BAUD = 57600

UDP_TX_IP = "127.0.0.1"
UDP_TX_PORT = 5055    # telemetry -> Unity (matches DroneDataReceiver.listenPort)
UDP_RX_IP = "127.0.0.1"
UDP_RX_PORT = 5056    # commands <- Unity (matches DroneDataReceiver.commandPort)

# Firmware whose flight-mode numbers we send in MAV_CMD_DO_SET_MODE.
# Default mapping is ArduCopter; swap with --firmware PLANE if you fly a plane.
FIRMWARE = "COPTER"
# ==========================================

# Request these MAVLink messages at these rates (microseconds between messages).
MESSAGE_RATES_US = {
    'LOCAL_POSITION_NED': 100000,  # 10 Hz
    'ATTITUDE':           100000,  # 10 Hz
    'SYS_STATUS':         200000,  #  5 Hz
    'VIBRATION':          100000,  # 10 Hz
    'GPS_RAW_INT':        1000000, #  1 Hz
    'HOME_POSITION':      1000000, #  1 Hz when supported
}

# ---------------------------------------------------------------------------
# Flight mode tables (ArduPilot). custom_mode values come straight off HEARTBEAT.
# ---------------------------------------------------------------------------
COPTER_MODES = {
    0: "STABILIZE", 1: "ACRO", 2: "ALT_HOLD", 3: "AUTO", 4: "GUIDED",
    5: "LOITER", 6: "RTL", 7: "CIRCLE", 8: "LAND", 11: "DRIFT",
    13: "SPORT", 14: "FLIP", 15: "AUTOTUNE", 16: "POSHOLD", 17: "BRAKE",
    18: "THROW", 19: "AVOID_ADSB", 20: "GUIDED_NOGPS", 21: "SMART_RTL",
    22: "FLOWHOLD", 23: "FOLLOW", 24: "ZIGZAG", 25: "SYSTEMID",
    26: "AUTOROTATE", 27: "AUTO_RTL",
}
PLANE_MODES = {
    0: "MANUAL", 1: "CIRCLE", 2: "STABILIZE", 3: "TRAINING", 4: "ACRO",
    5: "FBWA", 6: "FBWB", 7: "CRUISE", 8: "AUTOTUNE", 10: "AUTO",
    11: "RTL", 12: "LOITER", 13: "TAKEOFF", 14: "AVOID_ADSB", 15: "GUIDED",
    16: "INITIALISING", 17: "QSTABILIZE", 18: "QHOVER", 19: "QLOITER",
    20: "QLAND", 21: "QRTL", 22: "QAUTOTUNE", 23: "QACRO", 24: "THERMAL",
}
MODE_TABLES = {"COPTER": COPTER_MODES, "PLANE": PLANE_MODES}


def build_parser():
    """Argument parser mirroring the CLI style of udp_simulator.py."""
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--port", type=str, default=PORT,
                   help="serial port (default %s)" % PORT)
    p.add_argument("--baud", type=int, default=BAUD,
                   help="serial baud rate (default %d)" % BAUD)
    p.add_argument("--firmware", choices=sorted(MODE_TABLES), default=FIRMWARE,
                   help="firmware whose flight-mode numbers to send "
                        "(default %s)" % FIRMWARE)
    p.add_argument("--ip", type=str, default=UDP_TX_IP,
                   help="UDP host (default 127.0.0.1)")
    p.add_argument("--tx-port", type=int, default=UDP_TX_PORT,
                   help="telemetry OUT port, where Unity listens "
                        "(default %d)" % UDP_TX_PORT)
    p.add_argument("--rx-port", type=int, default=UDP_RX_PORT,
                   help="command IN port, where Unity sends LAND/modes "
                        "(default %d)" % UDP_RX_PORT)
    return p


def handle_command(master, mode_name_to_id, cmd):
    """Execute one validated command received over the command UDP port.

    `master` is the active pymavlink connection; `mode_name_to_id` maps
    flight-mode names (e.g. "LOITER") to the firmware's custom_mode number.
    Both are passed in so this is testable without hardware.
    """
    cmd = (cmd or "").strip().upper()
    if not cmd:
        return None
    # ascii-safe: garbage bytes decode to \ufffd which cp1252 can't print
    safe = cmd.encode("ascii", "backslashreplace").decode("ascii")
    print(f"CMD >>> {safe}")

    if cmd == "LAND":
        # MAV_CMD_NAV_LAND: land at the current location.
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_NAV_LAND, 0,
            0, 0, 0, 0, 0, 0, 0)
        return mavutil.mavlink.MAV_CMD_NAV_LAND

    elif cmd == "FORCE_DISARM":
        # MAV_CMD_COMPONENT_ARM_DISARM: param1=0 (disarm), param2=21196 (force).
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_COMPONENT_ARM_DISARM, 0,
            0, 21196, 0, 0, 0, 0, 0)
        return mavutil.mavlink.MAV_CMD_COMPONENT_ARM_DISARM

    elif cmd in mode_name_to_id:
        # MAV_CMD_DO_SET_MODE with the firmware-specific custom mode number.
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_DO_SET_MODE, 0,
            mavutil.mavlink.MAV_MODE_FLAG_CUSTOM_MODE_ENABLED,
            mode_name_to_id[cmd], 0, 0, 0, 0, 0)
        return mavutil.mavlink.MAV_CMD_DO_SET_MODE
    else:
        print("Unknown command:", cmd)
    return None


def drain_commands(rx_sock, master, mode_name_to_id, pending_commands=None):
    """Accept queued commands and return ACKs resolved without the autopilot."""
    local_acks = []
    while True:
        try:
            data, _ = rx_sock.recvfrom(256)
        except BlockingIOError:
            return local_acks
        decoded = decode_command(data.decode("utf-8", "replace"))
        if decoded is None:
            print("Ignored malformed command datagram")
            continue
        request_id, command = decoded
        mav_command = handle_command(master, mode_name_to_id, command)
        if mav_command is None:
            local_acks.append(encode_command_ack(
                request_id, command, mavutil.mavlink.MAV_RESULT_UNSUPPORTED))
        elif pending_commands is not None:
            pending_commands[mav_command] = (request_id, command)


def command_ack_payload(msg, pending_commands):
    """Map a MAVLink COMMAND_ACK to the matching Unity request, if any."""
    mav_command = int(msg.command)
    pending = pending_commands.get(mav_command)
    if pending is None:
        return None

    request_id, command = pending
    result = int(msg.result)
    progress = int(getattr(msg, "progress", -1))
    result_param2 = int(getattr(msg, "result_param2", 0))
    if result != mavutil.mavlink.MAV_RESULT_IN_PROGRESS:
        del pending_commands[mav_command]
    return encode_command_ack(
        request_id, command, result, progress, result_param2)


def source_age(last_seen, now):
    """Seconds since a MAVLink source message, or -1 when never observed."""
    return -1.0 if last_seen is None else max(0.0, now - last_seen)


def is_valid_coordinate(latitude, longitude):
    return (math.isfinite(latitude) and math.isfinite(longitude)
            and -90.0 <= latitude <= 90.0
            and -180.0 <= longitude <= 180.0)


def is_target_message(msg, master):
    """Accept telemetry only from the autopilot selected by wait_heartbeat()."""
    try:
        source_system = int(msg.get_srcSystem())
        source_component = int(msg.get_srcComponent())
    except (AttributeError, TypeError, ValueError):
        return False

    target_component = int(master.target_component)
    return source_system == int(master.target_system) \
        and (target_component == 0 or source_component == target_component)


def all_finite(*values):
    try:
        return all(math.isfinite(float(value)) for value in values)
    except (TypeError, ValueError):
        return False


def main():
    args = build_parser().parse_args()
    firmware = args.firmware.upper()
    mode_name_to_id = {name: num for num, name in MODE_TABLES[firmware].items()}

    print("Connecting...")
    master = mavutil.mavlink_connection(args.port, baud=args.baud)
    initial_heartbeat = master.wait_heartbeat()
    heartbeat_received_at = time.monotonic()
    print("Connected! (system %d, component %d)"
          % (master.target_system, master.target_component))

    # Request the telemetry streams Unity consumes.
    for msg_name, rate_us in MESSAGE_RATES_US.items():
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL, 0,
            getattr(mavutil.mavlink, "MAVLINK_MSG_ID_" + msg_name),
            rate_us, 0, 0, 0, 0, 0)

    # HOME_POSITION is commonly request-only even when interval requests are
    # supported for other streams. Request it once as well as asking for updates.
    master.mav.command_long_send(
        master.target_system, master.target_component,
        mavutil.mavlink.MAV_CMD_REQUEST_MESSAGE, 0,
        mavutil.mavlink.MAVLINK_MSG_ID_HOME_POSITION,
        0, 0, 0, 0, 0, 0)

    # -----------------------------------------------------------------------
    # UDP sockets
    # -----------------------------------------------------------------------
    tx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # telemetry out
    rx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # commands in
    # Commands are intentionally local-only. Remote command transport requires
    # an authenticated channel rather than widening this UDP bind.
    rx_sock.bind((UDP_RX_IP, args.rx_port))
    rx_sock.setblocking(False)
    print("Telemetry  -> UDP %s:%d" % (args.ip, args.tx_port))
    print("Commands   <- UDP %s:%d (local only)" % (UDP_RX_IP, args.rx_port))

    # -----------------------------------------------------------------------
    # Telemetry state
    # -----------------------------------------------------------------------
    # Unknown sources use neutral transport values plus age=-1. Unity makes all
    # validity decisions from source metadata, never from these placeholders.
    x = y = z = 0.0
    roll = pitch = yaw = 0.0
    battery = -1           # unknown until a valid SYS_STATUS value arrives
    voltage = -1.0
    current = -1.0
    vib_x = vib_y = vib_z = 0.0
    flight_mode = MODE_TABLES[firmware].get(
        initial_heartbeat.custom_mode, str(initial_heartbeat.custom_mode))
    armed = 1 if (initial_heartbeat.base_mode
                  & mavutil.mavlink.MAV_MODE_FLAG_SAFETY_ARMED) else 0
    satellites = -1
    gps_fix_type = 0
    gps_lat = gps_lon = gps_alt = 0.0
    sensors_present = sensors_enabled = sensors_health = 0
    home_valid = False
    home_lat = home_lon = home_alt = 0.0
    home_north = home_east = 0.0
    last_seen = {
        "position": None,
        "attitude": None,
        "system_status": None,
        "vibration": None,
        "heartbeat": heartbeat_received_at,
        "gps": None,
    }
    pending_commands = {}

    counter = 0
    last_print = time.time()

    # -----------------------------------------------------------------------
    # Main loop: poll MAVLink (with a short timeout) and drain UDP commands.
    # -----------------------------------------------------------------------
    try:
        while True:
            msg = master.recv_match(blocking=True, timeout=0.05)
            if not msg:
                for payload in drain_commands(
                        rx_sock, master, mode_name_to_id, pending_commands):
                    tx_sock.sendto(payload.encode("ascii"), (args.ip, args.tx_port))
                continue

            msg_type = msg.get_type()
            received_at = time.monotonic()
            if not is_target_message(msg, master):
                for payload in drain_commands(
                        rx_sock, master, mode_name_to_id, pending_commands):
                    tx_sock.sendto(payload.encode("ascii"), (args.ip, args.tx_port))
                continue

            # ---- LOCAL POSITION (NO GPS) ----
            if msg_type == 'LOCAL_POSITION_NED':
                if all_finite(msg.x, msg.y, msg.z):
                    x = msg.x
                    y = msg.y
                    z = -msg.z                   # invert Z (down -> up) for Unity
                    last_seen["position"] = received_at

            # ---- ATTITUDE ----
            elif msg_type == 'ATTITUDE':
                if all_finite(msg.roll, msg.pitch, msg.yaw):
                    roll = math.degrees(msg.roll)
                    pitch = math.degrees(msg.pitch)
                    yaw = math.degrees(msg.yaw)
                    last_seen["attitude"] = received_at

            # ---- BATTERY / ELECTRICAL ----
            elif msg_type == 'SYS_STATUS':
                battery = msg.battery_remaining \
                    if 0 <= msg.battery_remaining <= 100 else -1
                voltage = msg.voltage_battery / 1000.0 if msg.voltage_battery > 0 else -1.0
                current = msg.current_battery / 100.0 if msg.current_battery >= 0 else -1.0
                sensors_present = int(msg.onboard_control_sensors_present)
                sensors_enabled = int(msg.onboard_control_sensors_enabled)
                sensors_health = int(msg.onboard_control_sensors_health)
                last_seen["system_status"] = received_at

            # ---- VIBRATION (used by Unity alerts + gradient bars) ----
            elif msg_type == 'VIBRATION' and all_finite(
                    msg.vibration_x, msg.vibration_y, msg.vibration_z):
                vib_x, vib_y, vib_z = msg.vibration_x, msg.vibration_y, msg.vibration_z
                last_seen["vibration"] = received_at

            # ---- FLIGHT MODE / ARMED ----
            elif msg_type == 'HEARTBEAT':
                armed = 1 if (msg.base_mode & mavutil.mavlink.MAV_MODE_FLAG_SAFETY_ARMED) else 0
                flight_mode = MODE_TABLES[firmware].get(msg.custom_mode, str(msg.custom_mode))
                last_seen["heartbeat"] = received_at

            # ---- GPS ----
            elif msg_type == 'GPS_RAW_INT':
                gps_fix_type = int(msg.fix_type)
                satellites = -1 if msg.satellites_visible == 255 else int(msg.satellites_visible)
                candidate_lat = msg.lat / 1e7
                candidate_lon = msg.lon / 1e7
                if (gps_fix_type >= 3
                        and is_valid_coordinate(candidate_lat, candidate_lon)
                        and all_finite(msg.alt)):
                    gps_lat = candidate_lat
                    gps_lon = candidate_lon
                    gps_alt = msg.alt / 1000.0    # mm -> m (MSL)
                else:
                    gps_lat = gps_lon = gps_alt = 0.0
                last_seen["gps"] = received_at

            elif msg_type == 'HOME_POSITION':
                candidate_lat = msg.latitude / 1e7
                candidate_lon = msg.longitude / 1e7
                home_valid = (msg.latitude != 0 or msg.longitude != 0) \
                    and is_valid_coordinate(candidate_lat, candidate_lon)
                if home_valid:
                    home_lat = candidate_lat
                    home_lon = candidate_lon
                    home_alt = msg.altitude / 1000.0
                    raw_north = float(getattr(msg, "x", 0.0))
                    raw_east = float(getattr(msg, "y", 0.0))
                    home_north = raw_north if math.isfinite(raw_north) else 0.0
                    home_east = raw_east if math.isfinite(raw_east) else 0.0

            elif msg_type == 'COMMAND_ACK':
                payload = command_ack_payload(msg, pending_commands)
                if payload is not None:
                    tx_sock.sendto(payload.encode("ascii"), (args.ip, args.tx_port))

            for payload in drain_commands(
                    rx_sock, master, mode_name_to_id, pending_commands):
                tx_sock.sendto(payload.encode("ascii"), (args.ip, args.tx_port))

            # Emit a complete snapshot even when individual MAVLink sources have
            # never arrived. Their age=-1 keeps the corresponding UI explicitly
            # unavailable while connection and heartbeat remain truthful.
            now_monotonic = time.monotonic()
            message = encode_telemetry({
                "x": x, "y": y, "z": z,
                "roll": roll, "pitch": pitch, "yaw": yaw,
                "status": 1,
                "battery": battery,
                "vibration_x": vib_x, "vibration_y": vib_y, "vibration_z": vib_z,
                "send_ts": "%.3f" % time.time(),
                "flight_mode": flight_mode,
                "armed": armed,
                "voltage": "%.2f" % voltage,
                "current": "%.2f" % current,
                "satellites": satellites,
                "lat": gps_lat, "lon": gps_lon, "gps_alt": gps_alt,
                "protocol_version": PROTOCOL_VERSION,
                "position_age": "%.3f" % source_age(last_seen["position"], now_monotonic),
                "attitude_age": "%.3f" % source_age(last_seen["attitude"], now_monotonic),
                "system_status_age": "%.3f" % source_age(last_seen["system_status"], now_monotonic),
                "vibration_age": "%.3f" % source_age(last_seen["vibration"], now_monotonic),
                "heartbeat_age": "%.3f" % source_age(last_seen["heartbeat"], now_monotonic),
                "gps_age": "%.3f" % source_age(last_seen["gps"], now_monotonic),
                "gps_fix_type": gps_fix_type,
                "sensors_present": sensors_present,
                "sensors_enabled": sensors_enabled,
                "sensors_health": sensors_health,
                "home_valid": 1 if home_valid else 0,
                "home_lat": home_lat, "home_lon": home_lon, "home_alt": home_alt,
                "home_north": home_north, "home_east": home_east,
            })

            tx_sock.sendto(message.encode(), (args.ip, args.tx_port))
            counter += 1

            # ---- RATE PRINT ----
            if time.time() - last_print > 1:
                print("Sending UDP packets: %d/sec" % counter)
                counter = 0
                last_print = time.time()

    except KeyboardInterrupt:
        print("\nShutting down...")
    finally:
        tx_sock.close()
        rx_sock.close()


if __name__ == "__main__":
    main()
