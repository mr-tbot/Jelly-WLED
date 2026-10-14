#!/usr/bin/env bash
# Build the plugin in Release and package dist/jelly-wled_<version>.zip plus dist/manifest.json.
set -euo pipefail
if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: $0   Build plugin/Jellyfin.Plugin.JellyWled and package it under dist/."
  exit 0
fi
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/plugin/Jellyfin.Plugin.JellyWled"
YAML="$ROOT/plugin/build.yaml"
[[ -f "$PROJ/Jellyfin.Plugin.JellyWled.csproj" ]] || { echo "error: missing project $PROJ/Jellyfin.Plugin.JellyWled.csproj" >&2; exit 1; }
[[ -f "$YAML" ]] || { echo "error: missing $YAML" >&2; exit 1; }
export PATH="$HOME/.dotnet:$PATH"
command -v dotnet >/dev/null || { echo "error: dotnet not found (expected in \$HOME/.dotnet)" >&2; exit 1; }
command -v zip >/dev/null || { echo "error: zip not found" >&2; exit 1; }

# Simple `key: "value"` lines.
val() { sed -n "s/^$1: *\"\(.*\)\" *$/\1/p" "$YAML" | head -1; }
# Folded `key: >` blocks: following indented lines joined with spaces.
block() { awk -v k="$1" '$0 ~ "^"k":" {on=1; next} on && /^[ \t]+/ {sub(/^[ \t]+/,""); printf "%s%s", sep, $0; sep=" "; next} on {exit}' "$YAML"; }

GUID="$(val guid)"; NAME="$(val name)"; VERSION="$(val version)"; ABI="$(val targetAbi)"
OVERVIEW="$(val overview)"; CATEGORY="$(val category)"; OWNER="$(val owner)"
DESC="$(block description)"; CHANGELOG="$(block changelog)"
for v in GUID NAME VERSION ABI; do [[ -n "${!v}" ]] || { echo "error: $v missing in $YAML" >&2; exit 1; }; done
TS="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

if command -v aw >/dev/null; then
  aw run --class build -- dotnet build "$PROJ" -c Release
else
  dotnet build "$PROJ" -c Release
fi

BIN="$(find "$PROJ/bin/Release" -name Jellyfin.Plugin.JellyWled.dll -printf '%T@ %h\n' | sort -n | tail -1 | cut -d' ' -f2-)"
[[ -n "$BIN" ]] || { echo "error: Release output not found" >&2; exit 1; }
DIST="$ROOT/dist"; STAGE="$DIST/plugin"
rm -rf "$STAGE"; mkdir -p "$STAGE"
cp "$BIN/Jellyfin.Plugin.JellyWled.dll" "$STAGE/"
for f in "$BIN"/*.dll; do
  b="$(basename "$f")"
  case "$b" in
    Jellyfin.Plugin.JellyWled.dll|Jellyfin.*|MediaBrowser.*|Microsoft.*|System.*|netstandard*) ;;
    *) cp "$f" "$STAGE/"; echo "bundled dependency: $b" ;;
  esac
done

python3 -I - "$STAGE/meta.json" "$GUID" "$NAME" "$DESC" "$OVERVIEW" "$OWNER" "$CATEGORY" "$VERSION" "$CHANGELOG" "$ABI" "$TS" <<'PY'
import json, sys
keys = ["guid", "name", "description", "overview", "owner", "category", "version", "changelog", "targetAbi", "timestamp"]
d = dict(zip(keys, sys.argv[2:12])); d["imagePath"] = ""
open(sys.argv[1], "w").write(json.dumps(d, indent=2) + "\n")
PY

ZIP="$DIST/jelly-wled_$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -q -r "$ZIP" .)
SUM="$(md5sum "$ZIP" | cut -d' ' -f1)"

python3 -I - "$DIST/manifest.json" "$GUID" "$NAME" "$DESC" "$OVERVIEW" "$OWNER" "$CATEGORY" "$VERSION" "$CHANGELOG" "$ABI" "$TS" "$SUM" <<'PY'
import json, sys
g, n, desc, ov, owner, cat, ver, chg, abi, ts, sum_ = sys.argv[2:14]
m = [{"guid": g, "name": n, "overview": ov, "description": desc, "owner": owner, "category": cat,
      "imageUrl": "", "versions": [{"version": ver, "changelog": chg, "targetAbi": abi,
      "sourceUrl": f"https://example.invalid/jelly-wled_{ver}.zip", "checksum": sum_, "timestamp": ts}]}]
open(sys.argv[1], "w").write(json.dumps(m, indent=2) + "\n")
PY
echo "wrote $ZIP (md5 $SUM)"
echo "wrote $DIST/manifest.json"
