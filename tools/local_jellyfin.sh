#!/usr/bin/env bash
# Local test Jellyfin (jellyfin/jellyfin:12.1) with the dev plugin and fixtures mounted.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAME=jellywled-jf
PORT=8097
case "${1:-}" in
  up)
    mkdir -p "$ROOT/.agent/jf-local/config" "$ROOT/.agent/jf-local/cache" "$ROOT/tools/fixtures/movies" "$ROOT/dist/plugin"
    docker run -d --name "$NAME" --user "$(id -u):$(id -g)" -p "$PORT:8096" \
      -v "$ROOT/.agent/jf-local/config:/config" -v "$ROOT/.agent/jf-local/cache:/cache" \
      -v "$ROOT/tools/fixtures/movies:/media/movies:ro" \
      -v "$ROOT/dist/plugin:/config/plugins/Jelly-WLED_dev" \
      jellyfin/jellyfin:12.1
    "$0" url ;;
  down) docker rm -f "$NAME" ;;
  logs) docker logs -f --tail 100 "$NAME" ;;
  status)
    docker inspect -f 'container {{.Name}}: {{.State.Status}}' "$NAME"
    curl -s "http://localhost:$PORT/System/Info/Public"; echo ;;
  url)
    echo "http://localhost:$PORT"
    echo "docker host address for the plugin (simulator): $(docker network inspect bridge -f '{{(index .IPAM.Config 0).Gateway}}')" ;;
  *) echo "Usage: $0 up|down|logs|status|url"; exit 1 ;;
esac
