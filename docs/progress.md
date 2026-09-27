# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Phases 2 and 3 have landed on main, including the MCP server with per-call connection leases and the repository's own analyzers. The .NET 11 preview migration, analyzer cleanup, visible Help controls, and startup prompt synchronization pass local verification; updated GitHub workflows await a run. Next: release pipeline, terminal and client resource baselines, mouse resize, search. The scale requirements are documented; measurements and enforcement remain pending.

## Phase 0: Research and scaffolding

- [x] Durable-session multiplexer architecture and coverage reviewed
- [x] Repository conventions extracted from csls and dotsider
- [x] dotnet/runtime coding style adopted
- [x] Name chosen: weft (free on nuget.org and Homebrew)
- [x] Repository root: policy files, build props, package management, license
- [x] Hex1b workload, presentation, HMP1, and testing APIs researched
- [x] tmux, zellij, wezterm, shpool designs researched
- [x] Agent tooling (codex, hermes-agent, opencode) needs researched
- [x] Design document written
- [x] Solution and project skeletons build clean under full analyzers
- [x] Weft.SourceGen analyzers enforce the conventions and mirror the CodeQL queries at build time, with real compilation tests

## Phase 1: Durable sessions

- [x] Server process: unix-socket listener, lock file for single instance, on-demand start from any command
- [x] Session, tab, and block model with stable ids
- [x] PTY-backed blocks via Hex1b child processes with scrollback
- [x] Attach and detach from any number of clients
- [x] Startup prompt cursor restoration stays aligned between server and attached views, including an intervening resize
- [x] Client renders server-side state (smart client, no ANSI re-parsing)
- [x] Session persistence across server restarts (layout, cwd, commands)
- [x] Real tests: server process, real shells, real sockets

## Phase 2: Multiplexer UX

- [x] Layout tree: splits, resize, zoom, presets, even/main layouts
- [x] Floating blocks (server methods, persistence, client rendering; keyboard move pending)
- [x] Status bar, block titles, and tab activity markers
- [x] Leader-key keybinding model with configurable chords, an armed-leader indicator, and a lock mode (repeat pending)
- [x] Command palette with a clickable Help button, direct F1 shortcut, and mouse-accessible Close button; rename prompts, session and tab pickers
- [x] Native scrollback, selection, and copy through the terminal widget; paste from the server buffer (search pending)
- [ ] Mouse: focus, select, and scroll work through the toolkit; drag to resize pending
- [x] Themes and configuration file

## Phase 2b: Multi-block input

- [x] Synchronized input per tab with per-block exclusion
- [x] BenchmarkDotNet project for layout, protocol, and chords

## Phase 3: Composable control surface

- [x] CLI: every UI action addressable from the command line
- [x] Event streaming with sequence numbers and replay ring
- [x] Run-and-await, capture, wait-for-pattern, send-keys, named wait channels
- [x] Hooks (configuration driven; protocol methods pending)
- [x] MCP server for agents (`weft mcp`: eight tools, two resources, SDK client test over in-memory pipes)

## Phase 4: Sharing and operations

