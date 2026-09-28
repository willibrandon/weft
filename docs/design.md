# weft design

weft is a native terminal workspace built on Hex1b, with durable sessions shared
by desktop applications, a companion CLI, and terminal attachment. A local server
keeps shells running when a window closes. Clients own rendering, history viewing,
selection, and workspace controls; the structured control protocol lets people,
scripts, and agents work with the same sessions.

This document is the source of truth for architecture and behaviour. The
[native app design](standalone-app.md) is the main product direction: macOS first,
then Windows and Linux, over a shared client core. Current installation work targets
local development and testing with ordinary platform packages and no certificate
setup. Public distribution and stores are outside the current scope.
Progress lives in [progress.md](progress.md).
The [native app parity contract](desktop-parity.md) tracks the Mac capabilities,
Windows and Linux adaptations, and qualification required for equivalent behavior.
When code and this document disagree, fix one of them in the same change.

## 1. Positioning

weft serves interactive work and automation through the same durable sessions. A single
user may run many agents, each opening terminals and control connections, so terminal count,
retained output, and client count are independent design concerns. Four commitments guide it:

- **Durable by default.** Sessions outlive clients, terminals, and network hiccups. Closing a
  window never loses work. The server owns the authoritative state.
- **Smart client, dumb transport.** The client never re-parses a session's ANSI into a second
  screen model of its own. Each block streams as authoritative terminal state; the client
  owns rendering, scrollback viewing, selection, and chrome.
- **Everything is a command.** Every key binding runs a named action. Every action is callable
  from the CLI and the control socket with structured input and output. Agents and scripts
  get the same surface as the keyboard.
- **Predictable resource use.** Measure empty terminals, output-heavy terminals, and attached
  clients separately. Share authoritative state, bound retained data, and release resources
  when their owner closes. A small executable alone does not establish a small running footprint.

Against tmux, weft matches the command surface that matters (send-keys, capture, split,
select, resize, list, wait, hooks) and replaces the parts that show their age: text-only
control mode, size negotiation by smallest client, a copy mode that fights the mouse, and
a configuration language of its own. Against zellij, weft keeps the friendly chrome, floating
blocks, and session resurrection while keeping the CLI and server in one native executable
with no plugin runtime to boot. Standalone desktop interfaces ship as separate applications.

## 2. Concepts

| Term | Meaning |
| --- | --- |
| Server | One process per user hosting every session. Started on demand, exits when idle if configured. |
| Session | A named, durable container of tabs. Has a working directory, environment, and metadata. |
| Tab | An ordered page inside a session holding a layout tree of tiled blocks plus floating blocks. |
| Block | One terminal: a pseudo-terminal running a command, with its own scrollback, title, and history. |
| Client | A native desktop app, a CLI command, an agent, or the `weft attach` terminal viewer. |
| Layout | A binary split tree over a tab's tiled blocks, plus geometry computed for the authoritative size. |
| Authoritative size | The columns and rows the server lays a tab out for, chosen from attached clients by policy. |

Identifiers are stable for the life of the server: sessions by name and `s<N>`, tabs `t<N>`,
blocks `b<N>`. Ids are never reused, so automation can hold one across renumbering. Tabs and
blocks also have mutable, contiguous indices for humans. Targets accept ids or the path form
`session:tab.block` with `.` and `:` optional from the left, mirroring tmux so muscle memory
carries over; a target can name a session by name or id, a tab by index or id, and a block
by index or id.

## 3. Architecture

```text
 client process (weft attach)                    server process (weft server)
 ┌──────────────────────────────┐                ┌──────────────────────────────────┐
 │ Hex1bApp                     │  control sock  │ ControlListener (NDJSON)         │
 │  ├ layout renderer           │◄──────────────►│  └ SessionRegistry ──┬ Session   │
 │  ├ status bar / palette      │                │                      ├ Tab       │
 │  └ per-block TerminalWidget  │  block sockets │                      └ Block ────┼─► Hex1bTerminal
 │      └ Hmp1WorkloadAdapter   │◄──────────────►│  Hmp1PresentationAdapter  ▲      │     └ PTY child
 └──────────────────────────────┘   (HMP1)       │  LayoutAuthorityPeer ─────┘      │
                                                 │  SessionStore (json)             │
                                                 └──────────────────────────────────┘
```

Two socket kinds, both Unix domain sockets under one runtime directory:

- **Control socket** `weft.sock`: newline-delimited JSON requests, responses, and events.
  Session, tab, block, and layout operations; capture; input; waits; subscriptions.
- **Block sockets** `blocks/<id>.sock`: one Hex1b HMP1 endpoint per block. A client that
  shows a block connects here with a Hex1b HMP1 client adapter and renders the stream into a
  Hex1b terminal widget. The client is always a secondary peer.

The runtime directory resolves in order: `WEFT_SOCKET_DIR`, `$XDG_RUNTIME_DIR/weft`,
`$TMPDIR/weft-<uid>`, `/tmp/weft-<uid>`. It is created with mode `0700`. Windows uses the
same layout under `%LOCALAPPDATA%\weft\run` because .NET supports AF_UNIX there.

### 3.1 Why Hex1b carries the terminal path

Hex1b already provides the pieces a smart-client multiplexer needs, all on its public API:

- `Hex1bTerminalChildProcess` and `WithPtyProcess` spawn real PTYs with environment, cwd,
  initial size, resize, kill, and exit codes.
- `Hex1bTerminal` owns the authoritative screen, modes, scrollback, and graphics state, with
  `CreateSnapshot()` for capture and `GetScrollbackRows()` for history.
