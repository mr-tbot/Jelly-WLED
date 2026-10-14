# Jelly-WLED protocol and file formats

This document is the contract shared by the server plugin (C#), the Android app (Kotlin) and the test tools (Python). Change it here first, then update the implementations.

## 1. Glow track

A *glow track* is the pre-analyzed edge color of a video over time. Only the picture's outer ring is stored, so a two-hour film costs about 21 MB at the defaults (ring 32x18, 10 frames per second).

### 1.1 Ring layout

Each analyzed frame is downscaled by ffmpeg to `RingWidth x RingHeight` pixels (`scale=W:H:flags=area`, after an optional letterbox crop). The *ring* is the set of edge zones, visited **clockwise from the top-left corner as seen from the front of the TV**:

| Segment | Zones | Order |
|---------|-------|-------|
| top     | `W`   | left to right |
| right   | `H`   | top to bottom |
| bottom  | `W`   | right to left |
| left    | `H`   | bottom to top |

Total zones `N = 2W + 2H` (100 at 32x18). Each zone is the mean RGB of the edge band at that position: the band is `BandPercent` of the picture height deep (default 8 %, at least one pixel row or column). Corners belong to both the horizontal and the vertical band; that is intentional and harmless.

Zone `k` on a horizontal side covers the fraction `[k/W, (k+1)/W)` of the picture width; on a vertical side `[k/H, (k+1)/H)` of the height, both measured from the picture's left or top edge regardless of ring direction. The mapping to LEDs (section 3) relies on this.

### 1.2 Segment file (`*.seg`)

Tracks are stored per media item as one or more *segments*, each a contiguous run of frames. On-demand analysis starts at the current playback position, so an item may have a segment `[startFrame, end)` and a second one `[0, startFrame)` created later. The scheduled task writes whole-item segments.

Path: `<cache>/<itemId-N-format>/<mediaSourceId or "default">/<startFrame>.seg`. The directory name is the item id as 32 hex digits.

All integers are little-endian.

| Offset | Size | Field |
|--------|------|-------|
| 0  | 8  | magic `JWGLOW01` |
| 8  | 2  | `ringW` |
| 10 | 2  | `ringH` |
| 12 | 2  | `fps` (integer frames per second) |
| 14 | 2  | `flags` (bit 0 = finalized) |
| 16 | 4  | `startFrame` (absolute frame index = floor(positionSeconds * fps)) |
| 20 | 4  | `frameCount` (valid only when finalized; 0 while writing) |
| 24 | 8  | `sourceSize` (bytes of the media file at analysis time) |
| 32 | 8  | `sourceMtimeMs` (media file mtime, Unix ms) |
| 40 | 2  | `bandPermille` (edge band depth, per mille of height) |
| 42 | 2  | reserved (0) |
| 44 | 2  | `cropX` (per mille of source width) |
| 46 | 2  | `cropY` (per mille of source height) |
| 48 | 2  | `cropW` (per mille; 1000 = no crop) |
| 50 | 2  | `cropH` (per mille; 1000 = no crop) |
| 52 | 12 | reserved (0) |
| 64 | ... | frames: `frameCount x N x 3` bytes RGB, ring order |

Readers must tolerate an unfinalized segment: the number of readable frames is `floor((fileSize - 64) / (N * 3))`. A segment whose `ringW/ringH/fps` differ from the current configuration is ignored (and deleted by the scheduled task). A segment whose `sourceSize/sourceMtimeMs` differ from the current file is stale and ignored.

### 1.3 Track stream (HTTP)

`GET /JellyWled/Track/{itemId}?mediaSourceId=&fromMs=0&analyze=true` returns `application/octet-stream`, chunked: the same 64-byte header (with `startFrame` = first frame sent and `frameCount` = 0, `flags` bit 0 clear) followed by frames in order from `floor(fromMs * fps / 1000)`. The response ends at the first gap in the track or when analysis has produced nothing new for 15 seconds; the client re-requests from its current position. Response header `X-JellyWled-Track: complete|partial|analyzing|none` says what the server has for the item. With `analyze=true` and on-demand analysis enabled, a missing or gapped track starts an analysis job at `fromMs` and the response streams frames as they are produced (initial wait up to 15 s).

## 2. Playback state (SSE)

`GET /JellyWled/Events` returns `text/event-stream`. It carries the sessions the caller may see (administrators: all; other users: their own). Every event's `data` is one JSON object.

| event | data |
|-------|------|
| `hello`   | `{serverTimeMs, pluginVersion, analysisFps, ringW, ringH}` |
| `state`   | `{sessionId, deviceId, deviceName, client, userId, userName, itemId, itemName, mediaSourceId, runTimeTicks, positionTicks, reportedAtMs, isPaused, track:{state, frames, fps, ringW, ringH}}` |
| `stopped` | `{sessionId, deviceId}` |
| `ping`    | `{serverTimeMs}` every 5 s |

`reportedAtMs` and `serverTimeMs` are the server's wall clock in Unix milliseconds. A client estimates `offset = serverTimeMs - localNowMs` from `hello` and `ping` (keep the sample with the smallest round trip). Position at any moment while not paused:

```
positionMs = positionTicks / 10000 + (nowMs + offset - reportedAtMs)
frame      = floor((positionMs + syncOffsetMs) * fps / 1000)
```

Ticks are Jellyfin's 100 ns units (10 000 per millisecond).

## 3. Ring to LED mapping

A device is described by `LedsTop, LedsRight, LedsBottom, LedsLeft`, `StartCorner` (`top_left | top_right | bottom_right | bottom_left`, where LED 0 sits) and `Direction` (`cw | ccw`, as seen from the front). The strip order is derived exactly as in the ButterFlyEffect-Player glow feature: clockwise from top-left is top (left to right), right (top to bottom), bottom (right to left), left (bottom to top); counterclockwise reverses both the side order and the walk along each side; the start corner rotates the side order.

LED `i` on a side with `n` LEDs covers the fraction `[k/n, (k+1)/n)` of that side measured in picture coordinates (left to right or top to bottom), where `k` is the LED's index along the side after accounting for the walk direction. Its color is the mean of the ring zones overlapping that fraction (at least one zone).

## 4. Color pipeline

Per output frame, per LED, in this order (identical in C# and Kotlin, with 0-255 integer math at the end):

1. **Saturation** `S` (percent, 100 = unchanged): `gray = 0.299 R + 0.587 G + 0.114 B`, `c' = clamp(gray + (c - gray) * S/100)`.
2. **Gamma** `G` (default 2.2): lookup table `lut[v] = round(255 * (v/255)^G)`.
3. **Brightness** `B` (percent): `c'' = floor(lut[c'] * B/100)`.
4. **Smoothing** `K` (percent of the previous value kept, default 35): `v = prev + (cur - prev) * (1 - K/100)`, rounded *toward* the target (ceil when rising, floor when falling) so heavy smoothing still reaches black and white.

Missing frames (gap, analysis not there yet, no track): fade the previous colors to black at the smoothing rate.

## 5. DDP output (UDP, WLED port 4048)

One frame is sent as one or more packets of at most 1440 data bytes (480 RGB LEDs). Header, 10 bytes, big-endian:

| Byte | Value |
|------|-------|
| 0 | `0x40` (version 1) OR `0x01` (push) on the **last** packet of the frame |
| 1 | sequence (0-15, incremented per packet, or 0) |
| 2 | `0x0B` (data type: RGB, 8 bits per channel) |
| 3 | `0x01` (destination: default output) |
| 4-7 | byte offset of this chunk in the frame |
| 8-9 | data length in bytes |

WLED enters realtime mode on the first packet and leaves it when packets stop for its realtime timeout (default 2.5 s). Before streaming, the sender reads `GET http://<host>/json/state` (remembering `on` and `bri`) and posts `{"on":true,"bri":<brightness>}`; after streaming it posts `{"live":false}` and, if `RestoreState` is set, the remembered `on`/`bri`.

Test patterns (same as ButterFlyEffect-Player): `sides` = top red (255,0,0), right green (0,255,0), bottom blue (0,60,255), left yellow (255,200,0), LED 0 white; `chase` = one white LED walking the strip at 20 LEDs per second; `white`; `off`.

## 6. REST summary

| Method | Route | Who | Purpose |
|--------|-------|-----|---------|
| GET | `/JellyWled/Status` | admin | plugin status: jobs, tracked sessions, devices, cache |
| GET | `/JellyWled/Sessions` | user | sessions visible to the caller with track state |
| GET | `/JellyWled/Events` | user | SSE playback state |
| GET | `/JellyWled/Track/{itemId}` | user | track stream (section 1.3) |
| GET | `/JellyWled/Track/{itemId}/Info` | user | segments and analysis status |
| POST | `/JellyWled/Track/{itemId}/Analyze` | user | queue analysis (`fromMs` optional) |
| DELETE | `/JellyWled/Track/{itemId}` | admin | delete the item's track |
| POST | `/JellyWled/Devices/{id}/Test` | admin | run a test pattern on a server-driven device |
| GET | `/JellyWled/Devices/{id}/Probe` | admin | fetch the device's `/json/info` through the server |
| GET/POST | `/Plugins/{pluginId}/Configuration` | admin | standard plugin configuration |

Authentication is Jellyfin's: `Authorization: MediaBrowser Client="...", Device="...", DeviceId="...", Version="...", Token="..."` or an API key.
