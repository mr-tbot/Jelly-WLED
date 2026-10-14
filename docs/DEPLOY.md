# Deploying the plugin to a production Jellyfin server

This is the runbook used for the owner's server (BOT-SERVER: Jellyfin 12.1 in Docker, container `jellyfin`, config bind-mounted from the host). Adapt paths for other installs.

## Before touching production

1. The local run must pass: `tools/local_jellyfin.sh up`, plugin loads, the dashboard page renders, a fixture analyzes, `tools/ddp_sim.py` shows the expected colors.
2. Build the package: `tools/package_plugin.sh` produces `dist/plugin/` (DLL + `meta.json`) and `dist/jelly-wled_<version>.zip`.
3. Check for active playback. A restart interrupts streams. With an API key:

   ```bash
   curl -s -H "Authorization: MediaBrowser Token=$JELLYFIN_API_KEY" "$JELLYFIN_URL/Sessions?activeWithinSeconds=120" \
     | python3 -c 'import json,sys; s=[x for x in json.load(sys.stdin) if x.get("NowPlayingItem")]; print(len(s), "playing")'
   ```

   Only continue when it prints `0 playing`, or when the owner has agreed to the interruption.

## Install

```bash
# from this repository
VERSION=$(grep -m1 '^version:' plugin/build.yaml | cut -d'"' -f2)
ssh botserver "mkdir -p ~/docker/bot_network/jellyfin/config/plugins/Jelly-WLED_$VERSION"
scp dist/plugin/Jellyfin.Plugin.JellyWled.dll dist/plugin/meta.json \
    "botserver:~/docker/bot_network/jellyfin/config/plugins/Jelly-WLED_$VERSION/"
ssh botserver "docker restart jellyfin"
```

The container runs as root on this host, so file ownership does not matter; on other installs make the folder readable by the Jellyfin user.

## Verify

```bash
curl -s -H "Authorization: MediaBrowser Token=$JELLYFIN_API_KEY" "$JELLYFIN_URL/Plugins" \
  | python3 -c 'import json,sys; print([(p["Name"],p["Version"],p["Status"]) for p in json.load(sys.stdin) if "WLED" in p["Name"]])'
curl -s -H "Authorization: MediaBrowser Token=$JELLYFIN_API_KEY" "$JELLYFIN_URL/JellyWled/Status" | head -c 600
ssh botserver "docker logs --since 5m jellyfin 2>&1 | grep -i jellywled | tail -20"
```

Expected: status `Active`, the Status route answers JSON, the log shows "Session tracker started", "Analysis queue started" and no exceptions.

## Roll back

```bash
ssh botserver "rm -rf ~/docker/bot_network/jellyfin/config/plugins/Jelly-WLED_$VERSION && docker restart jellyfin"
```

The plugin stores its configuration in `config/plugins/configurations/Jellyfin.Plugin.JellyWled.xml` and its tracks under `config/data/jellywled/tracks/` (or the configured cache directory). Delete those too for a complete removal.

## Upgrading

Copy the new version into a new `Jelly-WLED_<newversion>/` folder, remove the old folder, restart. Jellyfin keeps the configuration file.