- `Hmp1PresentationAdapter` plus `WithHmp1UdsServer` serve that state to many peers with a full
  state replay on attach, mode replay, graphics replay, and primary/secondary roles.
- `Hmp1WorkloadAdapter` plus `WithHmp1UdsClient` and `WithTerminalWidget` render a remote block
  inside a Hex1bApp with local scrollback, copy mode, selection, and mouse handling.
- The widget toolkit supplies splits, drag bars, floating windows, popups, menus, filterable
  prompts, info bars, theming, and a chord-capable key binding router.

weft never modifies Hex1b. Where Hex1b has no host-side hook, weft composes public pieces.

Performance qualification paces its finite output producer against absolute deadlines.
A blocked write or delayed timer can make a frame late, but cannot add another full
interval to every subsequent frame. Producer write and sleep timings remain recorded
separately from input and drawing latency.

weft's presentation filter projects ordinary cursor restores to the server's applied cursor
coordinates. A view can attach while a shell is drawing a temporary startup prompt, after
the shell saved its cursor but before it restores and erases that prompt. Explicit row and
column positioning keeps that view aligned with captures from the server, including after
a resize. Saves or restores at the right edge and sequences using origin or horizontal
margin modes retain their original tokens so positioning cannot discard pending wrap or
change margin semantics.

### 3.2 Resize authority

Hex1b resizes a terminal's buffer and PTY when its presentation adapter raises `Resized`.
The HMP1 adapter raises that only when the primary peer sends a resize frame. weft therefore
keeps a **layout authority peer** per block inside the server: an in-process HMP1 client over
an in-memory duplex stream registered through `Hmp1PresentationAdapter.AddClient`. It holds
the primary role permanently and sends resize frames whenever layout changes. Attached
clients stay secondary, which is also the correct security posture: viewers cannot resize a
session out from under each other.

Authoritative size policy per session, default `latest`:

- `latest`: the most recently active attached client's viewport.
- `smallest`: the smallest attached client's viewport.
- `fixed`: a configured size, useful for recording, agents, and headless sessions.

Headless sessions with no client keep the last authoritative size.

### 3.3 Client rendering modes

The server computes geometry once per tab for the authoritative size and broadcasts it. A
client renders that geometry into its own viewport:

- **Exact**: viewport equals the authoritative size; blocks fill the screen.
- **Larger**: geometry is centered; the margin is filled with the theme's inactive fill.
- **Smaller**: the top-left of the geometry is shown; a status hint names the authoritative
  size and offers "take size" which makes this client the latest and re-lays out.

Every block renders as a Hex1b terminal widget bound to its HMP1 stream. The client therefore
has local scrollback, selection, copy mode, search, and mouse behaviour for every block with
no server round trip, which is the native feel the design aims for.

## 4. Server

### 4.1 Process model

`weft` is one Native AOT executable. Commands connect to the control socket; if absent they
start `weft server` detached and wait for the socket. The server takes an exclusive advisory
lock on `server.lock` so a second start exits cleanly. `weft server --foreground` runs in the
current process for development and systemd units.

The server is single-threaded at the model level: one `SessionRegistry` guarded by a
`System.Threading.Lock`, with I/O on the thread pool. Every mutation produces events on an
ordered channel with a monotonic sequence number.

### 4.2 Session registry

```text
SessionRegistry
  Sessions: name -> Session { Id, Name, CreatedAt, Cwd, Environment, Tabs, ActiveTab, SizePolicy, Size }
  Session.Tabs: ordered Tab { Id, Name, Root: LayoutNode, Floating: Block[], ActiveBlock, ZoomedBlock }
  Block { Id, Title, Command, Args, Cwd, Environment, State, ExitCode, Pid, Terminal, SocketPath }
```

`LayoutNode` is either a leaf holding a block id or a split with an orientation, two children,
and a ratio. Geometry is computed from the tree for the authoritative size using integer
allocation with a minimum block size of 2 columns by 1 row plus frame space when frames are
enabled. The algorithm mirrors tmux `layout.c`: spare cells go to the last child, resize
adjusts the nearest ancestor split in the requested direction, and removing a block hands
its space to its sibling.

Presets: `even-horizontal`, `even-vertical`, `main-vertical`, `main-horizontal`, `tiled`.

Minimum sizes are a computed property of each leaf (frames, titles, and future scrollbars add
to it) rather than a constant, so chrome changes never need special cases in the resize
check. Applying a layout is total or refused: a layout with fewer leaves than blocks fails
with `invalidParams` instead of silently discarding blocks, which is a tmux papercut.

Layouts serialize to a short, checksummed, pasteable string in tmux's grammar
(`csum,WxH,X,Y[,block]` for leaves, `{...}` for left-right splits, `[...]` for top-bottom
splits) so `weft layout get` output can be stored, diffed, and re-applied. The checksum is a
16-bit rotate-and-add over the body, so tmux layout strings paste in directly when the block
count matches.

### 4.3 Block hosting

Each running block pins about two thread pool workers: the pseudo-terminal reader waits in a
blocking select loop on a pool thread, and the exit wait blocks in short slices. A pool that
starts at the core count starves once a handful of blocks run, and the runtime injects
replacements only about once a second, stalling every continuation in the process meanwhile.
The server raises the pool minimum as blocks start so the pool grows immediately instead.
This prevents starvation but leaves worker count and stack cost proportional to running
blocks, including idle ones. The minimum currently remains at its high-water mark after
blocks exit. Scale work must measure thread count, stack memory, wakeups, and reclamation,
and evaluate event-driven PTY reads and process-exit notification through Hex1b's public
APIs. The current reservation resolves starvation; efficient idle scale remains unverified.

