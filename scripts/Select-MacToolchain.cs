#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;

namespace Weft.Scripts;

/// <summary>
/// Selects the newest installed release of Xcode for subsequent hosted-runner steps.
/// </summary>
internal static class SelectMacToolchain
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync("Selects the newest numbered Xcode release on a hosted Mac runner for subsequent steps through GITHUB_ENV. System settings are unchanged.").ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Xcode selection requires macOS.");
        }

        // Hosted runners can default to an older Xcode despite containing current releases.
        // Select the newest installed numbered release without changing system preferences.
        string? selected = null;
        Version? newest = null;
        foreach (string bundle in Directory.EnumerateDirectories("/Applications", "Xcode_*.app"))
        {
            string name = Path.GetFileNameWithoutExtension(bundle)["Xcode_".Length..];
            string developer = Path.Join(bundle, "Contents", "Developer");
            if (Version.TryParse(name, out Version? version) && (newest is null || version > newest)
                && Directory.Exists(developer))
            {
                newest = version;
                selected = developer;
            }
        }

        if (selected is null)
        {
            throw new InvalidOperationException("No numbered Xcode release was found on the Mac runner.");
        }

        string? environmentFile = Environment.GetEnvironmentVariable("GITHUB_ENV");
        if (!string.IsNullOrEmpty(environmentFile))
        {
            await File.AppendAllTextAsync(environmentFile, "DEVELOPER_DIR=" + selected + Environment.NewLine).ConfigureAwait(false);
        }
        await Console.Out.WriteLineAsync("Using " + selected).ConfigureAwait(false);
        var start = new ProcessStartInfo("/usr/bin/xcrun") { UseShellExecute = false };
        start.ArgumentList.Add("swiftc");
        start.ArgumentList.Add("--version");
        start.Environment["DEVELOPER_DIR"] = selected;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not report the selected Swift compiler.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}
