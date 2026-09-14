#!/usr/bin/env python3
"""Tests for the MAVLink tooling — simulator CSV layout and bridge commands.

Covers the cross-language contract between Tools/MAVLink and the Unity
receiver (DroneDataReceiver.cs):
  * SimDrone.snapshot() always emits exactly the 20 fields the receiver parses.
  * Field 11 is a real unix send timestamp, not the sim's elapsed clock
    (a regression guard for latency/GPS-fallback).
  * Every field lands in the exact index the C# receiver expects.
  * mavlink_bridge is importable without hardware and handle_command()/drain_commands()
    dispatch to the right MAVLink message given a fake master/socket.

Run from this folder:
    python -m unittest test_mavlink -v
    python -m unittest test_mavlink -v --ip  (use any Python >= 3.8)
"""

import time
import unittest

from udp_simulator import SimDrone

try:
    import mavlink_bridge
    HAVE_BRIDGE = True
except ImportError:
    HAVE_BRIDGE = False

# Receiver index map — mirror of the comment block in DroneDataReceiver.cs:212-232.
FIELD_NAMES = (
    "x", "y", "z",            # 0..2  position (NED, z positive-up)
    "roll", "pitch", "yaw",   # 3..5  attitude (deg)
    "status",                 # 6     always "1"
    "battery",                # 7     %
    "vibration_x",            # 8
    "vibration_y",            # 9
    "vibration_z",            # 10
    "send_ts",                # 11    unix seconds
    "flight_mode",            # 12    string
    "armed",                  # 13    0/1
    "voltage", "current",     # 14..15
    "satellites",             # 16
    "lat", "lon", "gps_alt",  # 17..19
)


def parse_line(line):
    """Split one CSV telemetry line into typed values, indexed like the C# receiver."""
    parts = [p.strip() for p in line.split(",")]
    assert len(parts) == len(FIELD_NAMES), \
        "expected %d fields, got %d in: %s" % (len(FIELD_NAMES), len(parts), line)
    d = dict(zip(FIELD_NAMES, parts))
    d["send_ts"] = float(d["send_ts"])
    for k in ("x", "y", "z", "roll", "pitch", "yaw", "battery",
              "vibration_x", "vibration_y", "vibration_z",
              "voltage", "current", "satellites", "lat", "lon", "gps_alt"):
        d[k] = float(d[k])
    d["armed"] = int(d["armed"])
    return d


class SimulatorSnapshotTest(unittest.TestCase):
    def setUp(self):
        self.drone = SimDrone(radius=2.0, altitude=2.0, drain_per_min=3.0)

    def test_field_count_is_20(self):
        line = self.drone.snapshot(1.0, 0.1)
        self.assertEqual(len(line.split(",")), 20)

    def test_send_timestamp_is_unix_time_not_elapsed(self):
        # Regression for the "simulator breaks latency/GPS-fallback" bug:
        # field 11 must be a real unix timestamp near `now`, NOT the ~1 s
        # elapsed sim clock, or DroneDataReceiver discards every sample as
        # >5000 ms old and ComputeGpsQuality sticks at neutral 0.5.
        d = parse_line(self.drone.snapshot(1.0, 0.1))
        self.assertAlmostEqual(d["send_ts"], time.time(), delta=5.0)

    def test_z_is_positive_up(self):
        # At 2 m altitude d.z must read positive-up so the 3D view (posY = d.z)
        # and the Z readout agree.
        d = parse_line(self.drone.snapshot(0.5, 0.1))
        self.assertGreater(d["z"], 1.5)
        self.assertLess(d["z"], 2.6)

    def test_receiver_index_map_parses_every_field(self):
        d = parse_line(self.drone.snapshot(0.0, 0.0))
        # status flag (reserved, always "1")
        self.assertEqual(d["status"], "1")
        # battery is a clamped 0..100 int
        self.assertGreaterEqual(d["battery"], 0)
        self.assertLessEqual(d["battery"], 100)
        # mode strings are drawn from the sim's mode list
        self.assertIn(d["flight_mode"], SimDrone.MODES)
        # armed is the 0/1 int the receiver compares to "1"
        self.assertIn(d["armed"], (0, 1))
        # simulator reports a fixed 12 satellites (>= 0 => live GPS cells)
        self.assertEqual(d["satellites"], 12)
        # lat/lon near the configured origin, gps_alt = z + 200 m offset
        self.assertGreater(d["lat"], 28.6139 - 0.01)
        self.assertLess(d["lat"], 28.6139 + 0.01)
        self.assertGreaterEqual(d["gps_alt"], 200.0)
        # vib in the calm band by default
        self.assertLess(d["vibration_x"], 10.0)

    def test_landing_command_altitude_decays_and_disarms(self):
        drone = SimDrone(2.0, 2.0, 3.0)
        drone.on_command("LAND", 0.0)
        t = 0.0
        for i in range(1, 200):
            t += 0.1
            d = parse_line(drone.snapshot(t, 0.1))
        self.assertGreaterEqual(d["z"], 0.0)
        self.assertEqual(d["armed"], 0)