Each block builds a Hex1b terminal:

```text
Hex1bTerminal.CreateBuilder()
  .WithDimensions(cols, rows)
  .WithScrollback(capacity)
  .WithPtyProcess(options => { FileName, Arguments, WorkingDirectory, Environment })
  .WithPresentation(hmp1)            // Hmp1PresentationAdapter listening on blocks/<id>.sock
  .Build()
```

The block records `WindowTitle` changes as its title unless the user pinned one, tracks
`TerminalCompleted` for exit codes, and stays in the registry after exit (state `Exited`) so
captures and exit codes remain queryable until the block is closed or the retention cap trims
it. The environment always includes `TERM=xterm-256color`, `COLORTERM=truecolor`,
`WEFT=1`, `WEFT_SESSION`, `WEFT_TAB`, `WEFT_BLOCK`, and `WEFT_SOCKET`.

### 4.4 Persistence and resurrection

`SessionStore` writes `sessions/<name>.json` on every structural change (create, close,
split, rename, layout, cwd change reported by the shell). On start, the server reads the store
and offers resurrection: `weft attach` on a stored but not running session recreates tabs,
layout, and blocks with their commands and last known cwd. Scrollback is not persisted in
this phase; a block's exit code and last command are.

### 4.5 Input and capture

Input to a block goes through HMP1 from clients, or through the control protocol from the CLI
and agents (`block.sendKeys`, `block.paste`, `block.type`). Capture uses `CreateSnapshot()`
with optional scrollback lines and returns plain text, ANSI, or cell rows with attributes.
Every capture carries the block's output revision so callers can wait for change.

### 4.6 Waits and events

`block.wait` blocks until a pattern appears on screen, the block exits, or a timeout, with a
bounded poll driven by output notifications rather than a timer. `events.subscribe` streams
every registry event from an optional starting sequence; the server keeps a ring of the last
4096 events so short disconnects replay without gaps and a caller can detect a gap when its
requested sequence is older than the ring. `wait.signal` and `wait.for` provide tmux-style
named channels so scripts can synchronize with each other through the server.

The control-event policy pauses a slow subscriber, which gets a
`subscriber.paused` marker with the number of dropped sequence numbers, and resumes with a
`subscriber.resumed` marker. HMP1 block streams pace to their transport, but flow control
alone does not establish a memory bound. Scale work must bound queued bytes per peer and
verify that a stalled viewer cannot block PTY draining or other clients. Terminal updates
cannot be discarded like event notifications: any skipped state requires an authoritative
replay before incremental delivery resumes. If the public HMP1 API cannot support bounded
resynchronization, close that block stream and allow a fresh attach; the workload stays alive.

### 4.7 Resource ownership and retention

Each block owns one authoritative terminal and history in the server. An attached viewer
adds connection state, bounded delivery buffers, and protocol bookkeeping; it must not add a
second full server-side history. Rendering replicas live in the client and count toward the
total footprint. Control-only CLI and MCP connections do not need terminal widgets or HMP1
peers. Tab bars, status bars, and other chrome remain client widgets with no per-tab or
per-client server plugin runtime. The layout authority peer is part of each block's cost.

The current `scrollback` setting caps rows. Planned retention limits also account for bytes
per block and across the server, including cell attributes and graphics. Empty blocks should
allocate history as output arrives, without reserving a filled history buffer. Captures,
attach replays, event queues, and reusable buffers need byte bounds as well as item bounds,
including temporary allocations while serializing. On pressure, evict the oldest retained
history within policy. Reject new blocks or oversized capture requests with `unavailable`
when the configured budget cannot accommodate them. Existing workloads stay alive. Defaults
and enforcement remain work in the progress tracker.

A disconnected client owns no durable workload. Its sockets, subscriptions, pending waits,
delivery buffers, and view state must be released even after abrupt termination. A fresh
attach receives authoritative state from the surviving block, without replaying input or
restarting its process. Exited blocks may retain queryable output and exit metadata within
the retention policy; closing a block releases its terminal, history, authority peer, PTY,
and listener. Repeated create, attach, detach, and close cycles must reach a stable resource
plateau after bounded caches warm up.

Compact storage and compression of inactive history are candidates for measurement through
Hex1b's public APIs. Any saving must include compression metadata and temporary buffers, and
preserve capture, search, resize, and attach latency. Keeping an extra compressed copy beside
unchanged terminal history does not satisfy this goal. These optimizations are unimplemented;
they do not change the decision to use Hex1b as the terminal engine.

## 5. Client

