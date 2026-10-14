# Jelly-WLED — Claude Code entry (thin pointer)

Read `AGENTS.md` first (authoritative), then `instructions.md` and `.agent/MEMORY.md`.
Global `~/.claude/CLAUDE.md` applies too; on conflict, `AGENTS.md` wins.

Hard non-negotiables:
- Caveman-compressed replies by default; README/docs/UI strings stay full prose, US English.
- `.agent/` is gitignored scratch + memory; back up files to `.agent/backups/` before editing.
- Never commit secrets (`creds.md`, `.env`, keystores). Never push, force-push, or touch prod Jellyfin (BOT-SERVER / Main-Net) without TBOT's explicit instruction for that action; check active sessions before restarting the container.
- Never flash the attached ESP32 boards; claim shared Android devices with `devlock claim` first.
