# Jelly-WLED

Ambient "glow" lighting for Jellyfin. The LED strip behind your TV follows the edges of whatever is playing, like Philips Ambilight, but driven by your own Jellyfin server and any WLED controller on your Wi-Fi.

Jelly-WLED is two pieces that share one protocol:

- **A Jellyfin server plugin** that analyzes the edge colors of your videos once (with ffmpeg, at low priority), follows every playback session, and streams colors to WLED controllers it can reach directly.
- **An Android app for Android TV and phones** (a "bridge") for homes where the Jellyfin server is somewhere else: it follows your playback session, fetches the color track from the plugin and drives the WLED strip on the local network. No screen capture, no permissions dialogs, no modified Jellyfin apps.

Because the server does the analysis, it works with every Jellyfin client unchanged: Android TV, Android, web, iOS, Roku, Kodi. The clients only need to report playback progress, which they already do.

> Status: in active development (October 2026). The plugin targets Jellyfin 12.x. Expect rough edges and read the troubleshooting section.

## How it works

1. **Analysis.** For each movie or episode the plugin runs ffmpeg at low priority, scales every frame down to a small thumbnail (32x18 by default) and keeps only the outer ring of edge colors, ten times per second. The result, a *glow track*, costs about 21 MB for a two-hour film and is cached on the server. Tracks are produced on demand when playback starts (starting at the current position, so lights follow within seconds) and in the background by a scheduled task.
2. **Playback tracking.** Jellyfin clients report their position, pauses and seeks to the server. The plugin extrapolates the exact position between reports using the server clock.
3. **Output.** Each output frame maps the ring to your strip's LED layout (how many LEDs per side, which corner the strip starts at, which way it runs), applies saturation, gamma, brightness and smoothing, and sends the frame to WLED over UDP (DDP, port 4048), 25 times per second. When playback stops the strip returns to whatever WLED was showing before.
4. **Bridge mode.** The Android app does step 3 on the TV box or phone instead of the server, using a live event stream from the plugin for play/pause/seek and a streamed copy of the glow track.

## Requirements

- Jellyfin **12.0 or newer** (the plugin is built for .NET 10). Any OS; Docker is fine.
- ffmpeg: the one Jellyfin ships is used automatically.
- A **WLED** controller (0.13 or newer, DDP support) on a network the server or the Android app can reach, with "Receive UDP realtime" enabled (the default).
- For bridge mode: an Android TV device or Android phone (Android 8.0 or newer) on the same network as the WLED controller.

## Install the plugin

### From this repository's manifest (recommended once published)

1. In the Jellyfin dashboard open **Plugins > Repositories** and add the manifest URL (see `dist/manifest.json` after a release; a hosted URL will be listed here when the repository is published).
2. Open **Plugins > Catalog**, install **Jelly-WLED**, and restart Jellyfin.

### Manual

1. Download or build `jelly-wled_<version>.zip` (see *Build from source*).
2. Unzip it into your Jellyfin plugins directory as `Jelly-WLED_<version>/` (for example `/config/plugins/Jelly-WLED_1.0.0.0/` in Docker, `/var/lib/jellyfin/plugins/` on Linux packages). The folder must contain `Jellyfin.Plugin.JellyWled.dll` and `meta.json`.
3. Restart Jellyfin. The plugin appears under **Dashboard > Plugins > My Plugins**.

## Configure

Open **Dashboard > Plugins > Jelly-WLED**. Every option is on that page; the same values are stored in `plugins/configurations/Jellyfin.Plugin.JellyWled.xml`.

**Devices** (strips driven directly by the server):

| Option | Meaning |
|--------|---------|
| Name, Host, DDP port, HTTP port | Where the WLED controller is. Defaults: 4048 and 80. |
| LEDs top / right / bottom / left | How many LEDs on each edge of the TV, seen from the front. |
| LED 0 sits at, Strip runs | Start corner and direction (clockwise or counterclockwise, from the front). Use the **Show sides** test to check: top red, right green, bottom blue, left yellow, first LED white. |
| Brightness, Color richness, Gamma, Smoothing, Frames per second | The color pipeline. Defaults 60 %, 120 %, 2.2, 35 %, 25. |
| Sync offset | Milliseconds added to the playback position (positive = lights earlier). |
| Follow | Which Jellyfin devices (or users, or all sessions) this strip follows. |
| Leave live mode when playback stops | Tell WLED to return to its own effect right away (otherwise it does so after its realtime timeout, 2.5 s by default). WLED lights an "off" strip at its last brightness while live data arrives and turns it off again afterwards. |
| Mode | *Edges follow the picture* for a strip around the TV; *average color* for lamps and light bars elsewhere in the room. Add as many nodes as you like. |

