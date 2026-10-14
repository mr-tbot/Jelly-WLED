# Graph Report - Jelly-Wled  (2026-10-15)

## Corpus Check
- 92 files · ~51,261 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 20 file(s) not represented in the graph (top: .xml 7, (none) 4, .seg 2)

## Summary
- 1164 nodes · 2515 edges · 76 communities (44 shown, 32 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 214 edges (avg confidence: 0.85)
- Token cost: 100,000 input · 5,422 output

## Community Hubs (Navigation)
- C# Namespaces and Framework Types
- Python Test Tools
- Android Screens (Connect, Follow, Status)
- Android Node Editor Screens
- ffmpeg Analyzer and Crop Detection
- Android Main Activity and Tabs
- Android Jellyfin Client and Track Stream
- WLED Device Configuration
- Plugin Configuration Options
- Dashboard Page Controller (JS)
- Android Glow Service Lifecycle
- Playback State and Output Manager
- REST Controller and Media Resolver
- Analysis Queue API
- SSE Event Hub
- Track Store
- Segment Header Format
- Service Registration and Hosted Services
- Analysis Job Model
- Android Jellyfin Client Auth and Errors
- Android Glow Service Core
- Android WLED Discovery (mDNS)
- Ring Layout and Test Patterns
- Session Tracker
- Plugin Project and NuGet Packages
- Build, Deploy and Runbook Docs
- DDP Sender and Device Runner
- Android DDP and UDP Sender
- Android Unit Tests
- Protocol and Security Docs
- Android Glow Math (Ring, Mapper, Pipeline)
- Android Glow Tests
- Analysis Queue Internals
- Scheduled Task and Segment Info
- Device Status Model
- Android Debug Config Receiver
- Android Sync Clock
- Misc: baseplugin
- Misc: filestream
- Docs: agents_md_guide
- Android: glow_syncclock_syncclock_frame
- Android: net_jellyfinclient_disconnecte
- Android: service_glowservice_glowservic
- Docs: docs_design_bridge
- Plugin: glow_ledmapper
- Android: data_settings_glowsettings
- Docs: docs_design_glow_track_storage
- Misc: iconfigurablescheduledtask
- Android: glow_ledmapper_kt_bytearray
- Android: glow_trackbuffer
- Docs: agents_conventions
- Android: glow_segmentheader
- Android: glow_syncclock_syncclock_nowms
- Android: glow_colorpipeline_colorpipeli
- Docs: docs_design_fork_apps
- Plugin: tests_tracktests_referencesegm
- Android: net_wled_wled
- Misc: concurrentdictionary
- Plugin: glow_side
- Docs: agents_standing_rules
- Android: glow_testpatterns
- Android: ui_mainactivity_tab
- Tools: tools_package_plugin
- Docs: docs_design_failure_handling
- Tools: tools_local_jellyfin_native
- Tools: tools_make_fixtures
- Plugin: tests_configurationtests
- Tools: tools_local_jellyfin

## God Nodes (most connected - your core abstractions)
1. `PluginConfiguration` - 41 edges
2. `GlowService` - 38 edges
3. `AnalysisQueue` - 38 edges
4. `TrackStore` - 37 edges
5. `JellyWledController` - 33 edges
6. `AnalysisJob` - 32 edges
7. `WledDevice` - 32 edges
8. `DeviceRunner` - 31 edges
9. `PlaybackState` - 30 edges
10. `SegmentHeader` - 30 edges

## Surprising Connections (you probably didn't know these)
- `Non-negotiables (Copilot)` --semantically_similar_to--> `Hard non-negotiables (Claude)`  [INFERRED] [semantically similar]
  .github/copilot-instructions.md → CLAUDE.md
- `Phase 2 ideas` --semantically_similar_to--> `Open questions and later work`  [INFERRED] [semantically similar]
  instructions.md → docs/DESIGN.md
- `WLED realtime gotcha` --conceptually_related_to--> `DDP output`  [INFERRED]
  instructions.md → docs/PROTOCOL.md
- `Server analysis plus device bridge (chosen second path)` --rationale_for--> `Bridge mode`  [INFERRED]
  docs/DESIGN.md → README.md
- `Bridge mode` --references--> `Playback state SSE events`  [INFERRED]
  README.md → docs/PROTOCOL.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Server-side glow pipeline (analysis, tracking, output)** — readme_ring_analysis, readme_playback_tracking, readme_output_pipeline, readme_ddp_output [EXTRACTED 1.00]
- **Sync chain from playback report to LED frame** — docs_protocol_sse_events, docs_protocol_position_extrapolation, docs_design_sync_model, readme_playback_tracking [INFERRED 0.85]
- **Bridge mode decision (server analysis, device-side output)** — docs_design_bridge, readme_bridge_mode, readme_android_bridge_app, instructions_direction [INFERRED 0.85]

## Communities (76 total, 32 thin omitted)

### Community 0 - "C# Namespaces and Framework Types"
Cohesion: 0.05
Nodes (14): Jellyfin.Plugin.JellyWled.Output, Jellyfin.Plugin.JellyWled.Configuration, Jellyfin.Plugin.JellyWled.Sessions, Jellyfin.Plugin.JellyWled.Api, Jellyfin.Plugin.JellyWled.Analysis, Jellyfin.Plugin.JellyWled.ScheduledTasks, Jellyfin.Plugin.JellyWled.Glow, Jellyfin.Plugin.JellyWled (+6 more)

### Community 1 - "Python Test Tools"
Cohesion: 0.07
Nodes (31): avg(), edges(), fmt(), main(), make_handler(), do_GET(), do_POST(), _info() (+23 more)

### Community 4 - "Android Node Editor Screens"
Cohesion: 0.18
Nodes (22): ConnectScreen(), FollowScreen(), NodeCard(), NodeEditor(), NodesScreen(), Pair2(), InfoLine(), StatusScreen() (+14 more)

### Community 5 - "ffmpeg Analyzer and Crop Detection"
Cohesion: 0.12
Nodes (4): CropInfo, None, FfmpegAnalyzer, DdpPacketizer

### Community 6 - "Android Main Activity and Tabs"
Cohesion: 0.10
Nodes (4): App(), MainActivity, TabButton(), GlowTheme()

### Community 8 - "WLED Device Configuration"
Cohesion: 0.08
Nodes (24): WledDevice, Brightness, DdpPort, Direction, Enabled, FollowAllSessions, FollowDeviceIds, FollowUserIds (+16 more)

### Community 9 - "Plugin Configuration Options"
Cohesion: 0.08
Nodes (23): PluginConfiguration, AnalysisFps, AnalysisThreads, AnalyzeNewItems, AnalyzeOnPlayback, BandPercent, CacheDirectory, DetectLetterbox (+15 more)

### Community 10 - "Dashboard Page Controller (JS)"
Cohesion: 0.16
Nodes (23): addBlock(), ANALYSIS_NUM, BOOLS, clamp(), CORNERS, defaultDevice(), DEVICE_FIELDS, deviceHtml() (+15 more)

### Community 12 - "Playback State and Output Manager"
Cohesion: 0.09
Nodes (19): PlaybackState, Client, DeviceId, DeviceName, IsPaused, ItemId, ItemName, MediaSourceId (+11 more)

### Community 13 - "REST Controller and Media Resolver"
Cohesion: 0.16
Nodes (6): MediaResolver, JellyWledController, CallerUser, CallerUserId, IsAdmin, IsApiKey

### Community 15 - "SSE Event Hub"
Cohesion: 0.17
Nodes (3): EventHub, Subscriber, SseEvent

### Community 16 - "Track Store"
Cohesion: 0.18
Nodes (3): TrackStore, Config, RootDirectory

### Community 17 - "Segment Header Format"
Cohesion: 0.11
Nodes (17): SegmentHeader, BandPermille, CropH, CropW, CropX, CropY, Flags, Fps (+9 more)

### Community 18 - "Service Registration and Hosted Services"
Cohesion: 0.14
Nodes (5): LibraryWatcher, OutputManager, Statuses, WledHttpClient, PluginServiceRegistrator

### Community 19 - "Analysis Job Model"
Cohesion: 0.11
Nodes (18): AnalysisJob, Cancellation, DoneFrames, Error, ExpectedFrames, FinishedAt, FromMs, ItemId (+10 more)

### Community 20 - "Android Jellyfin Client Auth and Errors"
Cohesion: 0.28
Nodes (3): HttpException, JellyfinClient, ServerInfo

### Community 21 - "Android Glow Service Core"
Cohesion: 0.19
Nodes (5): Connection, GlowService, GlowStatus, NodeStatus, TestRun

### Community 22 - "Android WLED Discovery (mDNS)"
Cohesion: 0.15
Nodes (3): FoundWled, DiscoveryListener, ResolveListener

### Community 23 - "Ring Layout and Test Patterns"
Cohesion: 0.20
Nodes (4): LedSpan, TestPatterns, GlowTests, StoreAndSyncTests

### Community 25 - "Plugin Project and NuGet Packages"
Cohesion: 0.12
Nodes (15): Microsoft.NET.Test.Sdk (17.14.1), SerilogAnalyzer (0.15.0), SmartAnalyzers.MultithreadingAnalyzer (1.1.31), StyleCop.Analyzers (1.2.0-beta.556), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), net10.0, Jellyfin.Controller (12.1.0) (+7 more)

