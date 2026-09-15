#!/usr/bin/env python3
"""Tests for the MAVLink tooling — simulator CSV layout and bridge commands.

Covers the cross-language contract between Tools/MAVLink and the Unity
receiver (DroneDataReceiver.cs):
  * SimDrone.snapshot() always emits the full versioned contract Unity parses.
  * Field 11 is a real unix send timestamp, not the sim's elapsed clock
    (a regression guard for latency reporting).
  * Every field lands in the exact index the C# receiver expects.
  * mavlink_bridge is importable without hardware and handle_command()/drain_commands()
    dispatch to the right MAVLink message given a fake master/socket.

Run from this folder:
    python -m unittest test_mavlink -v
    python -m unittest test_mavlink -v --ip  (use any Python >= 3.8)
"""

import math
import time
import unittest

from udp_simulator import SimDrone, build_parser
from telemetry_protocol import (
    FIELD_NAMES,
    PROTOCOL_VERSION,
    TELEMETRY_FIELD_COUNT,
    decode_command,
)

try:
    import mavlink_bridge
    HAVE_BRIDGE = True
except ImportError:
    HAVE_BRIDGE = False

def parse_line(line):
    """Split one CSV telemetry line into typed values, indexed like the C# receiver."""
    parts = [p.strip() for p in line.split(",")]
    assert len(parts) == len(FIELD_NAMES), \
        "expected %d fields, got %d in: %s" % (len(FIELD_NAMES), len(parts), line)
    d = dict(zip(FIELD_NAMES, parts))
    d["send_ts"] = float(d["send_ts"])
    for k in ("x", "y", "z", "roll", "pitch", "yaw", "battery",
              "vibration_x", "vibration_y", "vibration_z",
              "voltage", "current", "satellites", "lat", "lon", "gps_alt",
              "position_age", "attitude_age", "system_status_age",
              "vibration_age", "heartbeat_age", "gps_age",
              "home_lat", "home_lon", "home_alt", "home_north", "home_east"):
        d[k] = float(d[k])
    d["armed"] = int(d["armed"])
    for k in ("protocol_version", "gps_fix_type", "sensors_present",
              "sensors_enabled", "sensors_health", "home_valid"):
        d[k] = int(d[k])
    return d


