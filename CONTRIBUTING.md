# Contributing

Install any compatible .NET 11 SDK, including previews. Native AOT publishing on Linux
also needs `clang` and the zlib development headers. On Windows it needs Visual Studio or
its Build Tools with the Desktop development with C++ workload; MSIX packaging also uses
the Windows SDK's MakeAppx and MakePri. The .NET SDK locates the linker through
`vcvarsall.bat`, which fails when `PATH` exceeds the command prompt's limit of about
8,000 characters, and publishing then reports that the platform linker was not found.
Shorten `PATH` if that happens.

Then run:

```console
dotnet restore Weft.slnx
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
dotnet run --file scripts/Verify-Repository.cs
dotnet format Weft.slnx whitespace --verify-no-changes --no-restore
dotnet format Weft.slnx style --verify-no-changes --no-restore
```

Tests use MSTest 4 on Microsoft.Testing.Platform. Always run `dotnet test` and
never use `--no-build`. Do not pass `--nologo` to `dotnet test`: it is forwarded to the
test host, which rejects it and reports zero tests. Product tests exercise real processes, pseudo-terminals,
Unix-domain sockets, and files. Mocking libraries and hand-written substitutes for
production services are prohibited.

Each C# file contains one type. Every public or internal type and member has
triple-slash XML documentation, and each `<summary>` uses exactly three lines:
an opening tag, one text line, and a closing tag. Code follows the dotnet/runtime
C# coding style, enforced by `.editorconfig` and `dotnet format`.

Swift uses DocC `///` comments for types, shared entry points, and behavior whose
contract is not obvious from its name. Explain ownership, threading, coordinate
systems, or input handling where they matter. Skip comments that merely repeat a
property name or a routine AppKit override; add parameter and return sections only
when they clarify use.

The Mac app currently links Apple system frameworks and has no Swift package
dependencies. Dependabot covers NuGet and GitHub Actions. Add its `swift` ecosystem
when a Swift package manifest introduces external dependencies; Xcode and Swift
remain supplied by the build environment without version pins.
Mac CI selects the newest numbered Xcode release installed on the runner, since
the Intel image's default compiler predates isolated protocol conformances.
The selection applies only to the job through `DEVELOPER_DIR`.
App and native test compilation use Swift batch mode with concurrency based on
the host's processor count, avoiding repeated parsing for every individual file.

CodeQL scans C# on Linux and again on Windows, where the Windows SDK, WinUI, Reactor, and
Win2D packages resolve, and builds the Mac app for Swift analysis on macOS. The C# jobs
read the sources without a build, because a traced build also extracts source generator
output that is not ours and that the path filter cannot exclude for compiled languages.
Each job runs the security and quality queries and fails on any finding. `Weft.SourceGen`
mirrors the queries that have fired here, so a finding fails the local build first; when
CodeQL reports something the build did not, extend or add the matching analyzer with a test. The Swift build
prepares dependencies with `Build-MacApp.cs --prepare-analysis`, then traces Swift
compilation using the generated response file. It reads the C bridge directly with
`-disable-bridging-pch`, avoiding compiler-specific precompiled headers. Scanning does not open a terminal
window. Add native Linux
C/C++ analysis with that frontend.

Builds explicitly enable all code-style analyzers and treat their findings as errors.
CI follows the rolling .NET 11 preview channel and records `dotnet --info` so its SDK
can be compared with a local build. Only the preview SDK notice NETSDK1057 is suppressed.

The current preview CLI native dispatcher looks for `dotnet-format` outside the SDK
directory. If formatting reports a missing tool DLL, run the same command with
`DOTNET_CLI_ENABLEAOT=false` to use the SDK's managed dispatcher. The CI formatting
steps use this setting; it does not affect Native AOT publishing of weft.

Repository automation is implemented only as .NET file-based C# apps under
`scripts/`. Shell, PowerShell, batch, and command scripts are not used.

The Windows app and its tests build only on Windows, from `Weft.Windows.slnx`:

```console
dotnet build Weft.Windows.slnx
dotnet test --project tests/Weft.Desktop.Windows.Tests
dotnet format Weft.Windows.slnx whitespace --verify-no-changes
dotnet format Weft.Windows.slnx style --verify-no-changes
```

The window tests host the app inside the test process and open real windows behind the
active window, each against a private server, without taking the keyboard or changing
your preferences or sessions. They capture pixels with Windows Graphics Capture, which
needs Windows 10 version 21H2 or later and lets desktop apps capture windows, as Windows 11
does by default. Windows Server asks first, so in CI `Test-WindowsApp.cs` grants that
consent before the tests run. `Test-WindowsApp.cs` also starts the published
app in front, so keep typing elsewhere until it finishes. Its `--package` option installs
the development MSIX and needs an elevated terminal.

For Mac redraw investigation, `dotnet run --file scripts/Test-MacApp.cs -- --record-output`
opens one temporary window against an isolated server, runs `ls` in Nushell for six
seconds, then closes it. It requires Nushell on `PATH`, macOS 14 or newer, and screen
recording permission. Changed compositor frames and timing CSVs are saved beside
the native smoke-test artifacts. Normal smoke tests do not capture the desktop.

`dotnet run --file scripts/Test-MacApp.cs -- --profile-memory` runs the memory sample
in a fresh native process against an isolated server. It captures `vmmap` and `heap`
reports in the architecture's artifact directory, then exits after a bounded pause.
Normal package tests also sample input delivery, idle CPU, and memory across client
churn, separately from the process that captures rendering-test bitmaps.

`dotnet run --file scripts/Test-MacApp.cs -- --qualify` runs the 16-tab output and
typing workload, scrolling, resize and backing-scale checks, multiple attachments,
and forced client termination. Normal native tests include these checks. Metrics
are saved in the architecture's `qualification.json`. Initial regression limits
are listed in [Mac qualification](docs/macos-qualification.md); they still need
repeated hardware validation and are not portable release budgets. Tests move their private window across connected
displays when more than one is available.

`--accessibility` checks the external macOS accessibility tree. It can also check
VoiceOver when it is already running with AppleScript control enabled. The test
never enables VoiceOver or changes the selected keyboard layout. The regular
suite translates French, German, and Spanish keys through installed Apple layouts
and checks exact PTY bytes, plus composition replacement and cancellation.

`--graphics-app /path/to/rbirds` runs an existing rbirds binary in Kitty and Sixel
modes against the private server, records animation measurements, and saves native
captures. It does not build or modify the reference checkout.

Design decisions live in `docs/design.md` and work tracking in `docs/progress.md`.
Update both alongside code changes.