### Community 26 - "Build, Deploy and Runbook Docs"
Cohesion: 0.17
Nodes (14): Build, test and run commands, Repository layout, Install via docker cp, Rollback by removing plugin folder, Production deploy runbook, Testing strategy, Target framework net10.0, Plugin GUID (+6 more)

### Community 28 - "DDP Sender and Device Runner"
Cohesion: 0.18
Nodes (4): DdpSender, LastError, DeviceRunner, Status

### Community 31 - "Protocol and Security Docs"
Cohesion: 0.17
Nodes (15): Security model, REST summary, Test patterns, Jelly-WLED dashboard config page, JellyWledConfig.js controller (not in chunk), Delete track control, Devices section, Scheduled task section (+7 more)

### Community 32 - "Android Glow Math (Ring, Mapper, Pipeline)"
Cohesion: 0.15
Nodes (5): Side, BOTTOM, LEFT, RIGHT, TOP

### Community 34 - "Analysis Queue Internals"
Cohesion: 0.22
Nodes (3): AnalysisQueue, Pending, Recent

### Community 35 - "Scheduled Task and Segment Info"
Cohesion: 0.19
Nodes (5): SegmentInfo, EndFrame, IsFinalized, StartFrame, TrackInfo

### Community 36 - "Device Status Model"
Cohesion: 0.15
Nodes (13): DeviceStatus, DeviceId, Fps, HasFrame, Host, ItemName, LastError, LastPacketUtc (+5 more)