@unittest.skipUnless(HAVE_BRIDGE, "pymavlink not installed — run the bridge tests in the project venv")
class BridgeCommandTest(unittest.TestCase):
    """handle_command()/drain_commands() must work with no serial hardware."""

    def make_fake_master(self, calls):
        class FakeMavlink(object):
            def __init__(self):
                self.target_system = 1
                self.target_component = 1

            class mav(object):
                @staticmethod
                def command_long_send(*args):
                    calls.append(args)

        return FakeMavlink()

    def test_land_sends_MAV_CMD_NAV_LAND(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, mavlink_bridge.MODE_TABLES["COPTER"], "LAND")
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_NAV_LAND)

    def test_force_disarm_uses_component_arm_disarm(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, mavlink_bridge.MODE_TABLES["COPTER"], "FORCE_DISARM")
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_COMPONENT_ARM_DISARM)

    def test_mode_command_maps_name_to_firmware_mode(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, mavlink_bridge.MODE_TABLES["COPTER"], "LOITER")
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_DO_SET_MODE)
        # param1 = custom-mode flag, param2 = ArduCopter custom_mode for LOITER
        self.assertEqual(calls[0][4], mavutil.mavlink.MAV_MODE_FLAG_CUSTOM_MODE_ENABLED)
        self.assertEqual(calls[0][5], 5)          # COPTER_MODES[5] == "LOITER"
        # Same command under PLANE firmware must use the PLANE mode number
        self.assertEqual(mavlink_bridge.MODE_TABLES["PLANE"][5], "FBWA")

    def test_drain_commands_consumes_queue_then_blocks(self):
        from pymavlink import mavutil
        class FakeSock(object):
            def __init__(self, items):
                self.items = list(items)
            def recvfrom(self, size):
                if self.items:
                    return self.items.pop(0), None
                raise BlockingIOError("queue drained")

        calls = []
        master = self.make_fake_master(calls)
        sock = FakeSock([b"LAND", b"STABILIZE", b"^\x00garbage\xff"])
        mavlink_bridge.drain_commands(sock, master, mavlink_bridge.MODE_TABLES["COPTER"])
        # LAND + STABILIZE dispatch; the unknown junk byte is ignored
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_NAV_LAND)
        self.assertEqual(calls[1][2], mavutil.mavlink.MAV_CMD_DO_SET_MODE)
        self.assertEqual(calls[1][5], 0)    # STABILIZE mode id under COPTER


class BridgeParserTest(unittest.TestCase):
    @unittest.skipUnless(HAVE_BRIDGE, "pymavlink not installed")
    def test_defaults_match_previous_hardcoded_config(self):
        ns = mavlink_bridge.build_parser().parse_args([])
        self.assertEqual(ns.port, "COM3")
        self.assertEqual(ns.baud, 57600)
        self.assertEqual(ns.firmware, "COPTER")
        self.assertEqual(ns.ip, "127.0.0.1")
        self.assertEqual(ns.tx_port, 5055)
        self.assertEqual(ns.rx_port, 5056)

    @unittest.skipUnless(HAVE_BRIDGE, "pymavlink not installed")
    def test_firmware_choices_reject_unknown(self):
        with self.assertRaises(SystemExit):
            mavlink_bridge.build_parser().parse_args(["--firmware", "ROCKET"])


if __name__ == "__main__":
    unittest.main(verbosity=2)