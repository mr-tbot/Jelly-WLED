# AGENTS.md — Jelly-WLED (authoritative agent guide)

Jelly-WLED turns a Jellyfin server into an ambient-light source: a server plugin pre-analyzes the edge colors of videos, follows playback sessions, and drives WLED LED strips over Wi-Fi (DDP/UDP), either directly from the server or through a small Android TV / phone bridge app on the strip's LAN. Owner: TBOT (mr-tbot). Prior art: the "wall glow" feature of ButterFlyEffect-Player (same algorithm, same LED layout conventions).

Read in this order every session: this file, `instructions.md`, `.agent/MEMORY.md` (resume point, prod facts, change log), `docs/PROTOCOL.md` (the contract between the three implementations).

## Repository layout

```
plugin/                      C# Jellyfin plugin (net10.0, Jellyfin.Controller 12.1.0); deployed on BOT-SERVER as Jelly-WLED_1.0.0.0
  Jellyfin.Plugin.JellyWled/  Plugin.cs, Configuration/, Api/, Analysis/, Output/, Sessions/, ScheduledTasks/
  Jellyfin.Plugin.JellyWled.Tests/   xUnit tests (mapping, color pipeline, DDP, track files, sync math)
  build.yaml                 jprm-style manifest (name, guid, version, targetAbi, framework)
android/                     Android app "Jelly-WLED Glow" (Kotlin, Compose BOM 2026.08, AGP 9.3, minSdk 26, targetSdk 36; TV + phone)
  app/src/main/java/com/mrtbot/jellywled/{data,glow,net,service,ui}   settings, protocol math, Jellyfin client, GlowService, Compose screens
  app/src/debug/             DebugConfigReceiver (test hook, debug builds only)
tools/                       Python helpers: ddp_sim.py (WLED simulator), glowtrack.py (track inspector), make_fixtures.sh
docs/                        PROTOCOL.md (contract), DESIGN.md (why), DEPLOY.md (prod runbook)
dist/                        built plugin zips + manifest.json (gitignored)
.agent/                      memory, backups, research notes, scratch (gitignored)
```

## Build, test, run

Prerequisites on this machine: .NET 10 SDK at `~/.dotnet` (`export PATH=$HOME/.dotnet:$PATH`), Docker, Android SDK at `~/Android/Sdk`, Java 17, Python 3.

- Plugin build: `cd plugin && dotnet build -c Release` (wrap long builds: `aw run --class build -- dotnet build -c Release`).
- Plugin tests: `cd plugin && dotnet test`.
- Package: `tools/package_plugin.sh` -> `dist/jelly-wled_<version>.zip` + `dist/manifest.json`.
- Local Jellyfin for testing: `tools/local_jellyfin_native.sh install <tarball>|up|down|logs|status` (portable Jellyfin 12.1 under `.agent/jf-native/`, port 8096, admin `jellywled`/`jellywled`; preferred here because Docker pulls are slow on this link) or `tools/local_jellyfin.sh up|down|logs` (Docker `jellyfin/jellyfin:12.1`, port 8097). Both copy/mount `dist/plugin` as `Jelly-WLED_dev`; run `tools/package_plugin.sh` then restart to reload the plugin.
- End-to-end without hardware: `tools/ddp_sim.py` + `tools/e2e_local.py` (see README). The full chain used before a prod deploy is `.agent/e2e/verify_and_deploy.sh` (build, tests, package, local restart, two e2e scenarios, headless dashboard check, deploy only when prod is idle).
- WLED simulator: `python3 tools/ddp_sim.py --top 32 --right 18 --bottom 32 --left 18 --http-port 8080`.
- Android: `cd android && ./gradlew assembleDebug testDebugUnitTest` (run it directly; `aw run` was seen to skip Gradle silently, check the APK timestamp), install with `adb -s <serial> install -r app/build/outputs/apk/debug/app-debug.apk`. Claim the device first: `devlock claim <id> --why '...'`. Never `am force-stop` the app before a broadcast: a force-stopped app receives none. Debug builds have `DebugConfigReceiver` (action `com.mrtbot.jellywled.DEBUG_CONFIG`, extras serverUrl/token/userId/followMode/nodes/glowEnabled/test) so `.agent/e2e/phone_test.sh` can drive the app without the UI. Android TV AVD: `jellywled_tv` (system-images;android-36;android-tv;x86_64).
- Prod deploy: see `docs/DEPLOY.md`. Never restart the prod container while `GET /Sessions` shows active playback.

