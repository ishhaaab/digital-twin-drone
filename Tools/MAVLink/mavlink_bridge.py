#!/usr/bin/env python3
"""Serial MAVLink -> UDP CSV bridge for the Unity digital-twin visualizer.

Reads MAVLink telemetry from a serial port and forwards it to Unity as comma-
separated values over UDP. Also listens on a second UDP port for plain-text
commands from Unity ("LAND", "STABILIZE", ...) and forwards them back to the
vehicle over MAVLink.

This module is import-safe: nothing touches hardware or sockets until main()
runs, so handle_command()/drain_commands() can be unit-tested with a fake
master/rx socket and no serial port.

CSV layout (20 fields, indices 0..19):
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
    """Execute one plain-text command received over the command UDP port.

    `master` is the active pymavlink connection; `mode_name_to_id` maps
    flight-mode names (e.g. "LOITER") to the firmware's custom_mode number.
    Both are passed in so this is testable without hardware.
    """
    cmd = (cmd or "").strip().upper()
    if not cmd:
        return
    print("CMD >>>", cmd)

    if cmd == "LAND":
        # MAV_CMD_NAV_LAND: land at the current location.
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_NAV_LAND, 0,
            0, 0, 0, 0, 0, 0, 0)

    elif cmd == "FORCE_DISARM":
        # MAV_CMD_COMPONENT_ARM_DISARM: param1=0 (disarm), param2=21196 (force).
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_COMPONENT_ARM_DISARM, 0,
            0, 21196, 0, 0, 0, 0, 0)

    elif cmd in mode_name_to_id:
        # MAV_CMD_DO_SET_MODE with the firmware-specific custom mode number.
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_DO_SET_MODE, 0,
            mavutil.mavlink.MAV_MODE_FLAG_CUSTOM_MODE_ENABLED,
            mode_name_to_id[cmd], 0, 0, 0, 0, 0)

    else:
        print("Unknown command:", cmd)


def drain_commands(rx_sock, master, mode_name_to_id):
    """Accept every queued UDP command datagram (non-blocking)."""
    while True:
        try:
            data, _ = rx_sock.recvfrom(256)
        except BlockingIOError:
            return
        handle_command(master, mode_name_to_id,
                       data.decode("utf-8", "replace"))


def main():
    args = build_parser().parse_args()
    firmware = args.firmware.upper()
    mode_name_to_id = {name: num for num, name in MODE_TABLES[firmware].items()}

    print("Connecting...")
    master = mavutil.mavlink_connection(args.port, baud=args.baud)
    master.wait_heartbeat()
    print("Connected! (system %d, component %d)"
          % (master.target_system, master.target_component))

    # Request the telemetry streams Unity consumes.
    for msg_name, rate_us in MESSAGE_RATES_US.items():
        master.mav.command_long_send(
            master.target_system, master.target_component,
            mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL, 0,
            getattr(mavutil.mavlink, "MAVLINK_MSG_ID_" + msg_name),
            rate_us, 0, 0, 0, 0, 0)

    # -----------------------------------------------------------------------
    # UDP sockets
    # -----------------------------------------------------------------------
    tx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # telemetry out
    rx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # commands in
    rx_sock.bind((args.ip, args.rx_port))
    rx_sock.setblocking(False)
    print("Telemetry  -> UDP %s:%d" % (args.ip, args.tx_port))
    print("Commands   <- UDP %s:%d" % (args.ip, args.rx_port))

    # -----------------------------------------------------------------------
    # Telemetry state
    # -----------------------------------------------------------------------
    x = y = z = None
    roll = pitch = yaw = None
    battery = 100          # assumed until a SYS_STATUS arrives
    voltage = 0.0
    current = 0.0
    vib_x = vib_y = vib_z = 0.0
    flight_mode = "UNKNOWN"
    armed = 0
    satellites = -1        # -1 = unknown (receiver treats it as "no satellite data")
    gps_lat = gps_lon = gps_alt = 0.0   # keep 0 until first GPS fix

    counter = 0
    last_print = time.time()

    # -----------------------------------------------------------------------
    # Main loop: poll MAVLink (with a short timeout) and drain UDP commands.
    # -----------------------------------------------------------------------
    try:
        while True:
            msg = master.recv_match(blocking=True, timeout=0.05)
            if not msg:
                drain_commands(rx_sock, master, mode_name_to_id)
                continue

            msg_type = msg.get_type()

            # ---- LOCAL POSITION (NO GPS) ----
            if msg_type == 'LOCAL_POSITION_NED':
                x = msg.x
                y = msg.y
                z = -msg.z                       # invert Z (down -> up) for Unity

            # ---- ATTITUDE ----
            elif msg_type == 'ATTITUDE':
                roll = math.degrees(msg.roll)
                pitch = math.degrees(msg.pitch)
                yaw = math.degrees(msg.yaw)

            # ---- BATTERY / ELECTRICAL ----
            elif msg_type == 'SYS_STATUS':
                if msg.battery_remaining != -1:
                    battery = msg.battery_remaining
                if msg.voltage_battery != 0:     # 0 = sensor unknown, keep last
                    voltage = msg.voltage_battery / 1000.0     # mV -> V
                if msg.current_battery != 0:     # 0 = sensor unknown, keep last
                    current = msg.current_battery / 100.0      # cA -> A

            # ---- VIBRATION (used by Unity auto-land + gradient bars) ----
            elif msg_type == 'VIBRATION' and msg.vibration_x is not None:
                vib_x, vib_y, vib_z = msg.vibration_x, msg.vibration_y, msg.vibration_z

            # ---- FLIGHT MODE / ARMED ----
            elif msg_type == 'HEARTBEAT':
                armed = 1 if (msg.base_mode & mavutil.mavlink.MAV_MODE_FLAG_SAFETY_ARMED) else 0
                flight_mode = MODE_TABLES[firmware].get(msg.custom_mode, str(msg.custom_mode))

            # ---- GPS ----
            elif msg_type == 'GPS_RAW_INT' and msg.fix_type >= 3:
                gps_lat = msg.lat / 1e7          # 1e-7 deg -> deg
                gps_lon = msg.lon / 1e7
                gps_alt = msg.alt / 1000.0       # mm -> m (MSL)
                satellites = msg.satellites_visible

            drain_commands(rx_sock, master, mode_name_to_id)

            # ---- SEND WHEN ALL 3D + ATTITUDE KNOWN ----
            if (x is not None and y is not None and z is not None and
                    roll is not None and pitch is not None and yaw is not None):

                message = "%s,%s,%s,%s,%s,%s,1,%s,%s,%s,%s,%.3f,%s,%s,%s,%s,%s,%s,%s,%s" % (
                    x, y, z, roll, pitch, yaw,
                    battery,
                    vib_x, vib_y, vib_z,
                    time.time(),                 # unix seconds at send
                    flight_mode, armed,
                    "%.2f" % voltage, "%.2f" % current,
                    satellites,
                    gps_lat, gps_lon, gps_alt,
                )

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