Native applications are the primary interactive clients. The [native app design](standalone-app.md)
defines AppKit on macOS, followed by Microsoft UI Reactor's latest published preview
on Windows and GTK4 on Linux. The macOS implementation shares
control connections, server startup, and session mirrors through `Weft.Client.Core`.
`Weft.Client.Native` exposes a Native AOT C interface to Swift. Visible blocks use
Hex1b HMP1 terminal snapshots; AppKit draws their cells in a custom view. Native menus
handle window and clipboard actions while terminal keys retain their normal role.
The macOS window integrates horizontal tabs and session selection into its native
toolbar. Every tab keeps a subtle visible boundary, including inactive tabs with the
same title. Tab widths stay compact, and icons and visible text are centered inside
the full click target without native button-bezel offsets. Hovering exposes a close button
without shifting the title. Closing an inactive tab preserves the current selection;
the same termination confirmation applies to the button, menu, and Commands panel.
Terminal drawing is confined to its view; bundled symbol fallback preserves
prompt glyphs. Visual acceptance includes real window captures, native scrolling,
and readable narrow layouts, alongside measured latency and resource use.
Mac CI selects the newest installed numbered Xcode release for both native tests
and Swift analysis. CI preserves native timing samples and compiler extraction
logs, including failed runs. Analysis prepares native dependencies before enabling the
tracer, then compiles every Swift source with the app's build arguments. Analysis
reads the C bridge header directly because precompiled headers cannot be shared
between Xcode and the extractor's compiler. Native performance checks require
visible accessory windows and record first-window painting separately from typing.
The paced output test uses one producer process with timed writes, avoiding a
new shell child for every frame.
Swift builds group files into compiler batches based on the host's processor count
so both native tests and analysis avoid redundant parsing of the entire module.
Tab-switch timing ends at the selected tab's first paint; background command
completion is checked separately before sampling loaded memory.
Producer reports separate time spent writing to the PTY from deliberate pacing
so output completion failures can be distinguished from presentation delays.
Server output processing admits one newline-containing batch at a time and yields
after at most eight line breaks, limiting concurrent screen-change allocations
and giving other terminals a turn. Character echo and graphics bytes without line
breaks bypass that queue. PTY reads, input, and client transport remain independent. Pending
bytes retain their order; cancellation and terminal failure release the processing
slot. Each server owns its slot, and process exit waits for already buffered
output. Producer diagnostics also record formatting and elapsed time.
AppKit batches ordinary ASCII glyphs by row and style using fixed cell positions.
Font lookups are retained only for the current font variants. Complex text,
decorations, and glyphs that need individual clipping retain AppKit text layout.
The font release lookup uses the job's read-only GitHub token;
asset and license downloads do not receive that credential.
The native client honors the configured shell for new terminals even when attaching
to an older running server; existing terminal processes keep their shell and state.
The desktop worker coalesces terminal and control events and wakes AppKit through
the versioned native callback bridge. Frame serialization and decoding run on a
bounded worker; AppKit receives only the latest immutable result. A main run-loop source delivers frames in common
modes so native scrollbar tracking continues to receive terminal updates. Queued
input wakes the worker without waiting for the display cadence or publishing an
unchanged frame; terminal output or a command error triggers the next display.
Each pane
uses an AppKit scroll view confined to the scrollbar gutter, with a virtual document
sized to its retained rows. AppKit
owns the scrollbar's thumb, track, and visibility preferences; terminal cells remain
in the shared canvas. Arriving frames retain pointer control during a drag. Returning
to live output cancels any native page-scroll animation. Visual
tests check the painted thumb against the track as well as its history proportion.
The terminal uses an AppKit backing layer and invalidates changed rows and old/new
cursor cells. Layout, selection, search, and graphics changes can request a full
repaint. Ordinary typing does not clear an absent composition or repaint unchanged
content. Font metrics are cached until the font changes; default cell backgrounds
reuse the cleared drawing region. Wheel input preserves AppKit's line or precise-point units and carries
fractional rows forward without queuing capped excess movement.
An ordinary click focuses the pane and clears any selection while leaving the
process cursor in place. Selection begins after deliberate pointer movement;
double and triple clicks select a word or row. Applications that enable terminal
mouse reporting continue to receive those events, with Shift available for selection.
History inspection and selection retain one bounded snapshot in the shared client
core. Selection anchors use its cell coordinates and can span viewports; dragging
past an edge scrolls until the pointer returns or the button is released. Copy joins
soft-wrapped rows and preserves hard line breaks. New output cannot change selected
text. Returning to live output or changing terminal geometry releases the snapshot.
A shared command catalog supplies
menus and command search, with native shortcut configuration and confirmation before
ending processes. Reconnection retains stale content and discards uncertain input.
A successful close-session reply releases that attachment immediately; shutdown
of its block sockets cannot trigger a reconnect before the session event arrives.
Accessible text uses UTF-16 ranges mapped to the displayed cell grid, including wide
and combining graphemes, line queries, selection, and screen coordinates. Input-method
composition remains local until commitment; partial replacements preserve unmodified
marked text, and candidate rectangles follow the marked range.
Composition is cancelled when its originating pane loses focus. The terminal is an
accessible text area; external selection highlights output without moving the shell
cursor or editing terminal contents. The focused cursor blinks by default, honors
steady styles requested by applications, and remains visible while composing text.
Input restarts its timer; focus loss, occlusion, and Reduce Motion stop it.
The native scrollbar maps page clicks to explicit history rows. Page navigation
does not leave a clip-view animation running into a later drag or Return to Live.
The native wire format encodes value-type cells as compact arrays and omits default
styles. The Native AOT client uses speed-focused optimization, matching the server,
because terminal parsing and raster projection are sustained workloads. ABI 4
prefixes JSON metadata with its little-endian byte length, then appends
raw texture buffers in block order. Each unique texture is transferred once, regardless
of sprite count. The bridge writes directly into its owned buffer; decoded texture
slices retain that allocation until their last image is released. Sixel placements are composited into a bounded viewport
plane, resolving newer opaque pixels first. A sprite atlas counts once against the
decoded budget; placements have a separate 16,384-item bound. Image offsets and native
sizes preserve fractional coordinates on the session's 10×20 virtual graphics grid.
Each desktop terminal gives its main and alternate screens separate 64 MiB retained
graphics budgets, with an 8,388,608-pixel bound that reserves room for both sparse
and dense raster storage. Sixel projection checks coverage before materializing older images
and reads cropped rows without allocating another complete raster. An opaque,
unscaled front image uses vectorized alpha checks and bulk row copies; transparent
overlays retain the general compositing path.
Fully covering crops derive their identity from the source instead of hashing the
entire bitmap again. AppKit draws cached Core Graphics images directly. Its image
cache releases entries absent from the latest frame and enforces limits of 256
images and 32 MiB of decoded row storage. These bounds exclude shared frame buffers
and compositor surfaces; process memory is measured separately. The
[Mac qualification record](macos-qualification.md) separates checked behavior,
initial regression limits, and outstanding hardware evidence.
The desktop retains a completed presentation during synchronized output, with a
one-second wakeup deadline, so a frame cannot expose the middle of an application's
redraw or remain held indefinitely when output stops before the update is closed.
Frame pacing includes projection time within a maximum 120 Hz update rate, leaving
headroom for asynchronous 60 Hz producers. Native views coalesce display updates;
idle terminals wait for a change and wake immediately after input. Sixel composition copies uncovered pixels directly and blends only
where transparent layers overlap. Closing
a window releases display snapshots, image caches, backing layers, and native content
immediately, even if the window controller remains referenced. Memory profiles distinguish
physical footprint, resident mappings, graphics surfaces, and managed allocations.
The terminal attachment remains supported; its current UI and defaults are described below.

