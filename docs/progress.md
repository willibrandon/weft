# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Phases 2 and 3 have landed on main, including the MCP server with per-call connection leases, and the repository's own analyzers are in review. Next: release pipeline, mouse resize, search.

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
- [x] Weft.SourceGen analyzers enforce the conventions and mirror the CodeQL queries at build time, with real compilation tests

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
- [x] Floating blocks (server methods, persistence, client rendering; keyboard move pending)
- [x] Status bar, block titles, and tab activity markers
- [x] Leader-key keybinding model with configurable chords, an armed-leader indicator, and a lock mode (repeat pending)
- [x] Command palette, rename prompts, session and tab pickers
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
