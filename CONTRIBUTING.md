# Contributing

Install any compatible .NET 11 SDK, including previews. Native AOT publishing on Linux
also needs `clang` and the zlib development headers.

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

CodeQL scans C# on Linux and builds the Mac app for Swift analysis on macOS. Both
jobs run the security and quality queries and fail on any finding. The Swift build
prepares dependencies with `Build-MacApp.cs --prepare-analysis`, then traces Swift
compilation using the generated response file. It reads the C bridge directly with
`-disable-bridging-pch`, avoiding compiler-specific precompiled headers. Scanning does not open a terminal
window. Add native Linux
C/C++ analysis with that frontend, and validate Windows-specific C# extraction
on a Windows runner when the Reactor project lands.

Builds explicitly enable all code-style analyzers and treat their findings as errors.
CI follows the rolling .NET 11 preview channel and records `dotnet --info` so its SDK
can be compared with a local build. Only the preview SDK notice NETSDK1057 is suppressed.

The current preview CLI native dispatcher looks for `dotnet-format` outside the SDK
directory. If formatting reports a missing tool DLL, run the same command with
`DOTNET_CLI_ENABLEAOT=false` to use the SDK's managed dispatcher. The CI formatting
steps use this setting; it does not affect Native AOT publishing of weft.

Repository automation is implemented only as .NET file-based C# apps under
`scripts/`. Shell, PowerShell, batch, and command scripts are not used.

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
