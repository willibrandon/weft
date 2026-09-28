# Native app parity

This is the porting contract for the macOS, Windows, and Linux applications. It
records the Mac implementation that the other frontends need to match, the
platform adaptations they need, and the evidence required before calling a
feature complete. Architecture lives in [standalone-app.md](standalone-app.md);
current work and measurements live in [progress.md](progress.md).

macOS and Windows are implemented and undergoing qualification. Linux is planned
and has no frontend yet. A shared implementation or a passing test on one platform
does not establish that a feature works on another.

## What parity means

The same session must preserve its processes, output, layout, graphics, and command
semantics across clients. Users must be able to discover every supported action
through the interface. Fonts, menus, shortcuts, window decorations, accessibility,
input methods, and installation should follow the operating system.

Parity does not require matching screenshots or translating Command directly to
Control. In particular, desktop commands must not take Control+C away from a
terminal process. Review Windows and Linux shortcut tables before implementing
them. The accepted Mac defaults are in the [native app design](standalone-app.md#first-macos-implementation).

Closing a window detaches it. Quitting exits the client. Both leave server-owned
sessions running. Closing a pane, tab, or session terminates work and is a separate,
clearly labeled operation with confirmation. Esc dismisses an active dialog or
reaches the focused terminal; it never exits the application.

## Implementation boundaries

| Responsibility | Shared implementation | Native responsibility |
| --- | --- | --- |
| Session authority | Server, control protocol, HMP1, stable session/tab/block IDs | Show authoritative state and connection status |
| Client lifecycle | `Weft.Client.Core`: launcher, connections, ordered commands, reconnect, mirrors | Window lifetime, application activation, UI thread dispatch |
| Terminal state | Hex1b through public APIs; `DesktopTerminal` projects cells, history, selection, mouse encoding, graphics | Draw the projection and deliver native input; no second VT parser |
| Actions | `DesktopActions` and `DesktopCommand`, including scope and termination semantics | Native menus, search, dialogs, enabled state, shortcut configuration |
| Frame delivery | Bounded work, newest frame, completed synchronized presentations | Coalesced drawing, display scaling, caches, input responsiveness |
| Platform boundary | Windows references the managed core; Mac and Linux use `Weft.Client.Native` | Own and release buffers and callbacks according to the interface contract |
| Preferences | Weft configuration controls new shell processes | Appearance, fonts, window placement, and shortcuts use platform storage |

The C interface is currently ABI 4: a little-endian metadata length, UTF-8 JSON,
then raw shared textures. Its header currently lives in
[`WeftNative.h`](../native/Weft.Desktop.Mac/WeftNative.h). Move this contract to a
shared native include location when adding Linux, keeping one authoritative
declaration. Check the ABI before attaching; never guess a buffer layout. Callbacks
must drain before their context is released, and the AOT library remains loaded
for the process lifetime. Windows uses the managed frame directly rather than
serializing it through this bridge.

Tab selection and active-pane state currently follow server authority and can
affect another attached client. Independent per-window selection needs a shared
protocol change. It must not emerge accidentally from frontend-specific state.

## Capability inventory

Every row below is required in Windows and Linux unless an explicit platform
adaptation is recorded. “Implemented” describes the Mac baseline, not a claim
that every hardware or compatibility case is qualified.

| Capability | Mac baseline and behavior to preserve | Port acceptance |
| --- | --- | --- |
| Startup | Bundled server resolved by path; on-demand start; configured shell used for new terminals | Launch outside a shell with a minimal PATH. Existing sessions keep their processes |
| Launch safety | Distinct GUI/server binaries, caller identity guard, server-argument rejection | No recursive windows or server restart during repeated application activation |
| Windows and restoration | New window, last-session reopening, normal close and quit | Two attachments, client crash, reopen, and unchanged server/shell identities |
| Reconnect | Retain stale content, show status, reject uncertain input, release a closed session cleanly | Disconnect during input and commands; no replay or duplicate destructive action |
| Sessions | Create, select, rename, close | Menus and command search agree; cancellation leaves work intact |
| Tabs | Create, select, cycle, rename, close; visible hover close control | Closing an inactive tab does not first select it; keyboard and accessibility access remain available |
| Panes | Split right/below, focus, resize dividers, arrange, zoom, float/tile, rename, close | Match server geometry through resize and reconnect; preserve focus after dialogs |
| Broadcast input | Shared command with confirmation | Deliberate fan-out only; stopping broadcast restores independent input |
| Command search | One searchable panel using shared action identities | Repeated opening focuses the same panel; dismissal restores prior focus |
| Shortcuts | Accepted Mac defaults, recording, conflict rejection, reset | Reviewed native mappings; every action remains discoverable without a shortcut or prefix mode |
| Text | Cell-aligned graphemes, wide cells, combining marks, fallback, emoji, styles and colors | Pixel and copied-text checks across fonts, scale changes, wrapping, and mixed Unicode |
| Cursor | Shape and blink requested by terminal; focused/visible lifecycle and reduced-motion handling | No stale caret pixels; steady styles stay steady; selection never moves the process cursor |
| Redraw | Retained pixels and changed-row invalidation; synchronized updates complete before display | Prompt editing and repeated `ls` without blank flashes; unfinished synchronization eventually presents |
| History | Bounded retained rows; native thumb proportions, page steps, dragging, wheel and precise scrolling | History before attachment, endpoints, new output while scrolled back, resize, and Resume Live Output |
| Find | Search retained terminal rows, navigate matches | Query survives resize; matches agree with displayed text and selection |
| Selection | Click leaves caret alone; drag, word, row, all, and edge autoscroll | Copy across viewports while output arrives; release stops autoscroll; geometry changes invalidate stale anchors |
| Clipboard | Focus-aware Copy/Paste/Select All and terminal paste encoding | Plain text and multiline paste reach the real PTY correctly; text fields retain normal editing |
| Mouse reporting | Core encodes negotiated terminal mouse events; Shift permits selection | Buttons, drag, motion, wheel, modifier handling, and selection override |
| Links | OSC 8 activation allows HTTP, HTTPS, and mail links | Use the platform link modifier and opener; output alone never opens a URL or file |
| Keyboard and composition | AppKit text input, dead keys, marked text, replacement, cancellation on pane change | Exact committed bytes, AltGr/Option, repeat, candidate positioning, no duplicate commit |
| Kitty graphics | Shared atlases, animation, layers, cropping, offsets, fractional native sizes, deletion | Full flock in rbirds; correct source rectangle, orientation, synchronization, and bounded resources |
| Sixel graphics | Newest visible pixels composited into a bounded plane; transparent overlap and clipping | Full visible animation without displacement; damaged/hidden pixels stay hidden through scrolling |
| Accessibility | Read-only terminal text, Unicode ranges, bounds, selection, native controls | Native screen reader can navigate controls and text; selection agrees with Copy and does not type into the shell |
| Appearance | Bundled licensed regular/italic font and symbol fallback; font and color preferences | Fonts stay app-local; native picker/settings; prompt configuration stays in the user's shell |
| Window chrome | Native title bar, session selector, tabs, add/actions controls, pane titles | Alignment, truncation, hover/focus states, compact widths, fullscreen and scaling checked visually |
| Icon | Original woven W rendered into Mac bundle sizes | Same identity adapted to native icon formats, sizes, backgrounds, and contrast |
| Cleanup | Closed views release frames, caches, layers, callbacks, timers, and attachments | Repeated creation/closure reaches a stable memory level; no work continues for disposed views |
| Installation | ARM64/Intel app bundles and local DMGs; install/upgrade/remove/reinstall tests | Installed artifacts run without a .NET SDK and preserve live sessions and user data |

The catalog is the source of command names. New actions added during Mac work must
extend this inventory and the port tests in the same change. Server-only or CLI-only
features are not implicitly declared native UI features by this table.

## Native platform mapping

| Area | macOS, implemented | Windows, implemented | Linux, planned |
| --- | --- | --- | --- |
| Application | Swift and AppKit | C# and Microsoft UI Reactor over WinUI | Small C frontend using GTK4 |
| Core access | Native AOT C interface | Direct managed core, published with Native AOT | Native AOT C interface |
| Terminal surface | Custom AppKit view, CoreText and CoreGraphics | Stable control over a Win2D virtual canvas; glyph runs at cell positions, DirectWrite for complex cells | Custom GTK widget; prototype GTK snapshot drawing with Pango text |
| Chrome | macOS menu bar, toolbar, sheets, native window controls | Windows title bar, tabs, menus/flyouts, dialogs, snap and system controls | GTK menus, dialogs and tabs; respect desktop decoration and window-manager conventions |
| Composition | `NSTextInputClient` | Windows text services through a borderless text box at the caret | `GtkIMContext`, preedit/commit/cancellation, cursor geometry |
| Accessibility | AppKit accessibility and VoiceOver | UI Automation text pattern and ranges from the terminal's peer, and Narrator | GTK accessible text and AT-SPI, exercised with Orca |
| Scrolling | AppKit scrollbar and wheel/trackpad events | Native scroll bar, 120-unit wheel detents and accumulated precision deltas | GTK adjustment/scrollbar, discrete and smooth scrolling on Wayland and X11 |
| Preferences | macOS defaults and native font panel | `%LOCALAPPDATA%\weft\desktop.json` and a native Settings window | XDG configuration and GTK settings controls |
| Installation | Ad hoc signed `.app` in DMG | Unsigned development MSIX; the server runs from a copy outside the package | Local `.deb` and `.rpm` packages |

The Linux drawing backend remains a prototype decision. It must meet terminal
behavior and measured responsiveness before becoming the production path. Keep cell output out of general UI reconciliation: no component
or native text control per terminal cell.
Batch glyphs at explicit cell positions, preserving a fallback for complex text
and individual clipping. The Mac renderer avoids a separate AppKit text layout
for each ASCII character; Windows and Linux need equivalent batching in their
chosen drawing backends.

### Windows

Use the newest published preview of `Microsoft.UI.Reactor` when the frontend
starts. Its version belongs only in `Directory.Packages.props`; the project uses a
versionless reference. Review the read-only checkout against the published
[documentation](https://microsoft.github.io/microsoft-ui-reactor/latest/) and
[package](https://www.nuget.org/packages/Microsoft.UI.Reactor), since the checkout
may be ahead of the release. Do not reference or build the checkout into Weft.

Project shared actions into Reactor's command model with deliberate focus scopes.
Use stable IDs for tabs and panes so reconciliation cannot recreate a terminal
surface on each output update. Validate custom control lifetime, Windows text
input, accessibility, theme resources, and package assets in an actual AOT-published
application. Follow the [Reactor AOT support matrix](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/aot-support.md)
and use explicit registration where needed; leave reflection-dependent developer
inspection out of the published app. Do not carry package workarounds forward
without checking whether they apply to the release being consumed.

Prove the entire server path on Windows early, including PTY creation, local socket
access, permissions, runtime paths, and shutdown. Unix tests do not prove Windows
support. A window, parent process, or job object must not own the durable server's
lifetime.

Development MSIX uses Windows' [unsigned package installation path](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package).
Executable unsigned packages require administrator installation. Implement the
helper as C# automation, explain elevation, and preserve real package identity,
Start menu integration, resources and uninstall behavior. This development identity
is not the eventual signed release identity; migration between them needs its own
test. No certificate creation or trust-store changes are part of local setup.

The Windows app now covers every capability row. Its server runs from a copy in the
user's local data, started outside the package, because Windows ends a package's
processes when the package updates or is removed. A Reactor reconciliation detail
matters for any conditional layout: children are matched by position unless keyed,
so a row that appears before the terminal surface must be keyed or it replaces the
surface and its session connection.

### Linux

GTK4 is the chosen native frontend plan. Use the shared C interface and public GTK
APIs, keeping platform objects out of the .NET core. Pango must preserve the terminal
grid while shaping text and choosing fallback fonts. Test GTK behavior in both
GNOME and KDE; avoid a mandatory desktop-specific extension library in the initial
frontend.

Use [`GtkIMContext`](https://docs.gtk.org/gtk4/class.IMContext.html) for text
composition and candidate positioning. Map the shared terminal text to
[`GtkAccessibleText`](https://docs.gtk.org/gtk4/iface.AccessibleText.html), including
range geometry and selection. The package's minimum GTK API level must include
the interfaces actually used. Do not pin a toolchain release to obtain them or
assume every supported distribution already supplies them.

Clipboard selection, link opening, window activation, decorations, and scaling
need Wayland and X11 coverage. Review primary-selection and middle-click behavior
with the Linux shortcut/input design; do not silently add a second paste path.
Use desktop entries, native icon assets, XDG paths, and declared runtime package
dependencies. GTK's installed runtime libraries come from the distribution, not
an unrelated NuGet or Swift dependency list.

Start with glibc x64 and ARM64 `.deb` and `.rpm` installations. Musl desktop support
is optional and requires a working GTK, font, graphics, PTY and AOT dependency
stack plus installed-app tests. Server/CLI musl support is a separate qualification;
neither glibc success nor a portable C interface establishes musl support.

## Shared verification contract

Run real shells, PTYs, sockets, files, servers, and native windows. Reuse the shared
MSTest coverage and port the observable scenarios under
[`native/Weft.Desktop.Mac/Tests`](../native/Weft.Desktop.Mac/Tests), with native
automation appropriate to each platform. Do not port AppKit test implementation
details or replace production services with substitutes.

| Scenario group | Existing Mac evidence to carry forward |
| --- | --- |
| Workspaces and commands | `MacSmoke`, `TabInteraction`, real core command tests |
| Scrollback and selection | `ScrollObservation`, `HistorySelection`, `OutputFrames` |
| Fonts and redraw | Native pixel captures, prompt echo/erase checks, `OutputRecording` |
| Keyboard and accessibility | `KeyboardLayouts`, `TextComposition`, `TextAccessibility`, `CursorBlink` |
| Graphics | `GraphicsQualification`, core graphics tests, `GraphicsApplication` using a read-only rbirds binary |
| Resource use | `ClientResources`, `ClientWorkload`, per-process CPU, resident size and physical footprint |
| Lifecycle and displays | `WindowQualification`, forced client termination, attachment and process identity checks |
| Installation | `Build-MacApp.cs`, `Package-MacApp.cs`, `Test-MacApp.cs` installed-artifact lifecycle |

For graphics, retain checks for shared atlases, 800 placements, synchronized frames
including timeout, animation, deletion, source crops, offsets, row orientation,
transparent overlap, and viewport clipping. Measure conversion and native drawing
separately. A fast draw call does not establish a fast frame pipeline.

Measure cold launch, reattach, input-to-paint p50/p95, tab switching, output and
graphics frame rate, idle CPU/wakeups, and memory before/after view churn. Record
hardware, OS, architecture, emulation, font/grid size, workload and duration. Report
client, server, and child-process costs separately. Establish per-platform budgets
from reproducible baselines; do not turn one Mac sample into a universal limit.

CI needs native x64 and ARM64 build and installed-app coverage for each claimed
platform. Rosetta and other emulation provide additional evidence, labeled as such.
Build on the target OS, report installed toolchains, and keep the portable solution
usable without another OS's UI dependencies. All repository automation remains
file-based C# under `scripts/`.

CodeQL covers the shared C# implementation, the Windows app with its Windows packages
resolved on a Windows runner, and the compiled Swift frontend. Add C/C++ analysis with the
GTK frontend. Keep the security and quality suites and the zero-findings gate.
Dependabot currently covers NuGet and Actions; Swift package updates become relevant
only if a `Package.swift` introduces dependencies. Reactor uses the existing NuGet
entry. Review native dependency coverage when adding the Linux manifests.

Windows carries the same groups into `tests/Weft.Desktop.Windows.Tests`, which drives
real windows through their input entry points and UI Automation:

| Scenario group | Windows evidence |
| --- | --- |
| Workspaces and commands | `WorkspaceTests`: splits, tabs, inactive tab close, context menu, divider drag, Find, Commands, sessions, rename, broadcast, reattach, narrow windows |
| Scrollback and selection | `TerminalHistoryTests` and `TerminalSelectionTests`: scroll bar through UI Automation, wheel and precision deltas, drag autoscroll, selection held during output |
| Fonts and redraw | `TerminalRenderingTests` pixel captures: prompt-row repaint and exact erase, true color wide text; `SettingsTests` font and color changes |
| Keyboard and accessibility | `TerminalInputTests`, `TerminalAccessibilityTests`, `ShortcutTests`, and the cursor blink pixel test |
| Graphics | `TerminalRenderingTests`: Kitty placement, animation, fractional size, crop, orientation, deletion, and Sixel |
| Resource use and lifecycle | `WindowLifecycleTests`: shared attachments through resizes, released frames and images, window churn, server loss and return |
| Installation | `Build-WindowsApp.cs`, `Package-WindowsApp.cs`, `Test-WindowsApp.cs` installed-package lifecycle |

## Remaining Windows qualification

| Open check | Completion evidence |
| --- | --- |
| Installed package lifecycle | `Test-WindowsApp.cs -- --package` passes from an elevated terminal on x64 and ARM64, with the server and shells outside the package |
| Narrator | Interactive reading, range navigation, selection and Copy, native controls, Find and dialog focus |
| Candidate windows | Japanese and Chinese input methods with candidates anchored at the caret through resize, scroll, and pane switches |
| Physical layouts | Non-US keyboards with AltGr, dead keys, and repeat in the terminal and command search |
| Multiple displays | Move a live window between displays with different scaling, including composition, graphics, and scrolling |
| Performance budgets | Launch, reattach, input-to-paint, tab switching, output and graphics frame rate, idle CPU, and memory after window churn, measured on x64 and ARM64 |

## Remaining Mac qualification

The automated baseline includes installed ARM64 and Intel-under-Rosetta runs,
547 .NET tests, private server lifecycle, 16-tab output, graphics, accessible Unicode
selection, composition callbacks, French/German/Spanish layout translation, and
offscreen 1×/2× drawing. Those tests leave the following checks open:

| Open check | Completion evidence |
| --- | --- |
| Sixel throughput | Speed-focused compilation reduces client CPU in repeated local measurements; steady 60 fps and larger viewport coverage remain unproven, as recorded in [Mac qualification](macos-qualification.md) |
| VoiceOver | Interactive reading, range navigation, selection/Copy, native controls, search and dialog focus; no unsolicited output flood |
| Candidate windows | Japanese and Chinese input methods with visible candidates correctly anchored through resize, scroll, pane switches and cancellation; committed text appears once |
| Physical layouts | Common non-US physical keyboards, dead keys, Option and repeat in terminal and command search; no shortcut collisions |
| Multiple displays | Move a live window between physical displays with different scale/resolution, including composition, graphics, scrolling and fullscreen |
| Native Intel hardware | Execute installed-app qualification on Intel hardware and record it separately from Rosetta |
| Performance budgets | Validate the [initial test limits](macos-qualification.md) against repeated platform baselines, including sustained memory after graphics and window churn |

Use private sessions for qualification. Keep launches bounded and leave the user's
running work alone. Tests never enable a screen reader or change the selected
keyboard layout automatically. A missing display or input device remains an open
check, not a simulated hardware pass.

## Port delivery order

Finish Mac and Windows qualification and keep this inventory current. Follow with
GTK on Linux, proving Wayland/X11 input and accessibility early.
Each frontend must complete its native input, rendering and lifecycle foundation
before expanding its chrome.

Local installation and testing are the delivery target. Public stores, signing
accounts, notarization, automatic updates, remote attachment, and further protocol
extensions remain separate milestones. No telemetry is introduced.
