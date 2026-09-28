# Progress

Living tracker for the weft build. Check items off as they land; keep the
"Now" section honest. Design decisions belong in [design.md](design.md).

## Now

- Phases 2 and 3, the .NET 11 migration, and startup prompt synchronization have landed on main. The current branch has direct TUI shortcuts, one Help panel, and an explicit Exit weft button; the final shortcut design remains under discussion.
- Native applications are the primary product direction. The `feat/macos-app` branch implements the [desktop app](standalone-app.md) with AppKit, a shared client core, and a Native AOT C interface. Windows and Linux follow later; the CLI and terminal attachment remain companion interfaces.
- The [native app parity contract](desktop-parity.md) maps the Mac implementation to Reactor on Windows and GTK4 on Linux. Shared behavior, native adaptations, installed-app acceptance, and the remaining Mac hardware checks are explicit. The Mac milestone is committed as `2cd2b06`.
- CodeQL now has a manual Swift build and analysis job on macOS alongside C# analysis. Both enforce zero findings. Dependabot already covers NuGet and Actions; there are no Swift package dependencies to monitor yet. The new workflow still needs its first GitHub run.
- Native visual iteration uses one app instance. The Mac app now has retained history, native scrollbars and Find, selection across scrollback, a shared command catalog, Commands search, configurable Mac shortcuts, pane divider dragging, terminal mouse input, animated Kitty and Sixel graphics, and automatic reconnect. Tabs expose close buttons on hover. Compact widths, persistent tab boundaries, and centered visible glyphs address ambiguous repeated titles and button-cell alignment. Focused Swift DocC comments describe lifetime and input contracts. An original vector icon is included in the bundle.
- Installation work targets local development and testing. Mac builds produce an ad hoc signed app and DMG without certificates or notarization. CI exercises an app copied out of that DMG. Windows development MSIX and Linux native packages are planned with their frontends. Store distribution, public signing, and update services are deferred.
- Mac scrolling uses an AppKit scroll view to manage the native thumb and its visibility. A standalone overlay scroller had shown its full-height track without a visible thumb even with the correct history count. Frame delivery continues during thumb tracking, and arriving frames cannot pull the thumb away from the pointer. Page clicks step through history directly so a delayed animation cannot compete with a later drag. Cached font measurements and skipping default background fills reduced median AppKit drawing from 54.9 ms to 4.8 ms in the same 130-column, 37-row history sample. Broader workload and hardware budgets remain open.
- Selection can span viewports and autoscroll during a held drag. External accessibility exposes a read-only terminal with Unicode selection. Composition replacement and pane-change cancellation, plus installed French/German/Spanish key translation, have real PTY coverage. Interactive VoiceOver, physical keyboards, input-method candidate windows, and movement between physical displays remain manual qualification work. Accessibility tests never enable VoiceOver themselves.
- Frame conversion runs off the UI thread. ABI 4 separates raw shared textures from compact JSON metadata, writes directly into owned native storage, and retains texture slices without further pixel copies. Closed windows explicitly release snapshots, image caches, backing layers, and content. Earlier five-client profiling reduced physical footprint from 141.5 MiB to 87.6 MiB. Current qualification also measures 16 tabs, sustained output and typing, scrolling, resize, multiple windows, and forced client termination; portable release budgets remain open.
- Kitty sprite atlases count once against the decoded budget, with a separate placement limit. Sixel composites the newest visible pixels first instead of spending the frame budget on old rasters. Completed synchronized presentations prevent partial flocks; a one-second wakeup releases an unfinished update even when output stops. Fractional placement preserves native-sized sprites. A read-only rbirds workload checks both protocols and records frame rate, drawing time, CPU, memory, and captures.
- Sixel allocates its composition buffer only when a visible raster needs it. Opaque rows overwrite uninitialized storage directly; only surrounding transparent regions are cleared. The native client now uses speed-focused AOT optimization like the server. Two local runs measured 53.1–56.2 Sixel frames/s and 117.3–117.6% client CPU, compared with 49.4 frames/s and 130.7% in the preceding balanced build. Steady 60 fps remains unproven.
- The native image cache drops obsolete frame entries and enforces 256-image and 32 MiB decoded-storage limits. Real capture files exercise reuse, eviction, replacement, and teardown. Graphics workloads and the complete ARM64 and native Intel installed-app CI suites pass the initial regression limits. CPU, resident memory, physical footprint, and remaining hands-on checks are kept separate in the [Mac qualification record](macos-qualification.md).
- The cursor blinks while focused and visible, honors terminal-requested steady styles, and stays visible during composition. Focus loss, occlusion, and Reduce Motion stop the blink timer. Real shell output and captured caret pixels verify blinking, focus loss, and steady styles.
- Ordinary Mac clicks leave the process cursor at the prompt, including small pointer jitter. Dragging selects text. AppKit retains terminal pixels in a backing layer, redraws changed rows and cursor cells, and confines the transparent scroll view to its gutter. Real shell echo/erasure tests check that prompt edits preserve other rows and leave no stale cursor pixels. A bounded compositor recording mode supports further redraw investigation.

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
- [x] Configurable direct shortcuts with a mode that passes shortcuts through to the terminal
- [x] One bounded Help panel with search focus, an F1 toggle, and visible Close button; rename prompts, session and tab pickers
- [x] Exit weft button and F10 detach the view while preserving sessions; Esc remains a terminal or dialog key
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