### Community 40 - "Misc: baseplugin"
Cohesion: 0.17
Nodes (5): Plugin, Description, Id, Instance, Name

### Community 41 - "Misc: filestream"
Cohesion: 0.23
Nodes (3): SegmentWriter, Frames, PartPath

### Community 42 - "Docs: agents_md_guide"
Cohesion: 0.22
Nodes (10): AGENTS.md authoritative agent guide, Claude Code entry pointer, Open questions and later work, Copilot chat instructions, Developer playbook, Gotchas, Phase 2 ideas, WLED realtime gotcha (+2 more)

### Community 44 - "Android: net_jellyfinclient_disconnecte"
Cohesion: 0.31
Nodes (6): Disconnected, GlowEvent, Hello, Ping, State, Stopped

### Community 46 - "Docs: docs_design_bridge"
Cohesion: 0.24
Nodes (9): How to extend, Android bridge app (Jelly-WLED Glow), Architecture, DDP output to WLED, Jelly-WLED project, Jellyfin server plugin, Output pipeline, Playback tracking (+1 more)

### Community 47 - "Plugin: glow_ledmapper"
Cohesion: 0.27
Nodes (5): LedMapper, LedCount, ZoneCount, RingLayout, TrackTests

### Community 48 - "Android: data_settings_glowsettings"
Cohesion: 0.38
Nodes (3): GlowSettings, Settings, WledNode

### Community 49 - "Docs: docs_design_glow_track_storage"
Cohesion: 0.29
Nodes (9): Letterbox detection via cropdetect, Glow track format, Ring layout, Segment file format, Track stream HTTP endpoint, Analysis section, Analysis options, Glow track (+1 more)

