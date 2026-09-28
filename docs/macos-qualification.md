# Mac qualification

The Mac feature inventory is in [desktop parity](desktop-parity.md). This record
tracks the evidence still needed before the branch is ready. Windows and Linux
implementation, store distribution, public signing, and automatic updates remain
separate work.

## Current evidence

An earlier local baseline passed 547 .NET tests and installed-app checks on
ARM64 and Intel under Rosetta. The 800-bird workload at 165×43 cells, with 9×20-point
cells, measured 59.9 observed Kitty frames/s and 52.3 Sixel frames/s. The
[progress log](progress.md#verification-log) records CPU, memory, and drawing costs.

The native cache releases images absent from the latest frame
and bounds retained decoded row storage to 32 MiB and 256 images. Swift compilation
with warnings as errors and cache checks using real capture files pass. This does
not establish improved animation throughput or lower process footprint by itself.
After execution access was restored, all 547 .NET tests passed again, and the real
Kitty and Sixel workloads passed the initial limits. The Native AOT client now
uses speed-focused optimization, matching the server. Repeated graphics and
installed-app measurements distinguish local results from CI hardware coverage.

The ARM64 installed-DMG suite passes, including the 16-tab workload, resource
limits, native rendering and input, crash recovery, upgrade, removal, and reinstall.
Its loaded input-to-paint p95 was 50.8 ms and tab-switch p95 was 81.3 ms. Native
Intel qualification is covered by the installed-app CI job on native Intel
hardware; its results and host report are attached to PR 18. Swift CodeQL passes with zero findings.
Interactive checks below
remain open; only one physical display was connected and VoiceOver was off.

On the Apple M4 Pro, the preceding balanced-optimization run measured 49.4 Sixel
frames/s, 130.7% client CPU, and 341.8 MiB resident memory. Two speed-optimized runs
measured 56.2 and 53.1 frames/s, 117.6% and 117.3% CPU, and 331.2 and 330.4 MiB
resident memory. Sampled footprint peaks were 619.2 and 604.9 MiB; those peaks do
not establish a footprint reduction. Kitty measured 59.9 and 57.3 frames/s.
The native library grew from 7,590,704 to 8,180,496 bytes. The same workload,
viewport, and initial limits were used throughout. Steady 60 fps is not established.

## Initial regression limits

These limits are implemented in `PerformanceLimits.swift`, using prior local
measurements with headroom. Validate them on repeated ARM64 and native Intel runs
before treating them as accepted platform budgets. Investigate failures rather
than raising limits to make a run pass. Graphics comparisons require the geometry
above; measurements from different geometry are saved but fail comparison.

| Measurement | Initial limit |
| --- | --- |
| Warm attachment p50 | 500 ms |
| Idle input-to-paint p95 | 100 ms |
| Idle client CPU over 750 ms | 50 ms |
| Resident growth from first to fifth closed client | 64 MiB |
| Loaded input-to-paint p95 | 150 ms |
| Tab switch p95 | 500 ms |
| Loaded native draw p95 | 33 ms |
| Loaded client resident memory | 512 MiB |
| Resident growth after closing the 16-tab workload | 192 MiB |
| Observed Kitty / Sixel animation | At least 55 / 48 frames/s |
| Graphics native draw p95 | 8 ms |
| Graphics client CPU | 200% of one core |
| Graphics client resident / sampled physical footprint | 600 / 800 MiB |

Graphics measurements cover the client process, excluding the server and rbirds.
Sampled footprint is not an allocation peak. The cache's decoded byte accounting
excludes shared frame buffers, compositor surfaces, and other process memory.

## Repeatable checks

Run `dotnet test --solution Weft.slnx`, then build and package each architecture
with `Build-MacApp.cs` and `Package-MacApp.cs`. Run
`dotnet run --file scripts/Test-MacApp.cs -- --arch arm64 --package` and repeat with
`--arch x64` on native Intel hardware. Rosetta results remain separately identified.

For graphics, run `dotnet run --file scripts/Test-MacApp.cs -- --graphics-app PATH`
with the existing rbirds executable. This reads the reference build without
modifying its checkout. Repeat the workload before and after changes, preserving
the viewport, font metrics, display scale, workload revision, and host conditions.

CI retains architecture-specific qualification JSON, graphics JSON and captures,
including all 100 input timing samples and initial window painting measured
separately. Typing measurements begin after the prompt has been painted; the
100 ms p95 limit is unchanged. The harness uses accessory activation and verifies
window visibility before timing. First-window painting includes attachment and
initial drawing. Earlier background-only runs did not verify visibility; none of
these AppKit timings measure compositor presentation latency. The final local
installed-app suite passes with idle input p95 12.1 ms, loaded input p95 21.4 ms,
and tab switch p95 6.4 ms. Upgrade, removal, reinstall, and forced client
termination preserve the server and shells.
Reports also include host/target architecture details and the private server log
when a test fails.
Paced output reports include formatting, PTY write, sleep, and elapsed timings.
An active server profile identified allocation contention during concurrent
scrolling. Newline-containing batches now take turns, with at most eight line
breaks per batch; ordinary character echo and graphics without newlines bypass
that queue. The executable uses server GC with the runtime's adaptive heap sizing;
the native client retains workstation GC. The producer uses absolute frame deadlines
so blocked writes and delayed timers do not accumulate extra sleep. AppKit batches ASCII glyphs at fixed
cell positions. Partial redraw, Unicode and symbol captures, and all 548 .NET
tests pass. CI exercises the workload on both architectures.
Graphics qualification passes: Kitty measured 59.9 frames/s and Sixel 51.8, with client CPU 133.6% and
106.5% of one core. Resident memory was 145.0 and 306.3 MiB; sampled footprint
maxima were 328.6 and 552.2 MiB. These measurements use the same 800-bird workload
and 165×43 grid. Earlier runs under heavy competing host load missed the limit on
both the previous and updated server; those results remain in the progress log.
The external rbirds workload is optional local qualification; CI does not download
or build that reference repository. Swift CodeQL's first successful GitHub scan
is recorded in [PR 18](https://github.com/willibrandon/weft/pull/18).

## Hands-on checks still open

Record the machine, OS, input method or physical device, actions performed, and
result for each check. API tests and simulated drawing scales do not close these
items.

| Check | Required evidence |
| --- | --- |
| VoiceOver | Read and navigate output, select and copy, use native controls, and enter/leave search and dialogs without unwanted output announcements |
| Japanese and Chinese input | Visible candidates follow composition through resize, scrolling, pane changes, and cancellation; committed text appears once |
| Physical non-US keyboards | Common layouts, dead keys, Option combinations, repeat, and command search work without shortcut collisions |
| Mixed-scale displays | Move a live window between different physical displays while composing, scrolling, drawing graphics, and entering fullscreen |

Use private sessions and bounded launches. Never enable VoiceOver or switch system
keyboard layouts automatically. Leave the user's running terminals untouched.
