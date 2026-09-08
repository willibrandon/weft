# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Core layout engine and control protocol contracts build clean; next is the server.

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

- [ ] Server process: unix-socket listener, on-demand start, single instance per user
- [ ] Session, window, and block model with stable ids
- [ ] PTY-backed blocks via Hex1b child processes with scrollback
- [ ] Attach and detach from any number of clients
- [ ] Client renders server-side state (smart client, no ANSI re-parsing)
- [ ] Session persistence across server restarts (layout, cwd, commands)
- [ ] Real tests: server process, real shells, real sockets

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
- [ ] Structured JSON output and event streaming
- [ ] Run-and-await, capture, wait-for-pattern, send-keys
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
