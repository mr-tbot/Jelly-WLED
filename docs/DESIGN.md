# Jelly-WLED design notes

Why the project looks the way it does. The contract itself is in `PROTOCOL.md`; this file records the reasoning, the alternatives that were rejected, and the risks that remain.

## 1. The problem

Ambient lighting needs the colors at the edges of the picture while it plays, delivered to an LED controller on the viewer's Wi-Fi with latency low enough that the wall does not visibly lag the screen. The request was a Jellyfin *plugin* that makes this work on Android TV and Android phones.

Two facts shape everything:

- **Jellyfin's apps are not extensible.** A server plugin can add server-side services, REST routes, scheduled tasks and dashboard pages. It cannot inject code into the Android TV app, the Android app or their players. Only the web client can be patched, and only by rewriting its files on disk.
- **The server never sees rendered pixels,** but it has the media file and it is told, by every client, what is playing, where in the file, and whether it is paused. Those reports are the basis of "continue watching", so every client sends them.

## 2. Options considered

| Option | Verdict |
|--------|---------|
| Fork jellyfin-androidtv and jellyfin-android, tap the Media3 pipeline, add a glow sender | Works, exact sync, but two forks of fast-moving apps to maintain forever, and the result is not a plugin. Rejected. |
| Screen-capture companion app (MediaProjection), like the Hyperion Android grabber | Works for any app, but Android 14+ asks for consent on every capture session (painful with a remote), costs CPU on weak TV boxes, and has nothing to do with Jellyfin. Kept as a possible later add-on. |
| **Server-side analysis + server-side output** | The plugin extracts colors from the file once and streams them in sync with the reported position. Zero client changes, works for every client. Only works when the server can reach the strip. **Chosen as the core.** |
| **Server-side analysis + thin device-side bridge** | Same tracks and the same live state, pushed to WLED by a small Android app on the strip's LAN. Covers remote servers (the owner's own case: server in Las Vegas, TV in Hong Kong) and phones. **Chosen as the second output path.** |
| Pure server-side with a VPN route to the strip | Possible with Tailscale subnet routing, but UDP over a lossy intercontinental path makes the wall stutter. Documented as an option, not relied on. |

## 3. Sync model

Clients report progress every few seconds and immediately on pause, resume and seek. The plugin stores for each session `(positionTicks, reportedAt, isPaused)` and extrapolates with the server's monotonic clock. Error sources and how they are handled:

- **Report granularity.** Between reports the extrapolated position is exact unless the player stalls; a stall shows as the lights running ahead until the next report corrects it. Smoothing hides the correction.
- **Clock offset in bridge mode.** The app receives the server's clock in every event and keeps the sample with the smallest round trip; the typical error is well under 50 ms on a LAN, under 150 ms on a long path.
- **Display latency.** TVs add 20-100 ms in game mode, more with processing. A per-device *sync offset* lets the user dial it in; ambient light is forgiving to about 100 ms.
- **Transport jitter.** Server push over a LAN is sub-millisecond; packet loss simply drops a frame, and WLED keeps showing the previous one.

## 4. Glow tracks

A full thumbnail per frame (the ButterFlyEffect-Player stores 64x36 at 30 fps for short loops) would cost about 1.5 GB per two-hour film. Jelly-WLED stores only the edge ring at 10 fps: 100 zones x 3 bytes x 10 fps = 3 KB per second, 21 MB per two hours. Trade-off: the edge band depth is fixed at analysis time (default 8 % of the height) instead of being adjustable live. Ring size, frame rate and band depth are configurable; changing them invalidates existing tracks.

Analysis runs ffmpeg at the lowest CPU and I/O priority with a small thread count so it never competes with transcodes. Software decode of 1080p H.264 runs 8-16x real time on a desktop CPU; 4K HEVC runs 1.5-3x. Tracks are written as *segments* so an on-demand analysis can start at the current playback position (lights follow within seconds) and the rest is filled in later.

Letterbox bars would otherwise make the top and bottom of the wall permanently dark. The analyzer samples `cropdetect` at a few points through the file and crops to the most common rectangle before scaling. Films that switch aspect ratio mid-way (IMAX sequences) will show bars for the minority scenes; a per-item override is a later feature.

## 5. Output

DDP over UDP is the standard "many LEDs, low overhead" path into WLED, used by Hyperion and xLights; one frame of 300 LEDs is one packet. WLED enters realtime mode on the first packet, scales live data by its master brightness, and leaves realtime mode when packets stop or on `{"live":false}`. The plugin therefore turns the light on at the configured brightness before streaming and restores the previous on/off and brightness afterwards.

The color pipeline (saturation, gamma, brightness, smoothing with a minimum step) and the LED layout model (counts per side, start corner, direction) are copied from the ButterFlyEffect-Player glow feature, which was tuned on real hardware; the `sides` and `chase` test patterns from the same project let an installer find the start corner without reading the code.

## 6. Security model

- All plugin routes require Jellyfin authentication. Status, device tests and track deletion require an administrator. Session and event routes show non-administrators only their own sessions. Track streams require the caller to be allowed to see the item (`ILibraryManager.GetItemById<T>(id, userId)`), so a user cannot pull edge colors of media they are not entitled to.
- The plugin never exposes file paths or media bytes, only derived colors.
- Device hosts are administrator-configured; the plugin only sends UDP to them and only fetches `/json/info` and `/json/state` from them.
- The Android app stores the Jellyfin access token in app-private storage and talks to the server over the URL the user entered (HTTPS recommended; plain HTTP works on a LAN).
- The on-demand analysis route is rate-limited per user and bounded by a queue so a client cannot make the server analyze the whole library at once.

## 7. Failure handling

- ffmpeg failure: the segment is discarded, the error is logged with the `JellyWled` prefix and shown on the status panel; playback is unaffected.
- Unreachable WLED: the output loop logs once, keeps trying at a slow rate, and the status panel shows the last error.
- Server restart mid-playback: sessions are rebuilt from the next progress report.
- Bridge app loses the event stream: it reconnects with backoff and re-syncs from `GET /JellyWled/Sessions`.

## 8. Testing strategy

- Pure functions (ring mapping, color pipeline, DDP packets, track header, position math) have unit tests in C# and Kotlin against the same vectors; `tools/glowtrack.py write` produces the reference files.
- `tools/ddp_sim.py` stands in for WLED: it reassembles DDP frames, serves the JSON endpoints, and prints average colors per side so a test video with a known color schedule verifies both mapping and timing end to end.
- A local Jellyfin 12.1 in Docker with generated fixture videos exercises the real plugin: analysis, session tracking from a real client (the web client, the Android app on a phone), server push, the REST surface and the dashboard page.
- Production: deploy only after the local run passes, when no one is playing, with the previous state restorable by deleting the plugin folder.

## 9. Open questions and later work

- Hardware-accelerated decode for analysis on servers with VAAPI/NVDEC.
- Per-item capture box and per-scene letterbox handling.
- Hue Entertainment / Hyperion as additional outputs behind the same pipeline.
- Quick Connect sign-in in the Android app.
- A Linux/Raspberry Pi bridge daemon (the same client logic as the Android app) for non-Android households.