### 5.1 Attach UI

The client is a Hex1bApp:

```text
┌ VStack ────────────────────────────────────────────────────────┐
│  ZStack                                                        │
│   ├ tiled area: nested HStack/VStack built from tab geometry   │
│   │    each leaf: Border(title) > Terminal(handle)             │
│   ├ WindowPanel: floating blocks as resizable windows          │
│   └ popups: command palette, prompts, confirmations            │
│  InfoBar: session · tabs · active block · Help · Exit weft     │
└────────────────────────────────────────────────────────────────┘
```

Each block view owns a Hex1b terminal built with `WithHmp1UdsClient` and
`WithTerminalWidget`, sized to the block's geometry. The current client opens views for all
blocks in its session. The target is to keep views only for visible blocks and a bounded
cache of recently hidden blocks. A short grace period may avoid reconnecting on rapid tab
switches, but the cache also needs a byte limit. Eviction disposes the terminal replica and
HMP1 connection while the server keeps the workload alive; returning to the block replays
authoritative state. Hidden-tab activity comes from control events. This remains planned work.

### 5.2 Key bindings

The bottom bar has Help and Exit weft buttons. Help lists every action and its configured
shortcut; users can find and run actions there without memorizing keys. Common actions also
have direct function keys. Every configurable shortcut is one key with optional modifiers.
Ordinary terminal keys, including `Ctrl+B` and `Esc`, pass to the focused block.

| Shortcut | Action |
| --- | --- |
| `F1` | open or close Help |
| `F2` | new tab |
| `F3` / `F4` | split right / split below |
| `F5` | zoom block |
| `F6` / `F7` | next / previous tab |
| `F8` | pick tab or block |
| `F9` | switch session |
| `F10` | exit weft, keeping sessions alive |
| `F12` | toggle passing shortcuts to the terminal |

Renaming, closing blocks, resizing, layouts, copy mode, paste, synchronized input, and
numbered tabs remain available in Help and can receive custom shortcuts. The Help button
shows its configured shortcut and stays available when that shortcut is disabled. Locked
mode passes function keys through to the block except its unlock key; Help and Exit weft
remain clickable. Read-only clients retain Help and Exit weft, with server-changing actions
omitted from the palette. Bare `Esc` cannot be configured as an application shortcut.

Exit weft detaches this client and returns to its outer terminal. The server, sessions, and
child processes keep running and can be reattached. It does not shut down the server.

### 5.3 Command palette

Clicking Help or pressing `F1` toggles a single `SelectionPrompt` panel titled "Help and
commands". Its height stays within the viewport, with a visible Close button beneath the
results. Opening it explicitly focuses the search field; typing filters actions and Enter
runs the selected one. Close, the Help shortcut, and `Esc` dismiss the panel and restore
terminal focus. The panel's `Esc` binding takes precedence over text predictions and only
exists while Help is open. Repeated opening cannot stack panels. Help does not list itself
as an action. Actions that need arguments open a follow-up prompt. Accepting raw command
lines in CLI syntax, such as
`split --right --command htop`, remains planned work.

### 5.4 Scrollback, selection, and copy

Provided by Hex1b's terminal widget per block: scroll with keys or wheel, copy mode with
cursor and selection, word motions, and mouse selection. Copied text goes to the system
clipboard through OSC 52 and to the server's paste buffer; Help's Paste action inserts it into a block.
Search in scrollback is client-side over the widget's virtual buffer.

## 6. Control protocol

Newline-delimited JSON over the control socket. One JSON object per line, UTF-8, no framing
beyond the newline. Numbers are JSON numbers. Property names are camelCase and case-sensitive.

```json
{"id":1,"method":"session.list","params":{}}
{"id":1,"result":{"sessions":[{"id":"s1","name":"main","tabs":2,"clients":1,"created":"..."}]}}
{"id":2,"method":"block.sendKeys","params":{"target":"main:1.2","keys":["ls","Enter"]}}
{"id":2,"error":{"code":"notFound","message":"No block matches main:1.2"}}
{"event":"block.output","seq":8812,"data":{"block":"b7","revision":4102}}
```

Rules:

- A request has `id` and `method`; a response repeats `id` with exactly one of `result` or
  `error`. Notifications from the server have `event`, `seq`, and `data` and never `id`.
- The first line from the server after connect is `{"event":"hello","seq":N,"data":{"protocol":1,"server":"0.1.0","pid":123}}`.
- Errors carry a stable `code` from a closed set: `invalidRequest`, `unknownMethod`,
  `invalidParams`, `notFound`, `conflict`, `unavailable`, `timeout`, `internal`.
- Requests on one connection execute in order; a client that wants concurrency opens more
  connections. Long waits (`block.wait`, `events.subscribe`) do not block other connections.
- Protocol version is bumped only for incompatible changes; additive fields are always allowed.

Parameter objects with defaults use settable members so source-generated deserialization
preserves initializer values when a request omits a field. Tests verify those omitted-field
defaults. Required members keep init, since they must be present anyway.

### 6.1 Methods

| Method | Purpose |
| --- | --- |
| `server.info` / `server.shutdown` | Identity, uptime, counts; graceful shutdown. |
| `session.list` / `session.create` / `session.get` / `session.rename` / `session.close` | Session lifecycle. |
| `session.attach` / `session.detach` | Register a client viewport and receive geometry; release it. |
| `session.setSize` / `session.activate` | Report a client's viewport, or mark a client as the latest; the policy decides the authoritative size. |
| `tab.list` / `tab.create` / `tab.select` / `tab.rename` / `tab.close` / `tab.sync` | Tabs, including synchronized input for every block in a tab. |
| `block.list` / `block.get` / `block.create` / `block.close` / `block.kill` / `block.rename` | Blocks. |
| `block.split` / `block.float` / `block.tile` / `block.move` / `block.zoom` / `block.focus` / `block.swap` / `block.sync` | Layout and per-block sync exclusion. |
| `layout.get` / `layout.apply` / `layout.preset` / `layout.resize` | Layout tree and geometry. |
| `block.sendKeys` / `block.type` / `block.paste` / `block.signal` | Input. |
| `block.capture` / `block.history` | Screen and scrollback capture with revision. |
| `block.wait` | Wait for pattern, exit, or revision change with timeout. |
| `block.run` | Create a block for a command and await its exit; returns exit code, output head and tail, byte counts. |
| `events.subscribe` | Stream events from a sequence; the connection becomes event-only. |
| `paste.get` / `paste.set` | Server paste buffer. |
| `hooks.list` / `hooks.set` | Hooks that run commands on events. |

### 6.2 Events

`session.created`, `session.renamed`, `session.closed`, `tab.created`, `tab.selected`,
`tab.renamed`, `tab.changed`, `tab.closed`, `block.created`, `block.titled`, `block.changed`, `block.exited`, `block.closed`,
`block.focused`, `block.output` (throttled, carries revision), `layout.changed` (full geometry
for the tab), `client.attached`, `client.detached`, `size.changed`, `server.stopping`.

## 7. CLI

`weft` with no arguments attaches to the most recent session or creates `main`. Every method
in section 6 has a subcommand; output is human text by default and JSON with `--json`.

```text
weft [attach] [session]         weft new [name] [--cwd] [--command ...]
weft ls [--json]                weft kill-session <session>
weft split [target] [--right|--down] [--size N] [-- command...]
weft send [target] -- keys...   weft type [target] <text>
weft capture [target] [--history N] [--ansi|--json]
weft wait [target] [--for <regex>] [--exit] [--timeout 30s]
weft run [--session s] [--timeout 10m] -- command...
weft events [--since N]         weft layout <preset|get|apply>
weft focus / zoom / float / tile / swap / resize / rename / close / kill
weft server [--foreground] [--socket-dir DIR]   weft mcp
```

Targets default to the block the caller lives in when `WEFT_BLOCK` is set, so a shell inside
weft can address itself without arguments.

## 8. Configuration

`~/.config/weft/config.json` (JSON with comments and trailing commas allowed), read by the
server at start and by the client at attach. `WEFT_CONFIG` overrides the path.

```json
{
  "frames": true,
  "sizePolicy": "latest",
  "scrollback": 10000,
  "shell": null,
  "theme": "default",
  "defaultWidth": 120,
  "defaultHeight": 36,
  "bindings": { "alt+left": "focus.left", "alt+enter": "block.zoom", "f4": "none" },
  "hooks": { "block.exited": "notify-send weft \"$WEFT_TITLE exited $WEFT_EXIT_CODE\"" }
}
```

Shortcut syntax: modifiers `ctrl`, `alt`, `shift` joined with `+`, followed by one key name.
A capital letter means shift. Multi-key sequences are rejected. Binding values
are action ids from the palette (`detach`, `tab.new`, `split.right`, `focus.left`,
`block.zoom`, `block.float`, `layout.next`, `session.pick`, `lock`, `palette`, and so on);
`none` unbinds a default. Hooks name an event from section 6.2 and run a shell command with
the event described in `WEFT_EVENT`, `WEFT_SESSION`, `WEFT_TAB`, `WEFT_BLOCK`, `WEFT_TITLE`,
`WEFT_STATE`, `WEFT_COMMAND`, and `WEFT_EXIT_CODE`. Themes are `default`, `ocean`,
`high-contrast`, and `sunset`.

## 9. Agent surface

Agents get the same server through three doors:

- **CLI with `--json`** for anything scriptable.
- **Control socket** for long-lived integrations that want events.
- **`weft mcp`**, a Model Context Protocol server over stdio exposing tools:
  `list_sessions`, `list_blocks`, `create_block`, `run` (await exit with head and tail
  output), `send_keys`, `capture`, `wait_for`, `close_block`, plus resources `weft://sessions`
  and `weft://block/{id}` for the current screen of a block.

