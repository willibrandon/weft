# weft

Native terminal workspaces with sessions that stay running.

Weft keeps your shells, tabs, and split panes running when you close a window.
Reopen the app and pick up where you left off. A companion CLI lets scripts and
agents work with the same sessions.

Development focuses on native applications: macOS first, then Windows and Linux.
Current builds are for local development and testing. Store distribution and
public releases are outside the current scope.

The installation plan follows each platform: a drag-to-Applications disk image
on macOS, MSIX on Windows, and native packages on Linux. Local builds use normal
app bundles, icons, and dependencies without requiring signing accounts or
certificates. Windows and Linux packaging will follow their applications.

See the [desktop design](docs/standalone-app.md) and [progress tracker](docs/progress.md).

## Build the macOS app

Install a compatible .NET 11 SDK, including previews, and Xcode with its command
line tools:

```console
dotnet run --file scripts/Build-MacApp.cs
```

Open `artifacts/macos/osx-arm64/Weft.app` in Finder. On Intel, the directory is
`osx-x64`. The app includes its server and runtime.

Use **View → Commands** to find an action, **Edit → Find** to search terminal
history, and **Weft → Settings** to change colors or shortcuts. Closing a window
or quitting Weft leaves sessions running.

Build a local disk image after building the app:

```console
dotnet run --file scripts/Package-MacApp.cs
```

Open the resulting `Weft.dmg` beside the app bundle and drag Weft to Applications.
The build applies an automatic ad hoc signature without a certificate or Apple
account. These local packages are not notarized public releases.

## Development

```console
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
dotnet run --file scripts/Test-MacApp.cs
```

The CLI and terminal attachment remain available for automation and terminal-only
environments. Run `dotnet run --project src/Weft.App -- --help` for commands.
The terminal attachment has clickable Help and Exit controls; exiting it also
keeps sessions running.

Architecture: [docs/design.md](docs/design.md). Conventions: [CONTRIBUTING.md](CONTRIBUTING.md).
