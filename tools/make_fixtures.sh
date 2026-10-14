#!/usr/bin/env bash
# Generate test videos with known edge colors into tools/fixtures/movies/<Name>/<Name>.<ext>.
# Idempotent: existing files are skipped unless --force is given.
set -euo pipefail
if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: $0 [--force]   Generate Edges 60s, Letterbox 30s and Short 10s fixtures with ffmpeg."
  exit 0
fi
FORCE=0; [[ "${1:-}" == "--force" ]] && FORCE=1
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/fixtures/movies"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found" >&2; exit 1; }

cat <<'TXT'
Color schedule (edges, letterbox): every 10 s each side steps red -> green -> blue -> white -> red.
  t=  0-10 s: top=red   right=green bottom=blue  left=white
  t= 10-20 s: top=green right=blue  bottom=white left=red
  t= 20-30 s: top=blue  right=white bottom=red   left=green
  t= 30-40 s: top=white right=red   bottom=green left=blue
  t= 40-50 s: top=red   right=green bottom=blue  left=white   (repeats)
  Center is #202020; a white vertical line sweeps the center left to right once per 10 s.
  Letterbox: black bars 138 px top and bottom; bands sit inside the 1920x804 active picture.
Short: border hue = t / 10 s (red at 0 s, back to red at 10 s).
TXT

# make NAME EXT MODE W H FPS SECS ffmpeg-encoder-args...
make() {
  local name="$1" ext="$2" mode="$3" w="$4" h="$5" fps="$6" secs="$7"; shift 7
  local dir="$OUT/$name" file="$OUT/$name/$name.$ext"
  if [[ -f "$file" && $FORCE -eq 0 ]]; then echo "skip (exists): $file"; return; fi
  mkdir -p "$dir"
  echo "making $file"
  python3 -I "$HERE/_fixture_frames.py" "$mode" "$w" "$h" "$fps" "$secs" |
    ffmpeg -hide_banner -loglevel error -y -f rawvideo -pix_fmt rgb24 -s "${w}x${h}" -r "$fps" -i - \
      "$@" "$file"
}

make "Edges 60s" mp4 edges 1280 720 24 60 -c:v libx264 -pix_fmt yuv420p -preset veryfast -crf 23
make "Letterbox 30s" mp4 letterbox 1920 1080 24 30 -c:v libx264 -pix_fmt yuv420p -preset veryfast -crf 23
if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q libx265; then
  SHORTENC=(-c:v libx265 -x265-params log-level=error)
else
  SHORTENC=(-c:v libx264)
fi
make "Short 10s" mkv hue 640 360 30 10 "${SHORTENC[@]}" -pix_fmt yuv420p -preset veryfast -crf 23
echo done
