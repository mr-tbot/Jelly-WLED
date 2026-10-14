# Jelly-WLED — Copilot / VS Code Chat instructions

Authoritative guide: `AGENTS.md` (read it first), then `instructions.md` and `.agent/MEMORY.md`.

Non-negotiables (apply even if you read nothing else):
- US English everywhere a reader can see (color, gray, canceled). Never commit secrets (`creds.md`, `.env`, `*.jks`, API keys) — they are gitignored on purpose.
- Do not push, force-push, or deploy to the production Jellyfin server without an explicit instruction for that action. Check for active playback sessions before restarting the Jellyfin container.
- Keep `.agent/` out of git; back up a file to `.agent/backups/` before rewriting it and log the change in `.agent/MEMORY.md`.
- Keep the Jellyfin dashboard page in the native dashboard style (emby-* components); keep file-based and UI config in sync.

Style: reply caveman-compressed (terse, drop articles/filler, keep code and error strings verbatim); user-facing docs stay full prose.