Design rules borrowed from what agent runtimes actually do: `run` returns after a yield time
with partial output and a live block id instead of blocking forever; output is truncated head
and tail with an omission marker and total byte counts; captures carry revisions; waits are
pattern-based with rate limits; exited blocks keep their exit code until closed.

The MCP server is built on the official C# SDK: tools and resources are attribute-marked
instance methods on `[McpServerToolType]` and `[McpServerResourceType]` classes whose
constructor takes the bridge from dependency injection, with `[Description]` text that becomes
the schema agents read, registered through the hosting builder with `WithStdioServerTransport`.
Each call leases its own control connection from the bridge, because the server answers one
request at a time per connection, so a long `wait_for` cannot block a `capture` beside it. The
bridge keeps a few idle connections for reuse and closes the rest of a burst on return, so a
long-lived `weft mcp` does not hold its peak concurrency open. It connects through the same
connect-or-start path as the CLI, so it also brings the server back if it went away while the
agent kept `weft mcp` running. Connections are opened one at a time, so a burst of calls that
finds the server gone starts it once rather than once per call.
Queued calls leave the opener queue when cancelled or when the bridge is disposed. A failed
request write marks its connection unusable immediately, so returning that lease cannot pass
the broken connection to another call while the read loop is still winding down.
Tests drive it with the SDK client over in-memory pipes against a real server. Optional tool
parameters carry default values because the SDK treats any parameter without one as required.
The SDK's own log goes to standard error, the channel the protocol reserves for a stdio server,
at warning level by default; `weft mcp --verbose` adds request tracing. A failed call reaches
the agent only as an error, so that log is where the parameter or exception behind it shows up.

## 10. Sharing and remote

Multiple clients attach to one session and see the same tabs and geometry. Each client has its
own focus, scroll position, and selection. A client may attach `--read-only`, which drops input
at the server. Remote use is socket forwarding: `ssh -L /tmp/weft.sock:<remote runtime>/weft.sock`
and `WEFT_SOCKET_DIR` on the local side. A browser client through Hex1b's web terminal is a
later phase and needs no protocol change because blocks are already HMP1 endpoints.

Blocks started under a forwarded SSH agent get `SSH_AUTH_SOCK` pointed at a stable symlink in
the runtime directory that the server retargets on every attach, so agent forwarding keeps
working across reconnects. This is shpool's trick and the most common tmux-over-ssh papercut.

## 10.1 Borrowed from the field

| Idea | Source | weft |
| --- | --- | --- |
| Stable sigil ids plus mutable indices | tmux | Section 2 |
| Named wait channels for scripts | tmux `wait-for` | `wait.for` / `wait.signal` |
| Pasteable checksummed layout strings | tmux `layout-custom.c` | `layout get` / `layout apply` |
| Size policy as an explicit option | tmux `window-size` | `latest`, `smallest`, `fixed` |
| Hooks on lifecycle events | tmux `set-hook` | `hooks.set` |
| Reactive swap layouts by block count | zellij | `layout.preset` with a per-count table, phase 2 |
| Floating and pinned blocks | zellij, tmux 3.8 | `block.float` |
| Session resurrection from a stored layout | zellij | `SessionStore` |
| Sync input with per-block opt-out | zellij | Help's synchronize input action |
| Read-only watcher attach | zellij, tmux `-r` | `--read-only` |
| Stable JSON output as a documented contract | wezterm `cli list --format json` | `--json` everywhere |
| Smart client over a dumb transport | wezterm mux | HMP1 per block |
| Restore fidelity as a policy knob | shpool | `attach --restore screen|lines:N` later |
| `SSH_AUTH_SOCK` indirection | shpool | Section 10 |
| Event hooks that tests block on instead of sleeping | shpool `test_hooks` | `events.subscribe` in tests |
| Pause delivery for slow event subscribers | tmux's failure mode | Section 4.6 |

## 11. Security

- Sockets live in a `0700` directory owned by the user; there is no network listener.
- The server runs commands as the user; there is no privilege boundary between clients of the
  same user, which matches tmux and screen.
- Read-only attachments are enforced in the client: it never focuses a block, forwards no keys,
  and keeps only actions that change nothing on the server. The block sockets accept input from
  any peer, because the muxer protocol does not attribute input to a peer, so read-only guards
  against accidents by the same user rather than against a hostile process; the private runtime
  directory is the actual boundary.
- No telemetry, no outbound connections.

## 12. Performance budgets

These are targets unless a measurement is stated. Terminal and client scale must be measured
independently, with both steady-state and peak resource use reported.

- Keystroke to PTY write: under 1 ms inside the server.
- Output to client paint: bounded by Hex1b's frame limiter (16 ms) plus socket latency.
- Attach with 20 blocks: under 300 ms to first full paint on a local socket.
- Idle server with 50 blocks: target no periodic wakeups. The blocking PTY and exit waits in
  section 4.3 remain a constraint to measure and resolve.
- Native AOT binary under 22 MB with no runtime dependency. The linux-x64 build measures about
  20.3 MB with Hex1b 0.171. The CI size check reads the compiler's size report, whose total
  runs about 2.3 MB above the file on disk, so its budget is 24 MB.

Benchmarks in `benchmarks/Weft.Benchmarks` currently cover layout computation, protocol
encoding, and key chords. They do not establish terminal or client memory costs. Add a
process-level scale harness as a .NET file-based C# app under `scripts/`, using the published
Native AOT executable, real PTYs, sockets, and client processes:

| Scenario | Measurement |
| --- | --- |
| Server start with no sessions, then one session and one block | Fixed server cost and first-block cost |
| 1, 10, 50, and 100 idle blocks at 80 by 24 cells | Total footprint and incremental cost per empty block |
| The same blocks with 10,000 and 20,000 retained lines of real command output | Cost per filled block and retained byte; peak cost while ingesting |
| 0, 1, 10, and 50 clients against a fixed set of 50 filled blocks | Incremental server cost per control-only connection and per TUI client, measured separately |
| The same blocks in one tab and spread across many tabs, including hidden tabs | Per-tab overhead, visible-view cost, and hidden-view reclamation |
| Slow readers, abrupt client exits, reattach, and repeated block creation and closure | Queue bounds, cleanup, recovery latency, and memory after churn |

Use reproducible output from real commands or checked-in captures of real workloads passed
through the PTY. Include ordinary text, styled and Unicode output, and graphics where
supported. Record retained bytes as well as lines, and raise the configured row cap for the
20,000-line case. Keep the output corpus, terminal dimensions, configuration, and visible
block count fixed when varying clients. Exercise one control connection per agent and
concurrent MCP calls separately, since a tool call can lease its own connection.

Report server memory, each client process, and child workloads separately, along with their
combined cost. Record the OS, architecture, hardware, SDK and dependency versions, build
mode, sampling method, and settling interval. Use macOS physical footprint and the relevant
resident/private-memory metrics on other systems, naming each metric rather than treating
them as interchangeable. Include managed and native memory, thread stacks, handles or file
descriptors, CPU time, wakeups, and attach/input latency under load. Allocation counts alone
are insufficient. Repeat measurements and report their spread.

After closing clients and blocks, live resource counts should return to baseline apart from
documented bounded caches and intentional retained blocks. Process memory may stay above
its initial value because of allocator or thread-pool retention, but repeated cycles must
plateau; explain retained capacity instead of labeling every high-water mark a leak. Set
numerical memory budgets and regression tolerances from these measurements before release.
There are no measured memory budgets yet.

## 13. Testing

Product projects target .NET 11 with C# 15; the compiler analyzer stays on
`netstandard2.0` so Roslyn can load it. Compatible preview SDKs are supported without an
SDK pin. CI installs the rolling .NET 11 preview channel for builds, tests, repository
checks, CodeQL, and Native AOT publishing, and records the resolved SDK with `dotnet --info`.
Build properties explicitly enable all code-style rules, and repository configuration
makes the Style category an error so builds and formatting checks enforce the same policy.
Only NETSDK1057, the preview SDK notice, is suppressed through its dedicated SDK property.

The repository's own analyzers, in `Weft.SourceGen`, compile into every project. They enforce
the conventions in `AGENTS.md` and mirror the CodeQL queries CI runs, so those findings fail
the local build instead of costing a CI round trip. The file-based apps under `scripts/` are
checked by the repository verifier because they do not load the analyzer. Ownership analysis
counts a fallible lock expression before cleanup and follows cleanup inside checked blocks.
Each analyzer rule has real Roslyn compilation tests in `Weft.SourceGen.Tests`.


Real processes only. Test tiers:

- **Model tests**: layout tree, geometry, targets, protocol codec, configuration parsing.
- **Server tests**: start a real server on a temporary socket dir, drive it over the real
  control socket with the client library, spawn real shells (`sh`), capture real output.
- **Client tests**: run the attach UI headless with `WithHeadless()` against a real server and
  assert screen text with Hex1b's snapshot and automator APIs.
- **End-to-end tests**: publish the executable and drive it with the installed `hex1b` tool
  through a hosted terminal: keys in, screen assertions out.
- **Scale and lifecycle acceptance tests (planned)**: use real client processes and PTYs to
  verify that a client killed during output leaves the same workload running, reattach
  restores state, slow readers do not stall other clients, hidden views are reclaimed, and
  repeated close cycles release owned resources. Measure footprint with the section 12
  harness; keep timing and memory tolerances tied to recorded platform baselines.

No mocking libraries, no hand-written substitutes for production services, no skipped tests.

## 14. Packaging

Native AOT per RID (`linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`,
`osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`) as a `dotnet tool` package `weft` and as
GitHub release archives. Windows relies on Hex1b's ConPTY proxy and AF_UNIX support and is
best-effort until its tests run in CI.

Standalone desktop applications have separate bundles, native dependencies, and platform
validation. Their [distribution plan](standalone-app.md#builds-and-distribution) covers
ARM64 and x64 on all three operating systems. Windows desktop support requires real
Windows test coverage before release; CLI archive publishing alone does not establish it.

## 15. Phases

See [progress.md](progress.md). Phase 1 is durable sessions end to end with a plain layout;
Phase 2 is the full multiplexer UX; Phase 3 is the composable control surface and MCP;
Phase 4 is sharing and operations. The standalone desktop track covers the shared client
core, native terminal surfaces, platform interfaces, and desktop packaging.

## 16. Open questions

- Scrollback replay on attach: the client's terminal widget cannot be seeded with history
  through public API, so history before attach is viewable through `block.history` in a
  pager overlay rather than inline scrollback. Revisit if Hex1b exposes seeding.
- Cwd tracking relies on shells emitting OSC 7; without it the last known cwd is the block's
  start cwd.
- Whether to keep tabs as a concept or flatten to sessions of blocks. Tabs stay until real use
  argues otherwise.