## Conventions

- C#: nullable enabled, warnings as errors, StyleCop as in the Jellyfin template; one class per file; `ILogger<T>` with structured messages prefixed by the component ("[JellyWled] ..."); no blocking calls on the session event handlers (they run on the server's request path) — hand work to channels/queues.
- Kotlin: coroutines + Flow; no blocking on main; the streaming loop lives in a foreground service; config in DataStore; no reflection-heavy libraries.
- Python tools: stdlib only, `python3 -I` safe, each has a `--help`.
- Protocol changes go to `docs/PROTOCOL.md` first; the C#, Kotlin and Python sides must agree byte for byte (tests compare DDP packets and ring mapping against the same vectors).
- US English in every visible string. Comments full prose. Caveman style is for chat only.
- Tests: every non-trivial pure function (mapping, color pipeline, DDP packetizer, track header, position extrapolation) has a unit test on both C# and Kotlin sides; integration is verified against the local Docker Jellyfin + `ddp_sim.py`.

## Canonical UI / style conventions

**Jellyfin dashboard plugin page** (the only web UI): match the native dashboard exactly. Markup from the Jellyfin plugin template and the webhook plugin: `div[data-role=page].page.type-interior.pluginConfigurationPage`, `div.content-primary`, `div.verticalSection` + `h2.sectionTitle`, `div.inputContainer` + `input[is=emby-input]`, `div.selectContainer` + `select[is=emby-select]`, `div.checkboxContainer` + `input[is=emby-checkbox]`, `div.fieldDescription`, buttons `button[is=emby-button].raised.button-submit.block` (save), `.raised.button` (secondary), icon buttons `button.paper-icon-button-light > span.material-icons`. Lists of devices use `div.paperList` with `div.listItem` rows. No custom fonts, no custom color palette, no framework. Logic in the page's JavaScript via `ApiClient` and `Dashboard` helpers only.

**Android app**: dark theme only (it is for a TV), Jetpack Compose Material3, bundled display font Outfit (OFL) for headings, system font for body; accent is a warm-to-cool "glow" gradient (`#FF7A18` -> `#AF002D` -> `#319197`) used sparingly on the active state, otherwise near-black surfaces (`#0B0B0F`, `#15151C`) with high-contrast text; every interactive element is D-pad focusable with a visible focus ring (2 dp accent border + scale 1.04); minimum touch target 48 dp; one screen = one purpose (Connect, Follow, Strip, Picture, Status). Motion: short (150-200 ms) focus and state transitions only.

## Subagent model tiering

This project is built with `/auto-agent-eco`: the orchestrator (Fable) plans, designs, merges and reviews; Opus takes hard subtasks (protocol code, sync engine, service lifecycle, adversarial verification), Sonnet routine implementation/tests/docs, Haiku mechanical edits. Verification is never cheaper than what produced the claim. Always pass an explicit `model`.

## Standing rules (TBOT)

- **Caveman by default** in chat; docs/README/UI strings full prose. Caveman auto-disables for warnings and destructive confirmations.
- **Memory:** keep `.agent/MEMORY.md` current (resume point, decisions, change log with per-change revert). Scratch stays under `.agent/`.
- **Backups:** copy a file to `.agent/backups/` before rewriting it (git covers the rest; say so when you skip).
- **Credentials:** use what TBOT provides; secrets never in output, logs or commits; `creds.md` (gitignored) holds access notes; `change-these-keys.md` lists every credential used so it can be rotated.
- **Main-Net / prod:** the prod Jellyfin is TBOT's production server. Touch it only for the task at hand, check sessions before restarts, always leave a rollback path, log every prod change in memory.
- **Ecosystem:** `~/.tbot-agent/ECOSYSTEM.md` lists TBOT's repos; reuse before rewriting (ButterFlyEffect-Player glow, SofaCode for Android + Compose patterns, StreamScope for Android networking).
- **Easiest path first; autonomous setup;** install toolchains yourself and record it in memory.
- **UI parity:** every plugin option is editable in the dashboard page; every app option in the app. File config and UI never drift.
- **Verify in the browser / on the device** before claiming UI works; ground claims in logs and tool output.
- **Cost-conscious:** warn before expensive actions; prefer incremental work.
- **US English only.** **Confirm before destructive or outward-facing actions** (push, delete, prod restart outside the deploy runbook).
- **Never flash the attached ESP32 boards** (they belong to other projects) without TBOT's explicit say-so.