### Community 50 - "Misc: iconfigurablescheduledtask"
Cohesion: 0.20
Nodes (8): AnalyzeLibraryTask, Category, Description, IsEnabled, IsHidden, IsLogged, Key, Name

### Community 53 - "Docs: agents_conventions"
Cohesion: 0.29
Nodes (7): Coding conventions, ButterFlyEffect-Player wall glow prior art, Color pipeline inherited from ButterFlyEffect-Player, Color pipeline, Protocol contract, DDP output, Ring to LED mapping

### Community 58 - "Plugin: tests_tracktests_referencesegm"
Cohesion: 0.33
Nodes (4): SegmentReader, Header, Path, ReadableFrames

### Community 61 - "Plugin: glow_side"
Cohesion: 0.33
Nodes (5): Side, Bottom, Left, Right, Top

### Community 62 - "Docs: agents_standing_rules"
Cohesion: 0.40
Nodes (5): Standing rules, Canonical UI style conventions, Hard non-negotiables (Claude), Non-negotiables (Copilot), emby-* dashboard components

### Community 64 - "Android: ui_mainactivity_tab"
Cohesion: 0.40
Nodes (5): Tab, Connect, Follow, Nodes, Status

### Community 65 - "Tools: tools_package_plugin"
Cohesion: 0.60
Nodes (4): block(), PATH, package_plugin.sh script, val()

### Community 67 - "Docs: docs_design_failure_handling"
Cohesion: 0.50
Nodes (4): Failure handling, Sync model, Position extrapolation formula, Playback state SSE events

### Community 68 - "Tools: tools_local_jellyfin_native"
Cohesion: 0.50
Nodes (3): DOTNET_ROOT, PATH, local_jellyfin_native.sh script

## Knowledge Gaps
- **191 isolated node(s):** `TOP`, `RIGHT`, `BOTTOM`, `LEFT`, `Connect` (+186 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 427 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **32 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `AnalysisQueue` connect `Analysis Queue Internals` to `C# Namespaces and Framework Types`, `Scheduled Task and Segment Info`, `ffmpeg Analyzer and Crop Detection`, `DDP Sender and Device Runner`, `Analysis Queue API`, `REST Controller and Media Resolver`, `Track Store`, `SSE Event Hub`, `Service Registration and Hosted Services`, `Analysis Job Model`, `Misc: iconfigurablescheduledtask`, `Async Device Output Loops`, `Misc: concurrentdictionary`?**
  _High betweenness centrality (0.337) - this node is a cross-community bridge._
- **Are the 14 inferred relationships involving `PluginConfiguration` (e.g. with `.RunAsync()` and `.StartAsync()`) actually correct?**
  _`PluginConfiguration` has 14 INFERRED edges - model-reasoned connections that need verification._
- **What connects `TOP`, `RIGHT`, `BOTTOM` to the rest of the system?**
  _191 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `C# Namespaces and Framework Types` be split into smaller, more focused modules?**
  _Cohesion score 0.05350877192982456 - nodes in this community are weakly interconnected._
- **Why does `TrackStore` connect `Track Store` to `C# Namespaces and Framework Types`, `Analysis Queue Internals`, `Scheduled Task and Segment Info`, `ffmpeg Analyzer and Crop Detection`, `Plugin Configuration Options`, `REST Controller and Media Resolver`, `Analysis Queue API`, `SSE Event Hub`, `Service Registration and Hosted Services`, `Misc: iconfigurablescheduledtask`, `Plugin: tests_tracktests_referencesegm`, `DDP Sender and Device Runner`?**
  _High betweenness centrality (0.073) - this node is a cross-community bridge._
- **Should `Python Test Tools` be split into smaller, more focused modules?**
  _Cohesion score 0.06502816180235535 - nodes in this community are weakly interconnected._
- **Why does `JellyWledController` connect `REST Controller and Media Resolver` to `C# Namespaces and Framework Types`, `Analysis Queue Internals`, `Analysis Queue API`, `SSE Event Hub`, `Track Store`, `Service Registration and Hosted Services`, `Analysis Job Model`, `Session Tracker`?**
  _High betweenness centrality (0.069) - this node is a cross-community bridge._