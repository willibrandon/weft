# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Server runs real shells over the control socket with passing end-to-end tests; next is the attach client UI and CLI.

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

- [x] Server process: unix-socket listener, lock file for single instance (on-demand start pending)
- [x] Session, tab, and block model with stable ids
- [x] PTY-backed blocks via Hex1b child processes with scrollback
- [ ] Attach and detach from any number of clients
- [ ] Client renders server-side state (smart client, no ANSI re-parsing)
- [x] Session persistence across server restarts (layout, cwd, commands)
- [x] Real tests: server process, real shells, real sockets

## Phase 2: Multiplexer UX

- [ ] Layout tree: splits, resize, zoom, presets, even/main layouts
- [ ] Floating blocks
- [ ] Status bar, block titles, activity indicators
- [ ] Leader-key keybinding model with modes and repeat
- [ ] Command palette
- [ ] Native scrollback, selection, search, copy
- [ ] Mouse: focus, resize, select, scroll
- [ ] Themes and configuration file

## Phase 3: Composable control surface

- [ ] CLI: every UI action addressable from the command line
- [x] Event streaming with sequence numbers and replay ring
- [x] Run-and-await, capture, wait-for-pattern, send-keys, named wait channels
- [ ] Hooks
- [ ] MCP server for agents

## Phase 4: Sharing and operations

- [ ] Multi-client live sharing with read-only observers
- [ ] Remote attach over forwarded sockets
- [ ] Session recording and replay
- [ ] Native AOT release packaging for linux, macOS, windows

## Verification log

| Date | What | Result |
| --- | --- | --- |
| 2026-09-08 | `dotnet test --test-modules` on Weft.Tests | 37 passed, 0 failed |
| 2026-09-08 | `dotnet test --solution` | reports zero tests; host runs fine when invoked directly, cause under investigation |
