from pymavlink import mavutil
import socket
import math
import time

# ================= CONFIG =================
PORT = 'COM3'
BAUD = 57600

UDP_IP = "127.0.0.1"
UDP_PORT = 5055
# ==========================================

print("Connecting...")
master = mavutil.mavlink_connection(PORT, baud=BAUD)
master.wait_heartbeat()
print("Connected!")

# 🔥 REQUEST LOCAL POSITION + ATTITUDE
master.mav.command_long_send(
    master.target_system,
    master.target_component,
    mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,
    0,
    mavutil.mavlink.MAVLINK_MSG_ID_LOCAL_POSITION_NED,
    100000, 0, 0, 0, 0, 0
)

master.mav.command_long_send(
    master.target_system,
    master.target_component,
    mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,
    0,
    mavutil.mavlink.MAVLINK_MSG_ID_ATTITUDE,
    100000, 0, 0, 0, 0, 0
)

master.mav.command_long_send(
    master.target_system,
    master.target_component,
    mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,
    0,
    mavutil.mavlink.MAVLINK_MSG_ID_SYS_STATUS,
    100000, 0, 0, 0, 0, 0
)

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# INIT
x = y = z = None
roll = pitch = yaw = None
battery = 100

counter = 0
last_print = time.time()

while True:
    msg = master.recv_match(blocking=True)
    if not msg:
        continue

    msg_type = msg.get_type()

    # ✅ LOCAL POSITION (NO GPS)
    if msg_type == 'LOCAL_POSITION_NED':
        x = msg.x
        y = msg.y
        z = -msg.z   # 🔥 IMPORTANT: invert Z (down → up)

    # ✅ ATTITUDE
    elif msg_type == 'ATTITUDE':
        roll = math.degrees(msg.roll)
        pitch = math.degrees(msg.pitch)
        yaw = math.degrees(msg.yaw)

    # ✅ BATTERY
    elif msg_type == 'SYS_STATUS':
        battery = msg.battery_remaining

    # SEND WHEN READY
    if (x is not None and y is not None and z is not None and
        roll is not None and pitch is not None and yaw is not None):

        message = f"{x},{y},{z},{roll},{pitch},{yaw},1,{battery}"

        sock.sendto(message.encode(), (UDP_IP, UDP_PORT))

        print("SENT:", message)

        counter += 1

    # RATE PRINT
    if time.time() - last_print > 1:
        print(f"Sending UDP packets: {counter}/sec")
        counter = 0
        last_print = time.time()