## Standalone desktop track

- [x] Native frontend architecture, shared core boundary, and release targets documented
- [x] Inventory Mac capabilities and define Windows/Linux parity, platform adaptations, and qualification gates
- [x] Add Swift CodeQL analysis and audit dependency update coverage for native frontends
- [x] Microsoft UI Reactor preview package, command model, and AOT guidance reviewed for Windows
- [x] Extract shared control connections, server startup, logging, and session mirrors without changing TUI behavior
- [x] Add ordered desktop commands, coalesced frames, and a versioned Native AOT C interface
- [x] Add the first AppKit terminal window with menus, tab selection, splits, resize, and workload-preserving reattach
- [x] Create and switch sessions from the native menu while preserving existing shell processes
- [x] Add a shared desktop command catalog for native menus and command search
- [ ] Prove real terminal views on Reactor and GTK4
- [ ] Validate rendering, historical scrollback, selection, graphics, input methods, clipboard, and accessibility
- [x] Review and implement the Mac shortcut defaults, configurable shortcuts, native menus, command search, tabs, sessions, and splits
- [ ] Review Windows and Linux shortcut mappings alongside those frontends
- [x] Add bounded history viewing, Find, retained visible selection, terminal mouse input, and workload-preserving reconnect on macOS
- [x] Extend selection across scrollback, use current viewport dimensions after resize, and stop edge autoscroll on mouse release
- [x] Add native hover close controls without switching inactive tabs before confirmation
- [x] Add accessible Unicode ranges and geometry, and verify local composition and exact committed PTY bytes
- [x] Verify installed French, German, and Spanish layout translation, external accessibility selection, and composition cancellation on pane changes
- [x] Verify animated Kitty and Sixel output, shared atlases, cropping, offsets, native-size geometry, and synchronized redraws
- [x] Add focused cursor blinking with terminal style, composition, and Reduce Motion handling
- [x] Measure sustained output and typing across 16 tabs; verify resizing, multiple windows, simulated backing scales, and workload-preserving client crash recovery
- [x] Add an original app icon and focused Swift DocC documentation
- [x] Add AppKit and published Native AOT bridge smoke tests, plus macOS ARM64 and Intel CI jobs
- [ ] Add Windows and Linux desktop UI automation and published-app coverage for ARM64 and x64
- [x] Add a local macOS DMG and installed-bundle smoke coverage without signing credentials
- [ ] Add Windows unsigned development MSIX and Linux local package installation tests
- [x] Qualify Mac installation, upgrade, removal, and reinstall with running sessions; defer public signing and distribution until needed
- [ ] Measure desktop performance and resource budgets separately from the CLI and server; assess optional musl desktop packages after server and CLI validation

## Verification log

