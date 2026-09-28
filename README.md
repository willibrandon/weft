# weft

Native terminal workspaces with sessions that stay running.

Weft keeps your shells, tabs, and split panes running when you close a window.
Reopen the app and pick up where you left off. A companion CLI lets scripts and
agents work with the same sessions.

Development focuses on native applications: macOS and Windows, then Linux.
Current builds are for local development and testing. Store distribution and
public releases are outside the current scope.

The installation plan follows each platform: a drag-to-Applications disk image
on macOS, MSIX on Windows, and native packages on Linux. Local builds use normal
app bundles, icons, and dependencies without requiring signing accounts or
certificates. Linux packaging will follow its application.

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

## Build the Windows app

Install a compatible .NET 11 SDK, including previews, and Visual Studio or its Build
Tools with the **Desktop development with C++** workload and a Windows SDK:

```console
dotnet run --file scripts/Build-WindowsApp.cs
```

Run `artifacts\windows\win-x64\Weft\Weft.exe`. On ARM64, the directory is
`win-arm64`. The app includes its server and runtime.

Open **Commands** from the **…** menu to find an action, press Ctrl+Shift+F to search
terminal history, and open **Settings** from the same menu to change fonts, colors, or
shortcuts. Closing a window or exiting Weft leaves sessions running.

Build an unsigned development MSIX after building the app:

```console
dotnet run --file scripts/Package-WindowsApp.cs
```

Windows installs unsigned packages that contain programs only for an administrator.
From an elevated PowerShell, install it with
`Add-AppxPackage -Path artifacts\windows\Weft-0.1.0-x64.msix -AllowUnsigned`.
It adds Weft to the Start menu and a `weft-desktop` command. This is a development
package, not a signed public release.

## Development

```console
dotnet build Weft.slnx
dotnet test --solution Weft.slnx
dotnet run --file scripts/Test-MacApp.cs
dotnet run --file scripts/Test-WindowsApp.cs
```

The CLI and terminal attachment remain available for automation and terminal-only
environments. Run `dotnet run --project src/Weft.App -- --help` for commands.
The terminal attachment has clickable Help and Exit controls; exiting it also
keeps sessions running.

Architecture: [docs/design.md](docs/design.md). Conventions: [CONTRIBUTING.md](CONTRIBUTING.md).
