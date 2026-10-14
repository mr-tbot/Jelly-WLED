#!/usr/bin/env python3
"""WLED stand-in for testing Jelly-WLED without hardware.

Receives DDP (docs/PROTOCOL.md section 5) on UDP, reassembles frames by byte offset
(a packet with the push flag completes the frame) and prints one summary line per
interval. A small HTTP server mimics the WLED JSON API (/json/info, /json/state, /json).

Sides are derived from --top/--right/--bottom/--left in strip order: the first
`top` LEDs are the top side, then `right`, then `bottom`, then `left`. The simulator
does not know the start corner or direction, so with a rotated or reversed strip the
labels will not match the real sides; the totals and led0 still do.
Standard library only; runs with `python3 -I`.
"""
import argparse
import json
import socket
import struct
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _png import write_png  # noqa: E402


class State:
    def __init__(self, total):
        self.lock = threading.Lock()
        self.total = total
        self.on = True
        self.bri = 128
        self.live = False
        self.sender = ""
        self.end_requested = False


def avg(frame, a, b):
    n = max(0, min(b, len(frame) // 3) - a)
    if n == 0:
        return (0, 0, 0)
    s = [0, 0, 0]
    for i in range(a, a + n):
        for c in range(3):
            s[c] += frame[i * 3 + c]
    return tuple(round(v / n) for v in s)


def make_handler(st):
    class H(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def _info(self):
            return {"ver": "0.16.0-sim", "vid": 2606300, "name": "ddp-sim",
                    "leds": {"count": st.total}, "live": st.live, "lm": "DDP" if st.live else "",
                    "lip": st.sender if st.live else "", "ip": "127.0.0.1"}

        def _state(self):
            return {"on": st.on, "bri": st.bri, "live": st.live}

        def _send(self, obj):
            body = json.dumps(obj, separators=(",", ":")).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            print(f"http: GET {self.path}", flush=True)
            with st.lock:
                if self.path == "/json/info":
                    self._send(self._info())
                elif self.path == "/json/state":
                    self._send(self._state())
                elif self.path in ("/json", "/json/"):
                    self._send({"state": self._state(), "info": self._info()})
                else:
                    self.send_error(404)

        def do_POST(self):
            n = int(self.headers.get("Content-Length") or 0)
            raw = self.rfile.read(n).decode("utf-8", "replace")
            print(f"http: POST {self.path} {raw}", flush=True)
            if self.path != "/json/state":
                self.send_error(404)
                return
            try:
                d = json.loads(raw or "{}")
            except ValueError:
                self.send_error(400)
                return
            with st.lock:
                if "on" in d:
                    st.on = bool(d["on"])
                if "bri" in d:
                    st.bri = max(0, min(255, int(d["bri"])))
                if d.get("live") is False:
                    st.end_requested = True
                self._send({"success": True})

    return H


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=4048, help="UDP DDP port (default 4048)")
    ap.add_argument("--http-port", type=int, default=8080, help="HTTP port, 0 disables (default 8080)")
    ap.add_argument("--top", type=int, default=32)
    ap.add_argument("--right", type=int, default=18)
    ap.add_argument("--bottom", type=int, default=32)
    ap.add_argument("--left", type=int, default=18)
    ap.add_argument("--interval", type=float, default=1.0, help="seconds between summary lines")
    ap.add_argument("--timeout", type=float, default=2.5, help="realtime timeout in seconds")
    ap.add_argument("--csv", help="append one row per received frame to this CSV")
    ap.add_argument("--png", help="directory for strip PNG snapshots")
    ap.add_argument("--png-every", type=float, default=2.0)
    a = ap.parse_args()

    counts = [a.top, a.right, a.bottom, a.left]
    st = State(sum(counts))
    if a.http_port:
        srv = ThreadingHTTPServer(("0.0.0.0", a.http_port), make_handler(st))
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        print(f"http: listening on :{a.http_port}", flush=True)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("0.0.0.0", a.port))
    sock.settimeout(0.1)
    print(f"ddp: listening on udp :{a.port}, {st.total} LEDs", flush=True)

    csv = None
    if a.csv:
        new = not os.path.exists(a.csv) or os.path.getsize(a.csv) == 0
        csv = open(a.csv, "a", buffering=1)
        if new:
            csv.write("unix_ms,frame_index,led_count,top_r,top_g,top_b,right_r,right_g,right_b,"
                      "bottom_r,bottom_g,bottom_b,left_r,left_g,left_b,led0_r,led0_g,led0_b\n")
    if a.png:
        os.makedirs(a.png, exist_ok=True)

    buf = bytearray()
    last = b""
    frames = 0       # frames in the current interval
    frame_index = 0
    last_pkt = 0.0
    t_print = time.monotonic()
    t_png = t_print
    try:
        while True:
            now = time.monotonic()
            try:
                data, addr = sock.recvfrom(65535)
            except socket.timeout:
                data = None
            if data is not None and len(data) >= 10:
                flags, _seq, _dt, _dest, off, ln = struct.unpack(">BBBBIH", data[:10])
                if flags & 0xC0 == 0x40:
                    payload = data[10:10 + ln]
                    with st.lock:
                        if not st.live:
                            st.live = True
                            st.sender = addr[0]
                            print(f"realtime: started from {addr[0]}", flush=True)
                    last_pkt = now
                    if len(buf) < off + len(payload):
                        buf.extend(bytes(off + len(payload) - len(buf)))
                    buf[off:off + len(payload)] = payload
                    if flags & 0x01:
                        last = bytes(buf)
                        buf = bytearray()
                        frames += 1
                        frame_index += 1
                        if csv:
                            led0c, v = edges(counts, last)
                            row = [int(time.time() * 1000), frame_index, len(last) // 3]
                            for t in v:
                                row.extend(t)
                            row.extend(led0c)
                            csv.write(",".join(str(x) for x in row) + "\n")
            with st.lock:
                if st.live and (st.end_requested or now - last_pkt > a.timeout):
                    print("realtime: ended (live:false)" if st.end_requested else "realtime: ended (timeout)",
                          flush=True)
                    st.live = False
                    buf = bytearray()
                st.end_requested = False
                live = st.live
            if now - t_print >= a.interval:
                dt = now - t_print
                led0, sides = edges(counts, last)
                print("fps=%.1f leds=%d top=%s right=%s bottom=%s left=%s led0=%s live=%s" % (
                    frames / dt, len(last) // 3, fmt(sides[0]), fmt(sides[1]), fmt(sides[2]),
                    fmt(sides[3]), fmt(led0), live), flush=True)
                frames = 0
                t_print = now
            if a.png and last and now - t_png >= a.png_every:
                t_png = now
                n = len(last) // 3
                row = b"".join(last[i * 3:i * 3 + 3] * 4 for i in range(n))
                write_png(os.path.join(a.png, "ddp_%d.png" % int(time.time() * 1000)), n * 4, 16, [row] * 16)
    except KeyboardInterrupt:
        print("shutting down", flush=True)
    finally:
        if csv:
            csv.close()
        sock.close()


def fmt(t):
    return "(%d,%d,%d)" % t


def edges(counts, frame):
    """Return (led0 rgb, [top, right, bottom, left] average rgb)."""
    led0 = tuple(frame[:3]) if len(frame) >= 3 else (0, 0, 0)
    out, pos = [], 0
    for c in counts:
        out.append(avg(frame, pos, pos + c))
        pos += c
    return led0, out


if __name__ == "__main__":
    main()
