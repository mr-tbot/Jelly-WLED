#!/usr/bin/env python3
"""Inspect and generate Jelly-WLED glow track segment files (docs/PROTOCOL.md section 1.2).

Subcommands: info, frame, write, dir. The `write` output is the reference vector
for the C# and Kotlin segment parsers. Standard library only.
"""
import argparse
import colorsys
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _png import write_png  # noqa: E402

MAGIC = b"JWGLOW01"
HEADER = 64
# magic, ringW, ringH, fps, flags, startFrame, frameCount, sourceSize, sourceMtimeMs,
# bandPermille, reserved, cropX, cropY, cropW, cropH, 12 reserved bytes.
FMT = "<8sHHHHIIQQHHHHHH12x"
assert struct.calcsize(FMT) == HEADER


def read_header(path):
    with open(path, "rb") as f:
        raw = f.read(HEADER)
    if len(raw) < HEADER:
        raise SystemExit(f"{path}: too short for a header ({len(raw)} bytes)")
    v = struct.unpack(FMT, raw)
    if v[0] != MAGIC:
        raise SystemExit(f"{path}: bad magic {v[0]!r}, expected {MAGIC!r}")
    keys = ["magic", "ringW", "ringH", "fps", "flags", "startFrame", "frameCount", "sourceSize",
            "sourceMtimeMs", "bandPermille", "reserved", "cropX", "cropY", "cropW", "cropH"]
    h = dict(zip(keys, v))
    h["zones"] = 2 * h["ringW"] + 2 * h["ringH"]
    h["finalized"] = bool(h["flags"] & 1)
    h["readable"] = max(0, (os.path.getsize(path) - HEADER) // (h["zones"] * 3)) if h["zones"] else 0
    return h


def cmd_info(a):
    h = read_header(a.file)
    for k in ["magic", "ringW", "ringH", "zones", "fps", "flags", "startFrame",
              "frameCount", "sourceSize", "sourceMtimeMs", "bandPermille", "cropX", "cropY",
              "cropW", "cropH"]:
        print(f"{k}: {h[k].decode() if k == 'magic' else h[k]}")
    print(f"fileSize: {os.path.getsize(a.file)}")
    print(f"readableFrames: {h['readable']}")
    print(f"finalized: {'yes' if h['finalized'] else 'no'}")


def read_frame(path, h, index):
    n = h["zones"] * 3
    with open(path, "rb") as f:
        f.seek(HEADER + index * n)
        data = f.read(n)
    if len(data) < n:
        raise SystemExit(f"frame {index} not readable (readable frames: {h['readable']})")
    return [tuple(data[i:i + 3]) for i in range(0, n, 3)]


def split_sides(h, zones):
    w, hh = h["ringW"], h["ringH"]
    return {"top": zones[:w], "right": zones[w:w + hh],
            "bottom": zones[w + hh:2 * w + hh], "left": zones[2 * w + hh:]}


def cmd_frame(a):
    h = read_header(a.file)
    zones = read_frame(a.file, h, a.index)
    sides = split_sides(h, zones)
    print(f"frame {a.index} (absolute {h['startFrame'] + a.index}) ring {h['ringW']}x{h['ringH']}")
    for name, z in sides.items():
        print(f"{name} ({len(z)}): " + " ".join(f"({r},{g},{b})" for r, g, b in z))
    if a.png:
        w, hh, s = h["ringW"], h["ringH"], 8
        canvas = [[(0, 0, 0)] * w for _ in range(hh)]
        for x, c in enumerate(sides["top"]):
            canvas[0][x] = c
        for y, c in enumerate(sides["right"]):
            canvas[y][w - 1] = c
        for k, c in enumerate(sides["bottom"]):  # ring order is right to left
            canvas[hh - 1][w - 1 - k] = c
        for k, c in enumerate(sides["left"]):  # ring order is bottom to top
            canvas[hh - 1 - k][0] = c
        rows = []
        for row in canvas:
            line = b"".join(bytes(c) * s for c in row)
            rows.extend([line] * s)
        write_png(a.png, w * s, hh * s, rows)
        print(f"wrote {a.png}")


def hsv(hue):
    r, g, b = colorsys.hsv_to_rgb(hue % 1.0, 1.0, 1.0)
    return (round(r * 255), round(g * 255), round(b * 255))


def cmd_write(a):
    try:
        w, hh = (int(x) for x in a.ring.lower().split("x"))
    except ValueError:
        raise SystemExit("--ring must look like 32x18")
    header = struct.pack(FMT, MAGIC, w, hh, a.fps, 1, a.start, a.frames, a.source_size,
                         a.source_mtime_ms, 80, 0, 0, 0, 1000, 1000)
    colors = {"top": (255, 0, 0), "right": (0, 255, 0), "bottom": (0, 60, 255), "left": (255, 200, 0)}
    counts = [("top", w), ("right", hh), ("bottom", w), ("left", hh)]
    with open(a.file, "wb") as f:
        f.write(header)
        for i in range(a.frames):
            if a.pattern == "sides":
                frame = b"".join(bytes(colors[s]) * n for s, n in counts)
            else:
                frame = bytes(hsv(i / a.frames)) * (2 * w + 2 * hh)
            f.write(frame)
    print(f"wrote {a.file}: ring {w}x{hh} fps {a.fps} start {a.start} frames {a.frames} pattern {a.pattern}")


def cmd_dir(a):
    found = []
    for root, _, files in os.walk(a.dir):
        for name in files:
            if name.endswith(".seg") or name.endswith(".seg.part"):
                found.append(os.path.join(root, name))
    if not found:
        print("no segments found")
    for p in sorted(found):
        try:
            h = read_header(p)
            print(f"{os.path.relpath(p, a.dir)}: start={h['startFrame']} readable={h['readable']} "
                  f"finalized={'yes' if h['finalized'] else 'no'} ring={h['ringW']}x{h['ringH']} fps={h['fps']}")
        except SystemExit as e:
            print(f"{os.path.relpath(p, a.dir)}: INVALID ({e})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("info", help="print header fields, readable frames, finalized flag")
    p.add_argument("file")
    p.set_defaults(fn=cmd_info)
    p = sub.add_parser("frame", help="print one frame's ring grouped by side")
    p.add_argument("file")
    p.add_argument("index", type=int)
    p.add_argument("--png", help="also write a PNG of the ring drawn as a border (8x scale)")
    p.set_defaults(fn=cmd_frame)
    p = sub.add_parser("write", help="create a synthetic finalized segment")
    p.add_argument("file")
    p.add_argument("--ring", default="32x18")
    p.add_argument("--fps", type=int, default=10)
    p.add_argument("--start", type=int, default=0)
    p.add_argument("--frames", type=int, required=True)
    p.add_argument("--pattern", choices=["sides", "gradient"], default="sides")
    p.add_argument("--source-size", type=int, default=0)
    p.add_argument("--source-mtime-ms", type=int, default=0)
    p.set_defaults(fn=cmd_write)
    p = sub.add_parser("dir", help="list .seg and .seg.part files under a track directory")
    p.add_argument("dir")
    p.set_defaults(fn=cmd_dir)
    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
