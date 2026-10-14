#!/usr/bin/env python3
"""Write raw rgb24 test frames to stdout for ffmpeg (used by make_fixtures.sh).

Usage: _fixture_frames.py MODE WIDTH HEIGHT FPS SECONDS
Modes:
  edges     dark gray center, colored band (10 % of height) on each side. Each side steps
            through red, green, blue, white every 10 s; top starts at phase 0, right 1,
            bottom 2, left 3. A thin white line sweeps the center once per 10 s.
  letterbox same as edges inside a 2.39:1 active area with black bars above and below.
  hue       border hue cycles once over the whole clip.
"""
import colorsys
import sys

COLORS = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)]
GRAY = (32, 32, 32)  # #202020


def px(c, n):
    return bytes(c) * n


def picture(w, h, top, right, bottom, left, line_frac):
    """Build one w x h picture (bytes) with the given side colors and progress line."""
    b = max(1, round(h * 0.10))
    lx = min(w - 1, max(0, int(line_frac * w)))
    mid = bytearray(px(GRAY, w))
    mid[lx * 3:lx * 3 + 3] = b"\xff\xff\xff"
    mid[:b * 3] = px(left, b)
    mid[(w - b) * 3:] = px(right, b)
    rows = [px(top, w)] * b + [bytes(mid)] * (h - 2 * b) + [px(bottom, w)] * b
    return b"".join(rows)


def main():
    mode, w, h, fps, secs = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
    out = sys.stdout.buffer
    for i in range(fps * secs):
        t = i / fps
        step = int(t // 10)
        sides = [COLORS[(p + step) % 4] for p in range(4)]  # top, right, bottom, left
        frac = (t % 10) / 10
        if mode == "edges":
            out.write(picture(w, h, *sides, frac))
        elif mode == "letterbox":
            bar = 138
            black = px((0, 0, 0), w) * bar
            out.write(black + picture(w, h - 2 * bar, *sides, frac) + black)
        elif mode == "hue":
            c = tuple(round(v * 255) for v in colorsys.hsv_to_rgb(t / secs, 1, 1))
            out.write(picture(w, h, c, c, c, c, frac))
        else:
            sys.exit("unknown mode " + mode)


if __name__ == "__main__":
    main()