- [x] Resource ownership, terminal and client scale scenarios, and lifecycle acceptance criteria documented
- [ ] Measure Native AOT server, client, and child-process footprint across empty blocks, filled history, many clients, and many tabs; set memory budgets and regression tolerances before release
- [ ] Bound history and transient buffers by bytes, enforce aggregate retention limits, and evaluate compact or compressed history through Hex1b public APIs
- [ ] Keep client terminal views to visible blocks and a bounded cache; dispose hidden views and restore state on return
- [ ] Measure idle PTY workers, stack cost, wakeups, and reclamation; evaluate event-driven I/O through Hex1b public APIs
- [ ] Verify bounded slow-reader behavior, abrupt client loss, workload-preserving reattach, and resource reclamation under repeated create and close cycles
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
| 2026-09-08 | Full suite in parallel, several runs | attach UI chords were flaky until the leader became client-side state; stable since |
| 2026-09-08 | Full suite on a 12 core macOS machine, cold runs | attach UI and sync tests timed out while the screen showed the expected state at timeout; failure counters showed the thread pool grown from 12 to 34 workers, so continuations had stalled for 16 to 36 seconds while the runtime injected threads. Cause: each running block pins two pool workers in the pseudo-terminal read and exit waits. Fix: the server raises the pool minimum as blocks start. Cold run passed first time afterwards |
| 2026-09-08 | Synchronized input test on macOS and linux after the pool fix | the sibling had printed the expected line, yet the pattern wait timed out. Two causes: the revision signal came from a workload filter that runs before the terminal applies output, so a wait could capture a stale screen and never be woken again; and the waiter subscribed after capturing. The revision now comes from a presentation filter, which sees applied tokens, and the waiter subscribes first. Tests also wait for a prompt before typing into a fresh shell |
| 2026-09-08 | Codex review of the sync branch, two rounds | fixed: type and paste bypassing fan-out, fan-out interleaving, an unbound second stroke leaving the leader armed, a leaked log writer, one-line summaries, a stalled sibling backing up every other sibling, sync changes published as title changes, activation only on leader actions, Alt strokes escaping the leader fallback, read-only clients able to type, and a dropped trailing activity event |
| 2026-09-08 | Weft.SourceGen adopted across the solution | four findings in existing code, all real; two analyzer gaps fixed with tests (constructor lambdas, reassignment in loops); 358 analyzer tests pass |
| 2026-09-08 | Codex review of the analyzer stack, seven rounds | every thread fixed with a test: ownership transfers by direct and conditional return, cleanup required in every handler, risk carried through a direct disposal, interface-typed locals and aliases, writable ref escapes, struct member writes to static fields, guard analysis split by branch, loop resets compared by receiver, and a bounded idle pool in the MCP bridge; the stricter ownership rule found two real leaks in the server |
| 2026-09-08 | Full suite with the pool minimum pinned to 3 workers on linux | 50 passed before and after the fix; on a fast machine the readers unblock often enough that starvation never set in, which is why the failure only showed on macOS and CI |
| 2026-09-26 | Source generator review follow-up | lock acquisition is checked before cleanup, checked blocks count their disposal, and the repository verifier applies source conventions to file-based apps |
| 2026-09-26 | MCP bridge review follow-up | queued openers cancel promptly, disposal fails queued calls, and failed request writes prevent connection reuse |
| 2026-09-26 | Hex1b update sizing | version 0.171 raises the linux-x64 Native AOT binary from 18.5 MB to 20.3 MB; the compiler size report rises from 20.6 MB to 22.6 MB, so the documented budget is raised to 24 MB |
| 2026-09-27 | .NET 11 preview migration and analyzer enforcement | the prior green CI run used SDK 10.0.401, whose analysis level omitted the inherited full style ruleset; local SDK 11 RC1 exposed the findings. Projects and scripts now target .NET 11 with C# 15, all workflows follow the preview channel, and Style severity is explicitly an error. Only NETSDK1057 is suppressed. Clean build passes with zero warnings and errors, all 527 tests pass, and repository and formatting checks pass |
| 2026-09-27 | Native AOT on macOS arm64 with .NET 11 RC1 | publish and executable version smoke test pass; the native symbol tool reports duplicate debug-map objects and missing module-cache metadata from prebuilt libraries. Those messages remain visible |
| 2026-09-27 | Discoverable help in the attach UI | clickable Help and Close buttons, F1, and terminal focus restoration verified against real shells and sockets, including read-only input blocking and disabled help shortcuts. All 530 tests pass; build, formatting, and repository checks pass |
| 2026-09-27 | Startup prompt synchronization | reproduced a view attaching between cursor save and restore with real shells, PTYs, and HMP1 sockets. The weft presentation filter now sends authoritative coordinates for ordinary restores. A fresh zsh attach shows one prompt without the stray `%`; regression coverage includes resize, a view present before the save, and pending wrap at the right edge. All 534 tests pass; build, formatting, and repository checks pass |
