# weft design

weft is a durable-session terminal multiplexer built on Hex1b. It keeps any number of
terminal blocks alive inside long-lived sessions on a local server, renders them through
a smart client that owns scrollback, selection, and layout chrome, and exposes every
action through a structured control protocol so people and software drive it the same way.

This document is the source of truth for architecture and behaviour. Progress lives in
[progress.md](progress.md). When code and this document disagree, fix one of them in the
same change.

## 1. Positioning

Superlogical announced a "multiplexer for all work": server-side sessions, smart clients,
native scrollback and selection, reconnect from any device, live sharing, and a roadmap of
composability and production operation. weft targets the same shape with three commitments:

- **Durable by default.** Sessions outlive clients, terminals, and network hiccups. Closing a
  window never loses work. The server is the only place state lives.
- **Smart client, dumb transport.** The client never re-parses a session's ANSI into a second
  screen model of its own. Each block streams as authoritative terminal state; the client
  owns rendering, scrollback viewing, selection, and chrome.
- **Everything is a command.** Every key binding runs a named action. Every action is callable
  from the CLI and the control socket with structured input and output. Agents and scripts
  get the same surface as the keyboard.

Against tmux, weft matches the command surface that matters (send-keys, capture, split,
select, resize, list, wait, hooks) and replaces the parts that show their age: text-only
control mode, size negotiation by smallest client, a copy mode that fights the mouse, and
a configuration language of its own. Against zellij, weft keeps the friendly chrome, floating
blocks, and session resurrection while staying a single native binary with no plugin runtime
to boot.

## 2. Concepts

| Term | Meaning |
| --- | --- |
| Server | One process per user hosting every session. Started on demand, exits when idle if configured. |
| Session | A named, durable container of tabs. Has a working directory, environment, and metadata. |
| Tab | An ordered page inside a session holding a layout tree of tiled blocks plus floating blocks. |
| Block | One terminal: a pseudo-terminal running a command, with its own scrollback, title, and history. |
| Client | An attached viewer: the `weft attach` TUI, a CLI command, an agent, or a browser later. |
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

Backpressure never disconnects a client. A slow event subscriber is paused, gets a
`subscriber.paused` marker with the number of dropped sequence numbers, and resumes with a
`subscriber.resumed` marker; the HMP1 block streams already pace to their transport. tmux kills
control-mode clients that fall behind, which is the failure mode to avoid.

## 5. Client

### 5.1 Attach UI

The client is a Hex1bApp:

```text
┌ VStack ────────────────────────────────────────────────────────┐
│  ZStack                                                        │
│   ├ tiled area: nested HStack/VStack built from tab geometry   │
│   │    each leaf: Border(title) > Terminal(handle)             │
│   ├ WindowPanel: floating blocks as resizable windows          │
│   └ popups: command palette, prompts, confirmations            │
│  InfoBar: session name · tabs · active block · mode · hints    │
└────────────────────────────────────────────────────────────────┘
```

Each visible block owns a Hex1b terminal built with `WithHmp1UdsClient` and
`WithTerminalWidget`, sized to the block's geometry. Blocks that leave the visible tab are
disconnected after a grace period to keep the client light; reattaching replays state.

### 5.2 Key bindings

Leader model with a single chord table, default leader `Ctrl+B`, all rebindable in
configuration. Every binding names an action id; the palette lists actions with their bindings.

| Chord | Action |
| --- | --- |
| `leader d` | detach |
| `leader c` | new tab |
| `leader n` / `leader p` | next / previous tab |
| `leader 1..9` | select tab |
| `leader %` / `leader "` | split right / split down |
| `leader x` | close block (confirm if running) |
| `leader z` | zoom block |
| `leader f` | float block / re-tile block |
| `leader h j k l` and `leader arrows` | focus block by direction |
| `leader H J K L` | resize block by 5 |
| `leader space` | next layout preset |
| `leader ,` | rename block |
| `leader $` | rename session |
| `leader s` | session picker |
| `leader w` | tab and block picker |
| `leader [` | copy mode |
| `leader ]` | paste |
| `leader :` and `leader P` | command palette |
| `leader ?` | key binding help |

Non-leader defaults: `Alt+arrow` focus by direction, `Shift+PageUp/PageDown` scroll, mouse
click to focus, drag on frames to resize, wheel to scroll or forwarded when the block tracks
the mouse. A `locked` mode passes every key to the block until `leader L` is pressed again.
`leader S` toggles synchronized input for the tab, sending typed keys to every block in it;
individual blocks can opt out, which tmux's synchronize-panes cannot do. The status bar shows
the active mode and the bindings that apply in it, so the key model documents itself the way
zellij's mode ribbons do.

### 5.3 Command palette

`SelectionPrompt` in a popup listing every action with its binding and description. Typing
filters. Actions that need arguments open a follow-up prompt. The palette also accepts raw
command lines in CLI syntax, so `split --right --command htop` works from the keyboard.

### 5.4 Scrollback, selection, and copy

Provided by Hex1b's terminal widget per block: scroll with keys or wheel, copy mode with
cursor and selection, word motions, and mouse selection. Copied text goes to the system
clipboard through OSC 52 and to the server's paste buffer so `leader ]` pastes into any block.
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

### 6.1 Methods

