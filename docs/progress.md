# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Phase 1 is usable end to end: attach UI, CLI, on-demand server, persistence. Next: headless client tests, then phase 2 UX.

## Phase 0: Research and scaffolding

- [x] Superlogical announcement and coverage reviewed
- [x] Repository conventions extracted from csls and dotsider
- [x] dotnet/runtime coding style adopted
- [x] Name chosen: weft (free on nuget.org and Homebrew)
- [x] Repository root: policy files, build props, package management, license
- [x] Hex1b workload, presentation, HMP1, and testing APIs researched
- [x] tmux, zellij, wezterm, shpool designs researched
- [x] Agent tooling (codex, hermes-agent, opencode) needs researched
- [x] Design document written
- [x] Solution and project skeletons build clean under full analyzers

## Phase 1: Durable sessions

- [x] Server process: unix-socket listener, lock file for single instance, on-demand start from any command
- [x] Session, tab, and block model with stable ids
- [x] PTY-backed blocks via Hex1b child processes with scrollback
- [x] Attach and detach from any number of clients
- [x] Client renders server-side state (smart client, no ANSI re-parsing)
- [x] Session persistence across server restarts (layout, cwd, commands)
- [x] Real tests: server process, real shells, real sockets

## Phase 2: Multiplexer UX

- [x] Layout tree: splits, resize, zoom, presets, even/main layouts
- [ ] Floating blocks
- [x] Status bar and block titles (activity indicators pending)
- [x] Leader-key keybinding model (modes and repeat pending)
- [ ] Command palette
- [ ] Native scrollback, selection, search, copy
- [ ] Mouse: focus, resize, select, scroll
- [ ] Themes and configuration file

## Phase 3: Composable control surface

- [x] CLI: every UI action addressable from the command line
- [x] Event streaming with sequence numbers and replay ring
- [x] Run-and-await, capture, wait-for-pattern, send-keys, named wait channels
- [ ] Hooks
- [ ] MCP server for agents

## Phase 4: Sharing and operations

- [ ] Multi-client live sharing with read-only observers
- [ ] Remote attach over forwarded sockets
- [ ] Session recording and replay
- [ ] Native AOT release packaging for linux, macOS, windows (linux-x64 publish verified clean)

## Verification log

| Date | What | Result |
| --- | --- | --- |
| 2026-09-08 | `dotnet test --test-modules` on Weft.Tests | 37 passed, 0 failed |
| 2026-09-08 | `dotnet test --solution` | 37 passed once `--nologo` was dropped; the flag is forwarded to the host and rejected |
| 2026-09-08 | hex1b tool drives `weft attach`: type, assert, split, zoom, help, detach | all steps observed on screen; session survived detach with 3 shells |
| 2026-09-08 | `dotnet publish -r linux-x64` Native AOT | clean, 9.8 MB |
