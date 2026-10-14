# instructions.md — developer playbook

## Direction (agreed 2026-10-14, autonomous bootstrap)

Goal: ambient "glow" lighting for Jellyfin playback on Android TV and Android phones, driving WLED strips over Wi-Fi, as a Jellyfin plugin. Jellyfin's Android apps cannot be extended and the server never sees rendered pixels, so the design moves color extraction to the server (it has the file and the playback position) and keeps the device side to a thin bridge:

1. **Plugin** analyzes videos once (ffmpeg, low priority) into glow tracks, tracks sessions, exposes tracks + live state over REST/SSE, and streams DDP itself when it can reach the strip.
2. **Android app** (TV box or phone on the strip's LAN) logs into Jellyfin, follows a chosen session, pulls the track and pushes DDP locally. Needed when the server is remote (TBOT: server in Vegas, TV in Hong Kong).
3. **Tools** simulate WLED so everything is testable without hardware.

Phase 2 ideas, not in scope now: Hue/Hyperion outputs, per-item capture box overrides, letterbox detection per scene, Quick Connect login, a Linux/Pi bridge daemon.

## How to extend

- New config option: add to `PluginConfiguration` (XML-serializable, defaults in the property initializer), expose in `configPage.html` (same section pattern), document in README. Device-level options live in `WledDevice`.
- New output protocol: implement `IStripOutput` next to `DdpOutput`; keep the color pipeline untouched.
- New API route: add to `JellyWledController`, keep `[Authorize]` policies explicit, document in `docs/PROTOCOL.md`.
- Android: options live in `GlowSettings` (DataStore) and the matching Compose screen; the service reads settings as a Flow.

## Gotchas

- Jellyfin plugins load from `plugins/<Name>_<version>/` and need a restart to load. `targetAbi` is a minimum server version; keep `12.0.0.0`.
- Session event handlers run inside the server's request handling; never block, never throw.
- ffmpeg must decode every frame even at 10 fps output; use `-skip_frame` sparingly (quality vs speed), low priority (`nice`/`ionice`) so the server keeps transcoding smoothly.
- WLED realtime: packets stop -> strip returns to its effect after the realtime timeout; send `{"live":false}` to leave immediately. Master brightness scales live data; set `on:true` first.
- The ring order and LED mapping must match `docs/PROTOCOL.md` exactly on both sides; use the `sides` test pattern on real hardware to find the start corner and direction.
- Android 14+: foreground service type and permissions are mandatory; Android TV needs the leanback launcher category and a banner.