| Method | Purpose |
| --- | --- |
| `server.info` / `server.shutdown` | Identity, uptime, counts; graceful shutdown. |
| `session.list` / `session.create` / `session.get` / `session.rename` / `session.close` | Session lifecycle. |
| `session.attach` / `session.detach` | Register a client viewport and receive geometry; release it. |
| `session.setSize` | Report a client's viewport; the policy decides the authoritative size. |
| `tab.list` / `tab.create` / `tab.select` / `tab.rename` / `tab.close` / `tab.move` | Tabs. |
| `block.list` / `block.get` / `block.create` / `block.close` / `block.kill` / `block.rename` | Blocks. |
| `block.split` / `block.float` / `block.tile` / `block.zoom` / `block.focus` / `block.swap` | Layout. |
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
`tab.renamed`, `tab.closed`, `block.created`, `block.titled`, `block.exited`, `block.closed`,
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

`~/.config/weft/config.json` (JSON with comments and trailing commas allowed), read at
server start and client start, reloadable with `weft reload`.

```json
{
  "leader": "ctrl+b",
  "frames": true,
  "sizePolicy": "latest",
  "scrollback": 10000,
  "shell": null,
  "theme": "default",
  "bindings": { "leader h": "block.focus --left", "alt+enter": "block.zoom" },
  "hooks": { "block.exited": "notify-send weft \"block {block} exited {exitCode}\"" }
}
```

Key syntax: modifiers `ctrl`, `alt`, `shift` joined with `+`, key names as Hex1b names in
lower case, chords separated by spaces, `leader` as a token.

## 9. Agent surface

Agents get the same server through three doors:

- **CLI with `--json`** for anything scriptable.
- **Control socket** for long-lived integrations that want events.
- **`weft mcp`**, a Model Context Protocol server over stdio exposing tools:
  `list_sessions`, `create_block`, `run` (await exit with head and tail output),
  `send_keys`, `capture`, `wait_for`, `close_block`, and a `block://` resource per block.

Design rules borrowed from what agent runtimes actually do: `run` returns after a yield time
with partial output and a live block id instead of blocking forever; output is truncated head
and tail with an omission marker and total byte counts; captures carry revisions; waits are
pattern-based with rate limits; exited blocks keep their exit code until closed.

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
| Sync input with per-block opt-out | zellij | `leader S` |
| Read-only watcher attach | zellij, tmux `-r` | `--read-only` |
| Stable JSON output as a documented contract | wezterm `cli list --format json` | `--json` everywhere |
| Smart client over a dumb transport | wezterm mux, Superlogical | HMP1 per block |
| Restore fidelity as a policy knob | shpool | `attach --restore screen|lines:N` later |
| `SSH_AUTH_SOCK` indirection | shpool | Section 10 |
| Event hooks that tests block on instead of sleeping | shpool `test_hooks` | `events.subscribe` in tests |
| Pause instead of disconnect for slow clients | tmux's failure mode | Section 4.6 |

## 11. Security

- Sockets live in a `0700` directory owned by the user; there is no network listener.
- The server runs commands as the user; there is no privilege boundary between clients of the
  same user, which matches tmux and screen.
- Read-only clients are enforced at the server, not the client.
- No telemetry, no outbound connections.

## 12. Performance budgets

- Keystroke to PTY write: under 1 ms inside the server.
- Output to client paint: bounded by Hex1b's frame limiter (16 ms) plus socket latency.
- Attach with 20 blocks: under 300 ms to first full paint on a local socket.
- Idle server with 50 blocks: no periodic wakeups; every loop awaits I/O.
- Native AOT binary under 25 MB with no runtime dependency.

Benchmarks in `benchmarks/Weft.Benchmarks` cover layout computation, protocol encoding, and
capture serialization.

## 13. Testing

Real processes only. Test tiers:

- **Model tests**: layout tree, geometry, targets, protocol codec, configuration parsing.
- **Server tests**: start a real server on a temporary socket dir, drive it over the real
  control socket with the client library, spawn real shells (`sh`), capture real output.
- **Client tests**: run the attach UI headless with `WithHeadless()` against a real server and
  assert screen text with Hex1b's snapshot and automator APIs.
- **End-to-end tests**: publish the executable and drive it with the installed `hex1b` tool
  through a hosted terminal: keys in, screen assertions out.

No mocking libraries, no hand-written substitutes for production services, no skipped tests.

## 14. Packaging

Native AOT per RID (`linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`,
`osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`) as a `dotnet tool` package `weft` and as
GitHub release archives. Windows relies on Hex1b's ConPTY proxy and AF_UNIX support and is
best-effort until its tests run in CI.

## 15. Phases

See [progress.md](progress.md). Phase 1 is durable sessions end to end with a plain layout;
Phase 2 is the full multiplexer UX; Phase 3 is the composable control surface and MCP;
Phase 4 is sharing and operations.

## 16. Open questions

- Scrollback replay on attach: the client's terminal widget cannot be seeded with history
  through public API, so history before attach is viewable through `block.history` in a
  pager overlay rather than inline scrollback. Revisit if Hex1b exposes seeding.
- Cwd tracking relies on shells emitting OSC 7; without it the last known cwd is the block's
  start cwd.
- Whether to keep tabs as a concept or flatten to sessions of blocks. Tabs stay until real use
  argues otherwise.
