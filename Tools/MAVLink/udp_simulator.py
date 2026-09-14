#!/usr/bin/env python3
"""UDP telemetry simulator for the digital-twin drone Unity project.

Feeds fake 20-field CSV packets to the same UDP endpoint the real MAVLink
bridge uses (127.0.0.1:5055) so you can exercise the ENTIRE Unity pipeline —
position, rotation, battery, vibration, latency, GPS, mode/armed, auto-land —
with no drone hardware attached. It also listens on the command port (5056)
so Unity buttons / auto-land actually do something to the simulated drone.

Field layout matches Tools/MAVLink/mavlink_bridge.py — see the docstring
there for the field-by-field breakdown.

Usage:
    python udp_simulator.py                     # 10 Hz, 5 m / 15 s circle until Ctrl+C
    python udp_simulator.py --seconds 30        # stop after 30 seconds
    python udp_simulator.py --rate 20 --seconds 60
    python udp_simulator.py --radius 10 --period 30
    python udp_simulator.py --ip 127.0.0.1 --tx-port 5055 --rx-port 5056
"""

import argparse
import math
import socket
import time


def build_parser():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--ip", type=str, default="127.0.0.1",
                   help="UDP host (default 127.0.0.1)")
    p.add_argument("--tx-port", type=int, default=5055,
                   help="telemetry OUT port, where Unity listens (default 5055)")
    p.add_argument("--rx-port", type=int, default=5056,
                   help="command IN port, where Unity sends LAND/modes (default 5056)")
    p.add_argument("--rate", type=float, default=10.0,
                   help="packets per second (default 10)")
    p.add_argument("--seconds", type=float, default=0.0,
                   help="run duration in seconds; 0 = forever (default 0)")
    p.add_argument("--radius", type=float, default=5.0,
                   help="circle radius in meters (default 5)")
    p.add_argument("--period", type=float, default=15.0,
                   help="seconds per circle (default 15)")
    p.add_argument("--altitude", type=float, default=2.0,
                   help="cruise altitude in meters (default 2)")
    p.add_argument("--spike-after", type=float, default=10.0,
                   help="seconds after start for a vibration spike that trips "
                        "the Unity auto-land path (default 10)")
    p.add_argument("--battery-drain", type=float, default=3.0,
                   help="battery %% drained per simulated minute (default 3)")
    return p


class SimDrone:
    """A tiny simulated copter flying a circle, emitting the bridge CSV."""

    MODES = ["STABILIZE", "ALT_HOLD", "LOITER", "POSHOLD"]

    def __init__(self, radius, altitude, drain_per_min, period=15.0):
        self.radius = radius
        self.altitude = altitude
        self.angular_rate = 2.0 * math.pi / max(period, 0.1)
        self.drain_per_s = drain_per_min / 60.0
        self.battery = 100.0
        self.armed = 1
        self.landing = False
        self.land_start = 0.0
        self._landing_descent = 0.0  # cumulative descent while landing

    def snapshot(self, t, dt, spike=False):
        """Return one 20-field CSV line for elapsed time `t`."""
        # ---- slow integrated state (driven by t so dt is only a fallback) ----
        self.battery = max(0.0, self.battery - self.drain_per_s * dt)
        mode = self.MODES[int(t / 8.0) % len(self.MODES)]

        # ---- flight path: circle in NED (Unity X=east=+y, Z=north=+x, Y=up=+z) ----
        theta = t * self.angular_rate
        x = self.radius * math.cos(theta)              # north
        y = self.radius * math.sin(theta)              # east
        base_z = self.altitude + 0.3 * math.sin(t * 2.0)    # up (+) with a small bob

        if self.landing:
            self._landing_descent += 1.5 * dt
            z = max(0.0, base_z - self._landing_descent)
            if z <= 0.02:
                z = 0.0
                self.armed = 0
                self.landing = False
        else:
            self._landing_descent = 0.0
            z = base_z

        # ---- attitude ----
        yaw = math.degrees(math.atan2(math.cos(theta), -math.sin(theta)))
        roll = 8.0 * math.sin(t * 1.3)
        pitch = 5.0 * math.sin(t * 0.9)
        current = 14.0 + 6.0 * math.sin(t * 0.7)
        voltage = 16.8 * (0.25 + 0.75 * self.battery / 100.0)

        # ---- vibration: small by default; spike trips Unity's auto-land ----
        vib = 12.0 if self.landing else 2.5 + 1.5 * math.sin(t * 3.0)
        if spike:
            vib = 95.0                                # > receiver threshold (60)

        # ---- GPS: local meter offsets projected around a fixed test origin ----
        origin_lat = 28.6139
        origin_lon = 77.2090
        meters_per_degree = 111320.0
        lat = origin_lat + x / meters_per_degree
        lon = origin_lon + y / (meters_per_degree * math.cos(math.radians(origin_lat)))

        return "%.3f,%.3f,%.3f,%.2f,%.2f,%.2f,1,%d,%.2f,%.2f,%.2f,%.3f,%s,%d,%.2f,%.2f,12,%.6f,%.6f,%.3f" % (
            x, y, z,
            roll, pitch, yaw,
            int(round(self.battery)),
            vib, vib, vib,                    # vibration on all three axes
            time.time(),                      # unix seconds at send (latency ~0)
            mode, self.armed,
            voltage, current,
            lat, lon, z + 200.0,              # gps_alt (arbitrary MSL offset)
        )

    def on_command(self, cmd, t):
        """React to a command from Unity, mirroring what a copter would do."""
        cmd = (cmd or "").strip().upper()
        if not cmd:
            return
        print("  sim: command ->", cmd)
        if cmd == "LAND":
            self.landing = True
            self.land_start = t
        elif cmd == "FORCE_DISARM":
            self.armed = 0
        elif cmd in self.MODES:
            pass                               # snapshot() picks the mode by time;
                                               # real copter would switch here


def main():
    args = build_parser().parse_args()

    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # telemetry OUT
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # commands IN
    rx.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        rx.bind((args.ip, args.rx_port))
    except OSError as e:
        print("WARNING: can't bind command port %d (%s) — commands ignored" % (args.rx_port, e))
    rx.setblocking(False)

    print("Telemetry -> UDP %s:%d" % (args.ip, args.tx_port))
    print("Commands  <- UDP %s:%d" % (args.ip, args.rx_port))
    print("Ctrl+C to stop.")

    drone = SimDrone(args.radius, args.altitude, args.battery_drain, args.period)
    start = time.time()
    last_t = start
    last_print = start
    interval = 1.0 / args.rate if args.rate > 0 else 0.1
    sent = 0

    try:
        while True:
            now = time.time()
            t = now - start
            dt = min(max(now - last_t, 0.0), 0.1)
            last_t = now

            spike = 0 < args.spike_after <= t < args.spike_after + 2.5
            msg = drone.snapshot(t, dt, spike=spike)
            tx.sendto(msg.encode(), (args.ip, args.tx_port))
            sent += 1

            # Drain commands Unity sent (LAND / modes / disarm).
            while True:
                try:
                    data, _ = rx.recvfrom(256)
                except BlockingIOError:
                    break
                drone.on_command(data.decode("utf-8", "replace"), t)

            if now - last_print >= 1.0:
                print("Sending UDP packets: %d/sec" % sent)
                print("  last: %s" % msg)
                sent = 0
                last_print = now

            if args.seconds > 0 and t >= args.seconds:
                print("Done - simulated for %.1f s." % t)
                break

            time.sleep(max(0.0, interval - (time.time() - now)))
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        tx.close()
        rx.close()


if __name__ == "__main__":
    main()
