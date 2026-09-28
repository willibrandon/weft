#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Weft.Scripts;

/// <summary>
/// Packages an existing local macOS build as a drag-to-Applications disk image.
/// </summary>
internal static class PackageMacApp
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync("Packages a built Weft.app for local installation without certificates or notarization. Usage: dotnet run --file scripts/Package-MacApp.cs [--arch arm64|x64]").ConfigureAwait(false);
            return 0;
        }
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Package the macOS app on macOS.");
        }

        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (args is ["--arch", "arm64" or "x64"])
        {
            arch = args[1];
        }
        else if (args.Length != 0)
        {
            throw new ArgumentException("Usage: dotnet run --file scripts/Package-MacApp.cs [--arch arm64|x64]");
        }

        string output = Path.Join(Directory.GetCurrentDirectory(), "artifacts", "macos", "osx-" + arch);
        string app = Path.Join(output, "Weft.app");
        if (!File.Exists(Path.Join(app, "Contents", "MacOS", "weft-server")))
        {
            throw new FileNotFoundException("Build the app with scripts/Build-MacApp.cs first.", app);
        }

        await RunAsync("codesign", ["--verify", "--deep", "--strict", app]).ConfigureAwait(false);
        string stage = Path.Join(output, "package-" + Guid.NewGuid().ToString("N"));
        string temporaryImage = Path.Join(output, "Weft-" + Guid.NewGuid().ToString("N") + ".dmg");
        _ = Directory.CreateDirectory(stage);
        try
        {
            await RunAsync("ditto", [app, Path.Join(stage, "Weft.app")]).ConfigureAwait(false);
            _ = Directory.CreateSymbolicLink(Path.Join(stage, "Applications"), "/Applications");
            await RunAsync("hdiutil", ["create", "-volname", "Weft", "-srcfolder", stage, "-format", "UDZO", temporaryImage]).ConfigureAwait(false);
            await RunAsync("hdiutil", ["verify", temporaryImage]).ConfigureAwait(false);
            string destination = Path.Join(output, "Weft.dmg");
            File.Move(temporaryImage, destination, overwrite: true);
            await Console.Out.WriteLineAsync("Local installer: " + destination).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
            File.Delete(temporaryImage);
        }
        return 0;
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(executable + " failed with exit code " + process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
