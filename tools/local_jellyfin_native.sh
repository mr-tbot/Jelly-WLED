#!/usr/bin/env bash
# Runs a portable (non-Docker) Jellyfin 12.1 for local testing of the plugin.
# Usage: tools/local_jellyfin_native.sh install <tarball> | up | down | logs | status | url
# Data lives under .agent/jf-native/ (gitignored). The built plugin folder dist/plugin is linked into
# the plugins directory as Jelly-WLED_dev, so `tools/package_plugin.sh` followed by `down`/`up` reloads it.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE="$ROOT/.agent/jf-native"
APP="$BASE/app"
DATA="$BASE/data"
PORT="${JF_NATIVE_PORT:-8096}"
PIDFILE="$BASE/jellyfin.pid"
LOG="$BASE/jellyfin.out"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

case "${1:-}" in
  install)
    tarball="${2:?tarball path}"
    mkdir -p "$APP" "$DATA"
    tar -xJf "$tarball" -C "$APP" --strip-components=1
    ls "$APP/jellyfin" "$APP/jellyfin-web/index.html" >/dev/null
    echo "installed to $APP"
    ;;
  up)
    [ -x "$APP/jellyfin" ] || { echo "run: $0 install <jellyfin_12.1-amd64.tar.xz>"; exit 1; }
    mkdir -p "$DATA/plugins" "$DATA/config" "$DATA/cache" "$DATA/log"
    rm -rf "$DATA/plugins/Jelly-WLED_dev"
    mkdir -p "$ROOT/dist/plugin"
    cp -r "$ROOT/dist/plugin" "$DATA/plugins/Jelly-WLED_dev"
    if [ -f "$PIDFILE" ] && kill -0 "$(cat "$PIDFILE")" 2>/dev/null; then echo "already running (pid $(cat "$PIDFILE"))"; exit 0; fi
    nohup "$APP/jellyfin" --datadir "$DATA" --configdir "$DATA/config" --cachedir "$DATA/cache" --logdir "$DATA/log" \
      --webdir "$APP/jellyfin-web" --ffmpeg "$(command -v ffmpeg)" >"$LOG" 2>&1 &
    echo $! > "$PIDFILE"
    for i in $(seq 1 60); do
      if curl -fsS "http://127.0.0.1:$PORT/System/Info/Public" >/dev/null 2>&1; then echo "up: http://127.0.0.1:$PORT"; exit 0; fi
      sleep 1
    done
    echo "did not answer within 60 s; see $LOG"; exit 1
    ;;
  down)
    if [ -f "$PIDFILE" ]; then kill "$(cat "$PIDFILE")" 2>/dev/null || true; sleep 2; kill -9 "$(cat "$PIDFILE")" 2>/dev/null || true; rm -f "$PIDFILE"; echo "stopped"; else echo "not running"; fi
    ;;
  logs)
    tail -n "${2:-50}" -f "$DATA/log/"log_*.log 2>/dev/null || tail -f "$LOG"
    ;;
  status)
    curl -fsS "http://127.0.0.1:$PORT/System/Info/Public" || echo "down"
    ;;
  url)
    echo "http://127.0.0.1:$PORT"
    ;;
  *)
    echo "usage: $0 install <tarball> | up | down | logs | status | url"; exit 1
    ;;
esac