class SimulatorSnapshotTest(unittest.TestCase):
    def setUp(self):
        self.drone = SimDrone(radius=2.0, altitude=2.0, drain_per_min=3.0)

    def test_simulator_runs_until_stopped_by_default(self):
        args = build_parser().parse_args([])
        self.assertEqual(args.seconds, 0.0)
        self.assertEqual(args.radius, 5.0)
        self.assertEqual(args.period, 15.0)

    def test_gps_offsets_match_local_position_meters(self):
        drone = SimDrone(radius=5.0, altitude=2.0, drain_per_min=3.0, period=15.0)
        north = parse_line(drone.snapshot(0.0, 0.0))
        north_meters = (north["lat"] - 28.6139) * 111320.0
        self.assertAlmostEqual(north_meters, 5.0, delta=0.1)

        east = parse_line(drone.snapshot(15.0 / 4.0, 0.1))
        east_meters = ((east["lon"] - 77.2090) * 111320.0
                       * math.cos(math.radians(28.6139)))
        self.assertAlmostEqual(east_meters, 5.0, delta=0.1)

    def test_field_count_matches_protocol_v2(self):
        line = self.drone.snapshot(1.0, 0.1)
        self.assertEqual(len(line.split(",")), TELEMETRY_FIELD_COUNT)
        self.assertEqual(TELEMETRY_FIELD_COUNT, 37)

    def test_send_timestamp_is_unix_time_not_elapsed(self):
        # Regression for the "simulator breaks latency/GPS-fallback" bug:
        # field 11 must be a real unix timestamp near `now`, NOT the ~1 s
        # elapsed sim clock, or DroneDataReceiver discards every latency sample
        # as more than 5000 ms old.
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
        self.assertEqual(d["protocol_version"], PROTOCOL_VERSION)
        self.assertEqual(d["gps_fix_type"], 3)
        self.assertEqual(d["home_valid"], 1)
        self.assertEqual(d["home_north"], 0.0)
        self.assertEqual(d["home_east"], 0.0)
        self.assertEqual(
            d["sensors_health"] & (1 | 2 | 8 | 32),
            1 | 2 | 8 | 32,
        )

    def test_landing_command_altitude_decays_and_disarms(self):
        drone = SimDrone(2.0, 2.0, 3.0)
        drone.on_command("LAND", 0.0)
        t = 0.0
        for i in range(1, 200):
            t += 0.1
            d = parse_line(drone.snapshot(t, 0.1))
        self.assertGreaterEqual(d["z"], 0.0)
        self.assertEqual(d["armed"], 0)

    def test_mode_command_is_reflected_in_telemetry(self):
        self.assertTrue(self.drone.on_command("POSHOLD", 0.0))
        d = parse_line(self.drone.snapshot(1.0, 0.1))
        self.assertEqual(d["flight_mode"], "POSHOLD")

    def test_command_datagram_requires_version_and_request_id(self):
        self.assertEqual(decode_command("CMD,2,req-7,LAND"), ("req-7", "LAND"))
        self.assertIsNone(decode_command("LAND"))
        self.assertIsNone(decode_command("CMD,1,req-7,LAND"))
        self.assertIsNone(decode_command("CMD,2,req-7," + "A" * 33))


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

    def copter_modes(self):
        return {name: number for number, name in mavlink_bridge.COPTER_MODES.items()}

    def test_land_sends_MAV_CMD_NAV_LAND(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, self.copter_modes(), "LAND")
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_NAV_LAND)

    def test_force_disarm_uses_component_arm_disarm(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, self.copter_modes(), "FORCE_DISARM")
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_COMPONENT_ARM_DISARM)

    def test_mode_command_maps_name_to_firmware_mode(self):
        from pymavlink import mavutil
        calls = []
        master = self.make_fake_master(calls)
        mavlink_bridge.handle_command(master, self.copter_modes(), "LOITER")
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
        sock = FakeSock([
            b"CMD,2,req-land,LAND",
            b"CMD,2,req-mode,STABILIZE",
            b"CMD,2,req-bogus,BOGUS",
            b"^\x00garbage\xff",
        ])
        pending = {}
        local_acks = mavlink_bridge.drain_commands(
            sock, master, self.copter_modes(), pending)
        # LAND + STABILIZE dispatch; the unknown junk byte is ignored
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[0][2], mavutil.mavlink.MAV_CMD_NAV_LAND)
        self.assertEqual(calls[1][2], mavutil.mavlink.MAV_CMD_DO_SET_MODE)
        self.assertEqual(calls[1][5], 0)    # STABILIZE mode id under COPTER
        self.assertEqual(
            pending[mavutil.mavlink.MAV_CMD_NAV_LAND],
            ("req-land", "LAND"),
        )
        self.assertEqual(local_acks, ["ACK,2,req-bogus,BOGUS,3,-1,0"])

    def test_command_ack_is_correlated_and_terminal_ack_clears_pending(self):
        from pymavlink import mavutil

        class FakeAck(object):
            command = mavutil.mavlink.MAV_CMD_NAV_LAND
            result = mavutil.mavlink.MAV_RESULT_ACCEPTED
            progress = 100
            result_param2 = 0

        pending = {FakeAck.command: ("req-land", "LAND")}
        payload = mavlink_bridge.command_ack_payload(FakeAck(), pending)
        self.assertEqual(payload, "ACK,2,req-land,LAND,0,100,0")
        self.assertNotIn(FakeAck.command, pending)

    def test_only_selected_autopilot_messages_are_accepted(self):
        class FakeMessage(object):
            def __init__(self, system, component):
                self.system = system
                self.component = component
            def get_srcSystem(self):
                return self.system
            def get_srcComponent(self):
                return self.component

        master = self.make_fake_master([])
        self.assertTrue(mavlink_bridge.is_target_message(FakeMessage(1, 1), master))
        self.assertFalse(mavlink_bridge.is_target_message(FakeMessage(2, 1), master))
        self.assertFalse(mavlink_bridge.is_target_message(FakeMessage(1, 42), master))

    def test_nonfinite_source_values_are_rejected(self):
        self.assertTrue(mavlink_bridge.all_finite(1.0, 2, "3.5"))
        self.assertFalse(mavlink_bridge.all_finite(float("nan")))
        self.assertFalse(mavlink_bridge.all_finite(None))


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