The first PR run exposed an unauthenticated font-release API rate limit, the Intel
runner's older default Swift compiler, and five C# CodeQL findings. The fixes
authenticate only the metadata request, select the newest installed Xcode release,
reuse the selection text buffer, and determine Sixel direct-copy eligibility from
whole-pixel cell sizes. C# analysis now passes and its five review threads are
resolved. Native ARM64 CI reached the tests but measured 134.6 ms input p95 against
the unchanged 100 ms limit. Input now wakes the worker without the display delay
and no longer publishes an unchanged screen ahead of terminal output. A real PTY
test checks silent input. Output notifications no longer republish pixels already
delivered by the terminal connection. Intel's functional and idle-resource checks
passed; the loaded-input test now establishes a short prompt after pane resizing
instead of using cursor coordinates from before its login prompt reflowed.
Swift tracing is separated from dependency preparation
after the combined step stalled before producing .NET build output. Reruns are pending.
The next ARM64 run reduced input p50 from 75.5 to 32.6 ms, but the largest of its
16 samples was 424.1 ms. Qualification now paints the initial prompt before typing,
records cold painting separately, and saves 100 individual input samples. The p95
limit remains 100 ms; this separates startup work and gives the percentile more
than one observation in its upper tail.
ARM64 and Intel CI then passed input p95 at 41.9 and 21.7 ms. Their paced output
producers were still running when the completion wait expired. The producer now uses the
shell's built-in `printf` instead of launching `awk` for each frame, retaining
120 frames, 30 lines per frame, and the same 20 ms pacing.
The native harness now uses accessory activation instead of background-only
activation, and resource measurements require a visible window. First-window paint
timing includes attachment and initial drawing; earlier measurements did not
verify window visibility and must not be read as compositor latency.
The local suite passes with visible windows: initial paint 68.9 ms, idle input
p95 11.1 ms, loaded input p95 46.8 ms, and tab switch p95 75.4 ms.
Swift extraction logs identified an incompatible precompiled C bridge header.
Analysis now parses that header directly; the compiler and security checks remain
unchanged.
The subsequent ARM64 run exposed the same wrapped login-prompt assumption in the
idle resource sample. Both input measurements now establish their prompt after
the attached window's resize before recording cursor movement.
Intel's producer remained slow after removing per-frame `awk` launches; per-frame
`sleep` launches remained. The paced stream now comes from one native test process
with the same 120 frames, 30 lines per frame, and 20 ms interval. It reports its
own elapsed time, and completion and performance limits remain unchanged.
The corrected native suite passes locally with idle input p95 10.9 ms, loaded
input p95 46.2 ms, and tab switch p95 126.2 ms. Swift CodeQL completed its first
successful GitHub scan with zero findings after the header fix.
ARM64 CI then passed the complete installed-app suite, including the 16-tab
workload at 81.7 ms input p95 and installation lifecycle checks. The Intel rerun
expired the two-minute command deadline while compiling the native tests, before
any test executed. App and test builds now use Swift batch compilation to reduce
repeated parsing; command deadlines and test limits are unchanged.
The faster output producer also exposed that tab-switch timing included waiting
for a background command to finish. Timing now ends when the selected tab's frame
is painted, then separately verifies output completion. Loaded memory is sampled
after every background producer has completed.
Both CI architectures now compile and pass functional and idle-resource checks,
but output completion can still time out with the native producer. An initial
profile captured shutdown waits; a profile taken during active output instead
showed workstation GC allocation contention across terminal workers. Producer
diagnostics confirmed 15.3 seconds blocked in PTY writes in an 18.1-second run,
with only 11 ms spent formatting. That run failed loaded input at 297 ms p95.
Server output now processes one batch at a time, yielding after at most eight
line breaks. This bounds concurrent screen-change allocation and gives other
terminals a turn. The local workload completed in 11.8 seconds with loaded input
p95 57.1 ms and tab switching p95 18.7 ms. That measurement used workstation GC.
Per-frame formatting, write, sleep, and elapsed timings are retained in CI.
Later CI traces showed the windowless producer's 20 ms sleeps frequently taking
120–170 ms. An activity assertion did not correct those delays and was removed.
The producer now uses absolute frame deadlines rather than accumulating a fresh
sleep after each delayed write. Input and draw measurements are also printed before
waiting for output completion. The failing ARM64 run passed those limits at
131.5 ms and 1.6 ms p95 respectively; completion and performance limits are unchanged.
Intel also passed input and drawing at 61.0 ms and 6.8 ms p95, but remained blocked
on output writes. With bounded scrolling already in place, a local server-GC comparison
completed the fixed-deadline producer in 5.6 seconds versus 8.5 seconds with workstation
GC, with input p95 21.8 ms. The executable now uses server GC and the runtime's adaptive
heap sizing; the native client keeps workstation GC. All 548 .NET tests and the complete
installed-DMG suite pass: loaded input p95 21.4 ms, tab switching p95 6.4 ms. Graphics
limits also pass, with Kitty at 59.9 frames/s and Sixel at 51.8; client CPU measured
133.6% and 106.5%, resident memory 145.0 and 306.3 MiB. These local measurements do not
establish native Intel performance; the CI workload remains required.
ARM64 CI passed the complete installed suite with loaded input p95 102.3 ms and
tab switching p95 35.2 ms. Native Intel passed input and drawing limits but still
timed out on output completion. Failed native runs now retain a three-second sample
and memory summary of the private server before cleanup, to identify that remaining stall.
The Intel profile identified per-cell change recording requested by weft's presentation
filter, with a 553.8 MiB peak server footprint. Revisions now advance at the next workload
read, after application, and cursor controls are observed at bounded byte boundaries.
This retains authoritative restore coordinates without collecting unused cell changes.
All 552 tests pass, including real-process replay and file-based hostile-payload and
fragmentation checks. The installed-DMG suite completes the paced producer in 2.4 seconds,
with loaded input p95 25.1 ms and tab switching p95 8.2 ms. Kitty and Sixel both measured
60 frames/s in the same 800-bird workload; client resident memory was 154.7 and 331.9 MiB.
The complete installed-app CI suites then passed on ARM64 and native Intel. Loaded
input p95 was 101.6 and 98.2 ms; tab switching p95 was 27.5 and 44.4 ms. The workload
completed in 3.8 and 6.6 seconds respectively, with 136.4 and 88.6 MiB client resident
memory after output. Both jobs also passed crash recovery and installation lifecycle
checks. [CI evidence](https://github.com/willibrandon/weft/actions/runs/36413353713)
records the source revision and architecture-specific artifacts.
A concurrent real-file regression checks that output remains complete and ordered
as commands exit. Each server owns its processing slot; independent servers do not
share it. Exit draining also accounts for queued output. All 548 .NET tests pass.
CI runs the same workload on ARM64 and native Intel; limits and deadlines are
unchanged. Under current unrelated CPU-intensive load, a graphics comparison
measured Kitty at 22.7 frames/s on the previous server and 29.8 on the scheduled
server. Both missed the 55 frames/s limit; this establishes neither a regression
nor acceptance under normal load. The background-activity experiment used for
that comparison was removed.
The next Intel run completed output but measured loaded input p95 302.1 ms and
native drawing p95 91.1 ms. AppKit now batches ordinary ASCII glyphs at explicit
cell positions instead of laying out every character separately. The local native
suite passes, including partial redraw and Unicode/symbol captures, with loaded
input p95 48.2 ms and native drawing p95 0.42 ms. Non-scrolling character echo and
graphics also bypass the server's scrolling queue; pending output remains tracked
through application and exit.
With that bypass, the local 16-tab workload measured input p95 19.8 ms, tab switching
p95 6.0 ms, and drawing p95 0.75 ms. All 548 .NET tests pass again.
Graphics qualification then passed after competing host work subsided: Kitty
60.1 frames/s, 134.5% client CPU, 148.5 MiB resident; Sixel 57.2 frames/s, 113.4%
CPU, 336.8 MiB resident. Sampled footprint maxima were 288.9 and 550.5 MiB.
The viewport and 800-bird workload were unchanged, and native captures preserve
the full visible flock. These are local measurements, not universal budgets.
The final ARM64 installed-app run passes, including upgrade, removal, reinstall,
and forced client termination with the same server and shells. Idle input p95
was 12.3 ms, loaded input p95 20.0 ms, and tab switching p95 15.4 ms.

All 547 .NET tests passed again after execution access was restored. The current
ARM64 bundle compiles and passes signature verification and installed-DMG checks.
Prior installed-bundle smoke tests passed for Intel under Rosetta after copying
the app from its DMG and unmounting the image.
They exercise real shells, rendered prompt symbols and raster pixels, retained
history, scrollbar pixels and proportions, click jitter and drag selection, retained
pixels after prompt edits, wheel and precise scrolling, Find, Commands,
narrow layouts, session controls, and reattachment.
Real window captures verify the toolbar and terminal together;
offscreen AppKit captures do not reproduce composited toolbar materials.

| Date | What | Result |
| --- | --- | --- |
| 2026-09-28 | ARM64 installed app after cache and compiler changes | DMG installation, native rendering and input, resource limits, two attachments, forced client termination, upgrade, removal, and reinstall all pass. The 16-tab workload measured input-to-paint p95 50.8 ms, tab switch p95 81.3 ms, and 156.2 MiB resident memory after closing its tabs. Cache tests process 46,592,512 decoded bytes through real capture files while staying within the 32 MiB bound. One physical display was connected; VoiceOver was off. Both formatting checks and repository policy pass. Native Intel and Swift CodeQL await GitHub execution |
| 2026-09-28 | Native client optimization and graphics limits | The client bridge now uses speed-focused AOT compilation, matching the server. At 165×43 cells on Apple M4 Pro, two 800-bird runs measured Sixel at 56.2/53.1 frames/s, 117.6/117.3% client CPU, and 331.2/330.4 MiB resident memory; the preceding balanced build measured 49.4 frames/s, 130.7%, and 341.8 MiB. Sampled footprint peaks of 619.2/604.9 MiB do not establish a reduction. Kitty measured 59.9/57.3 frames/s. Both runs pass the initial limits and keep graphics inside the viewport. The library grows by 589,792 bytes. All 547 .NET tests pass; final installed-app and GitHub checks remain underway |
| 2026-09-27 | Native image retention and regression limits | Swift compilation with warnings as errors and the cache check using real capture files passed. Obsolete native images are removed when frames change; retained decoded row storage is bounded separately from process footprint. Workload limits, JSON graphics measurements, host architecture records, and CI artifact retention were added. At this point live-server validation was blocked by denied socket binding and GitHub was unreachable. The earlier 547-test and installed-app results predated these changes; no new Sixel throughput or process-memory result was claimed |
| 2026-09-27 | Desktop parity and native analysis | Added the Mac capability inventory, Reactor and GTK4 adaptations, port acceptance scenarios, and explicit manual qualification gaps. All 547 .NET tests and both formatting checks pass. Swift CodeQL uses the existing Mac build and zero-findings gate; `actionlint` passes. Dependabot coverage was audited: NuGet and Actions apply now, Swift packages do not. Actual Swift analysis awaits the first GitHub workflow run |
| 2026-09-27 | Sixel buffer initialization | Deferred canvas allocation and removed clearing pixels that opaque rows immediately overwrite. The real PTY regression checks separate opaque images and transparent gaps. A fresh ARM64 build ran 800-bird graphics at 165×43 cells: Kitty 59.9 frames/s and Sixel 52.3, with native draw p95 1.3/0.5 ms and client resident memory 146.8/364.1 MiB. Physical footprint ranged 318–322/525–634 MiB. Captures retain the full visible flock. These samples do not establish a throughput improvement or a lasting memory reduction |
| 2026-09-27 | rbirds graphics and projection cost | The existing rbirds binary, with 800 birds at 165×43 cells, measured 60.1 observed Kitty frames/s and 53.0 Sixel frames/s over 12 seconds per mode. Native drawing p95 was 1.2/0.5 ms; client CPU was 144.5/131.0% of one core and resident memory 140.9/396.6 MiB. Sampled physical footprint was 313–316/492–605 MiB. These client-only measurements exclude the server and rbirds; Sixel preparation remains short of steady 60 fps at this size. Native captures show both protocols inside the viewport. Shared textures, bulk opaque rows, direct Core Graphics drawing, binary texture transfer, bounded retention, and asynchronous pacing remove avoidable copies and per-sprite work |
| 2026-09-27 | Session-close ordering | A successful close reply now releases the local attachment before interpreting block socket shutdowns. The reconnect test repeats session creation and closure and verifies that transient socket errors cannot appear during an intentional close |
| 2026-09-27 | Mac workload, windows, input, and installation qualification | All 547 .NET tests pass. Both final DMGs pass installed-app checks, including Intel under Rosetta. A 16-tab, 33,600-line workload measured ARM64 input-to-paint p50/p95 of 26.5/60.5 ms and tab-switch p95 of 75.3 ms; Intel under Rosetta measured 36.2/77.9 ms and 174.2 ms. Private client termination, upgrade, removal, and reinstall preserve the server and every shell. Resize, two attachments, 1×/2× rasterization, French/German/Spanish layout translation, composition, caret blinking, and graphic row orientation pass. The installed ARM64 app was replaced once and visually checked; the existing server and shell process identities were unchanged. Only one physical display was connected; interactive VoiceOver, candidate windows, and physical keyboard layouts remain unverified |
| 2026-09-27 | Scrollbar navigation during faster presentation | Page-click animation could continue into a later drag. Native scrollbar actions now map directly to history positions while AppKit retains thumb tracking and appearance. Both installed architectures pass page, drag, endpoint, wheel, and Return to Live checks |
| 2026-09-27 | Tab presentation correction | Confirmed repeated working-directory titles belong to distinct live tabs. Inactive tabs now retain visible boundaries, widths are compact, and glyphs are centered without button-bezel offsets. ARM64 native interaction checks pass; the installed toolbar was inspected at full pixel size |
| 2026-09-27 | Mac lifecycle memory and frame transfer | Explicit window teardown reduced a five-client physical footprint sample from 141.5 to 93.2 MiB; compact value-type cells and decoding without another buffer copy reduced it to 87.6 MiB. Backing-layer allocations fell from 24.9 MiB to under 0.1 MiB. These are isolated local samples, not universal budgets. The profiling command records native heap and VM reports |
| 2026-09-27 | Native interaction and installed app lifecycle | Added hover close buttons, consistent toolbar text metrics, scrollback selection with edge autoscroll, Unicode accessibility ranges, and composition replacement. Tests caught and fixed stale viewport clamping, a Find query lost on resize, and page-scroll animation overriding Return to Live. Local package checks preserve server and shell identities through upgrade, removal, and reinstall |
| 2026-09-27 | Mac click behavior and terminal redraw | Ordinary clicks preserve the process caret; deliberate drags still select. AppKit retains unchanged rows in a backing layer, and scrolling invalidation is confined to the gutter. Installed ARM64 and Intel tests pass, including real shell echo/erasure checks for unchanged rows and stale cursor pixels. A six-second compositor trace verifies repeated Nushell output and provides an optional recording path for further flicker investigation |
| 2026-09-27 | Native scrollbar rendering and responsiveness | AppKit now manages the scrollbar through a scroll view. Installed ARM64 and Intel tests verify visible thumb pixels, history proportions, hit targets, page clicks, live dragging, endpoints, wheel input, and precise scrolling. The real window shows the expected thumb size after reattachment. The same local history sample draws in about 5 ms instead of 55 ms |
| 2026-09-27 | Native commands, terminal interaction, and local installers | Added retained history, Find, stable visible selection, mouse reporting, static raster drawing, configurable Mac shortcuts, native command search, and reconnect without uncertain input replay. Added focused Swift DocC comments and an original icon. Both local DMGs pass installed-app smoke checks; Intel ran under Rosetta. All 546 .NET tests pass. Native apps now lead the README; stores and signing credentials are outside the development installation scope |
| 2026-09-27 | Native desktop visual iteration | Fixed drawing outside terminal bounds, added horizontal tabs and session selection, bundled symbol fallback and font selection, preserved icon counters, and honored the configured shell for new desktop terminals. All 543 .NET tests and both native architecture smoke tests pass; Intel execution used Rosetta. Real app captures reviewed. Scrollback and performance qualification remain pending |
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
| 2026-09-27 | Direct shortcuts, Help, and exit | removed the prefix system and its configuration. F1 toggles one bounded Help panel, search receives focus on every opening, and Close or Esc dismiss it. Exit weft and F10 detach while retaining sessions. Real PTY tests verify Esc and Ctrl+B bytes reach the process unchanged and reattach preserves its PID. Repeated keyboard and mouse opening, filtering, read-only mode, and 60×12 through 188×51 viewports pass. All 539 tests, repository checks, and formatting checks pass |