**Analysis**: frames per second (10), ring size (32x18), edge band depth (8 %), letterbox detection, analyze on playback (on), ffmpeg threads and priority, cache location, and what the scheduled task covers (libraries, movies, episodes, items per run).

**Status**: running analyses, followed sessions and what each device is doing right now, with the last error if any.

## The Android app (bridge mode)

1. Install the APK on the TV box or phone that shares a network with the WLED controller.
2. **Connect**: server address and your Jellyfin login (an API key also works).
3. **Follow**: pick the session to follow, normally the Jellyfin app on the same TV. "Any session of my user" follows whatever you play.
4. **Strip**: pick the WLED controller (found automatically on the network or typed in) and set the LED layout; run **Show sides** and **Chase** to verify.
5. **Picture**: brightness, color richness, gamma, smoothing, frames per second, sync offset.
6. Switch the glow on. The app keeps a small foreground service running with a status notification; on Android TV it can start at boot.

## Build from source

Prerequisites: .NET 10 SDK, Python 3, Docker (for the local test server), Android Studio or the Android SDK with Java 17 for the app.

```bash
# Plugin
cd plugin
dotnet build -c Release
dotnet test
cd .. && tools/package_plugin.sh      # dist/jelly-wled_<version>.zip + dist/manifest.json

# Android app
cd android
./gradlew assembleDebug               # app/build/outputs/apk/debug/app-debug.apk
```

### Test without hardware

`tools/ddp_sim.py` is a WLED stand-in: it listens for DDP on UDP 4048, answers `/json/info` and `/json/state` on an HTTP port, and prints the average color per edge every second.

```bash
python3 tools/ddp_sim.py --top 32 --right 18 --bottom 32 --left 18 --http-port 8089 --port 4049 --csv /tmp/sim.csv
tools/make_fixtures.sh                # generates short test videos with known edge colors
tools/local_jellyfin.sh up            # Jellyfin 12.1 in Docker with the plugin and fixtures, or:
tools/local_jellyfin_native.sh install jellyfin_12.1-amd64.tar.xz   # portable build from repo.jellyfin.org
tools/local_jellyfin_native.sh up     # runs it on port 8096 with dist/plugin as Jelly-WLED_dev
```

`tools/e2e_local.py` then does the whole loop without a real player: it completes the setup wizard, adds the fixtures library, configures one device aimed at the simulator, starts a fake playback session (progress reports every 3 s like the Android TV app), and checks the simulator's CSV against the fixtures' color schedule. It has flags for a mid-file start, a seek, and deleting the track first to exercise on-demand analysis:

```bash
python3 tools/e2e_local.py --server http://127.0.0.1:8096 --fixtures "$PWD/tools/fixtures/movies" \
    --sim-host 127.0.0.1 --sim-ddp 4049 --sim-http 8089 --csv /tmp/sim.csv --seek-after 8 --seek-to-ms 40000
```

With real hardware, point a device at the WLED controller instead, play anything, and use **Show sides** to check the layout.

## Architecture

```
Jellyfin client (any) --progress reports--> Jellyfin server + Jelly-WLED plugin
                                                |  ffmpeg -> glow tracks (cache)
                                                |  session tracker + color pipeline
                                                +--DDP/UDP--> WLED (server-reachable strips)
                                                +--REST/SSE--> Android bridge app --DDP/UDP--> WLED (LAN)
```

The contract between the plugin, the app and the tools (track format, events, mapping, DDP) is in `docs/PROTOCOL.md`. Design notes are in `docs/DESIGN.md`.

## Troubleshooting

- **Nothing lights up.** Check *Status* on the plugin page: is the session followed, does a track exist, does the device show "streaming"? Probe the device from the page; it fetches `/json/info` through the server. Make sure "Receive UDP realtime" is on in WLED (Config > Sync Interfaces).
- **Colors on the wrong edges.** Run **Show sides** and change the start corner and direction until top is red and right is green.
- **Lights lag or lead the picture.** Adjust *Sync offset* on the device (or in the app). Clients report progress every few seconds; the plugin extrapolates in between, so a stall in the player can show as a short drift until the next report.
- **The strip dims on bright scenes.** WLED's current limit (Config > LED Preferences > Maximum current) is clamping; raise it to match your power supply.
- **Analysis never finishes.** The track cache directory must be writable by the Jellyfin process; ffmpeg errors are logged with the `JellyWled` prefix in the Jellyfin log.
- **Server cannot reach the strip** (different network, VPN): use the Android app in bridge mode, or route the server to the strip's LAN.

## License

MIT. See `LICENSE`.
