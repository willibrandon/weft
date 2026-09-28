#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0-windows10.0.26100.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true
#:property AllowUnsafeBlocks=true
#:property WeftAllowAlternateTargetFramework=true

using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Windows.Management.Deployment;

namespace Weft.Scripts;

/// <summary>
/// Tests the built Windows app against isolated servers, and with <c>--package</c> its installed lifecycle.
/// </summary>
/// <remarks>
/// The desktop test project drives real windows against the bundled Native AOT server. The published app is then
/// started outside a shell with a minimal PATH, closed, and reopened while its server and shell keep running.
/// Package tests install the unsigned development MSIX, which Windows allows only for an administrator, then
/// upgrade, remove, and reinstall it while checking that the server, shells, and user files are untouched.
/// </remarks>
internal static partial class TestWindowsApp
{
    private const int AppModelErrorNoPackage = 15700;
    private const string PackageName = "Weft.Development";
    private const int QueryLimitedInformation = 0x1000;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync(
                "Tests the built Windows app against isolated servers. --package installs, upgrades, removes, and "
                + "reinstalls the development MSIX, which needs an administrator. Opens Weft windows while it runs. "
                + "Usage: dotnet run --file scripts/Test-WindowsApp.cs [--arch x64|arm64] [--package]")
                .ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("Test the Windows app on Windows.").ConfigureAwait(false);
            return 1;
        }

        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        bool package = args.Contains("--package", StringComparer.Ordinal);
        string[] rest = [.. args.Where(argument => argument != "--package")];
        if (rest is ["--arch", "x64" or "arm64"])
        {
            arch = rest[1];
        }
        else if (rest.Length != 0)
        {
            await Console.Error.WriteLineAsync("Usage: dotnet run --file scripts/Test-WindowsApp.cs "
                + "[--arch x64|arm64] [--package]").ConfigureAwait(false);
            return 1;
        }

        string root = Directory.GetCurrentDirectory();
        string output = Path.Join(root, "artifacts", "windows", "win-" + arch);
        string app = Path.Join(output, "Weft");
        string server = Path.Join(app, "weft-server.exe");
        if (!File.Exists(Path.Join(app, "Weft.exe")))
        {
            await Console.Error.WriteLineAsync("Build the app first: dotnet run --file scripts/Build-WindowsApp.cs")
                .ConfigureAwait(false);
            return 1;
        }

        if (package && !IsAdministrator())
        {
            // Unsigned packages carry no publisher proof, so Windows installs those with executables only for an
            // administrator. Signing would avoid this but needs a certificate the development build does not use.
            await Console.Error.WriteLineAsync("Installing the unsigned development MSIX needs an administrator. "
                + "Run this command from an elevated terminal, or run it without --package.").ConfigureAwait(false);
            return 1;
        }

        await File.WriteAllTextAsync(Path.Join(output, "qualification-environment.txt"),
            "OS: " + RuntimeInformation.OSDescription + "\nHost architecture: " + RuntimeInformation.OSArchitecture
            + "\nApp target: " + arch + "\n").ConfigureAwait(false);

        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            AllowWindowCapture();
        }

        // The window tests run against the bundled server the app ships with, and their results are kept.
        await RunVisibleAsync("dotnet", ["test", "--project", "tests/Weft.Desktop.Windows.Tests", "--report-trx"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["WEFT_DESKTOP_SERVER"] = server })
            .ConfigureAwait(false);

        await IsolateAsync(output, server, environment =>
            VerifyPublishedAppAsync(Path.Join(app, "Weft.exe"), server, environment)).ConfigureAwait(false);
        if (package)
        {
            // A fresh server location makes the installed app start its own server rather than reuse one.
            string copies = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "weft",
                "server");
            try
            {
                await IsolateAsync(output, server, environment =>
                    VerifyPackageLifecycleAsync(root, arch, server, environment)).ConfigureAwait(false);
            }
            finally
            {
                foreach (string copy in Directory.Exists(copies)
                    ? Directory.EnumerateDirectories(copies, PackageName + "_*")
                    : [])
                {
                    DeleteWhenReleased(copy);
                }
            }
        }

        return 0;
    }

    private static async Task IsolateAsync(string output, string server,
        Func<Dictionary<string, string>, Task> test)
    {
        string temporary = Path.Join(Path.GetTempPath(), "wa-" + Guid.NewGuid().ToString("N")[..8]);
        Dictionary<string, string> environment = await PrepareAsync(temporary).ConfigureAwait(false);
        try
        {
            await test(environment).ConfigureAwait(false);
        }
        catch
        {
            PreserveFailureLog(temporary, output);
            throw;
        }
        finally
        {
            if (File.Exists(Path.Join(temporary, "run", "weft.sock")))
            {
                _ = await RunAsync(server, ["shutdown"], environment).ConfigureAwait(false);
            }

            DeleteWhenReleased(temporary);
        }
    }

    private static async Task<Dictionary<string, string>> PrepareAsync(string temporary)
    {
        _ = Directory.CreateDirectory(temporary);
        string config = Path.Join(temporary, "config.json");
        string shell = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        await File.WriteAllTextAsync(config, "{\"shell\": \"" + JsonEncodedText.Encode(shell) + "\"}")
            .ConfigureAwait(false);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WEFT_SOCKET_DIR"] = Path.Join(temporary, "run"),
            ["WEFT_STATE_DIR"] = Path.Join(temporary, "state"),
            ["WEFT_CONFIG"] = config,
            ["WEFT_DESKTOP_PREFERENCES"] = Path.Join(temporary, "desktop.json"),
            // The app must not depend on a developer's PATH to find its server or shell.
            ["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
    }

    private static async Task VerifyPublishedAppAsync(string executable, string server,
        Dictionary<string, string> environment)
    {
        // The app must never serve as the server bootstrap, whatever arguments it is given.
        var rejected = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true };
        rejected.ArgumentList.Add("server");
        rejected.ArgumentList.Add("--detached");
        using (Process process = Process.Start(rejected)
            ?? throw new InvalidOperationException("Could not start the app."))
        {
            _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 64)
            {
                throw new InvalidOperationException("The app accepted server arguments.");
            }
        }


        (int pid, Dictionary<string, int> shells) = await LaunchAndCloseAsync(executable, server, environment)
            .ConfigureAwait(false);
        (int reopened, Dictionary<string, int> kept) = await LaunchAndCloseAsync(executable, server, environment)
            .ConfigureAwait(false);
        if (reopened != pid || !SameProcesses(shells, kept))
        {
            throw new InvalidOperationException("Reopening the app replaced the server or its shells.");
        }

        await Console.Out.WriteLineAsync("The published app started its server, and closing and reopening it kept the "
            + "server and shell running.").ConfigureAwait(false);
    }

    private static async Task VerifyPackageLifecycleAsync(string root, string arch, string server,
        Dictionary<string, string> environment)
    {
        string version = (await RunAsync("dotnet", ["msbuild", "src/Weft.App/Weft.App.csproj",
            "-getProperty:VersionPrefix"]).ConfigureAwait(false)).Trim();
        var current = new Version(version);
        var next = new Version(current.Major, current.Minor, current.Build + 1);
        string first = Path.Join(root, "artifacts", "windows", "Weft-" + current.ToString(3) + "-" + arch + ".msix");
        string upgrade = Path.Join(root, "artifacts", "windows", "Weft-" + next.ToString(3) + "-" + arch + ".msix");
        if (!File.Exists(first))
        {
            _ = await RunAsync("dotnet", ["run", "--file", "scripts/Package-WindowsApp.cs", "--", "--arch", arch])
                .ConfigureAwait(false);
        }

        _ = await RunAsync("dotnet", ["run", "--file", "scripts/Package-WindowsApp.cs", "--", "--arch", arch,
            "--version", next.ToString(3)]).ConfigureAwait(false);

        var manager = new PackageManager();
        string alias = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft",
            "WindowsApps", "weft-desktop.exe");
        await RemoveInstalledAsync(manager).ConfigureAwait(false);
        await InstallAsync(manager, first).ConfigureAwait(false);
        try
        {
            (int pid, Dictionary<string, int> shells) = await LaunchAndCloseAsync(alias, server, environment)
                .ConfigureAwait(false);
            await VerifyOutsidePackageAsync(pid, shells).ConfigureAwait(false);
            string config = await File.ReadAllTextAsync(environment["WEFT_CONFIG"]).ConfigureAwait(false);

            await InstallAsync(manager, upgrade).ConfigureAwait(false);
            await VerifyRunningAsync(server, environment, pid, shells).ConfigureAwait(false);
            await RemoveInstalledAsync(manager).ConfigureAwait(false);
            await VerifyRunningAsync(server, environment, pid, shells).ConfigureAwait(false);
            if (config != await File.ReadAllTextAsync(environment["WEFT_CONFIG"]).ConfigureAwait(false)
                || !Directory.Exists(environment["WEFT_STATE_DIR"]))
            {
                throw new InvalidOperationException("Removing the app changed user configuration or session data.");
            }

            await InstallAsync(manager, upgrade).ConfigureAwait(false);
            (int reattached, Dictionary<string, int> kept) = await LaunchAndCloseAsync(alias, server, environment)
                .ConfigureAwait(false);
            if (reattached != pid || !SameProcesses(shells, kept))
            {
                throw new InvalidOperationException("The reinstalled app replaced the server or its shells.");
            }
        }
        finally
        {
            await RemoveInstalledAsync(manager).ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync("Installing, upgrading, removing, and reinstalling the package kept the "
            + "server, its shells, the configuration, and session data.").ConfigureAwait(false);
    }

    private static async Task<(int Server, Dictionary<string, int> Shells)> LaunchAndCloseAsync(string executable,
        string server, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }

        using Process app = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        Dictionary<string, int> shells = [];
        int pid = 0;
        await UntilAsync(async () =>
        {
            if (!File.Exists(Path.Join(environment["WEFT_SOCKET_DIR"], "weft.sock")))
            {
                return false;
            }

            using var info = JsonDocument.Parse(await RunAsync(server, ["info", "--json"], environment)
                .ConfigureAwait(false));
            pid = info.RootElement.GetProperty("pid").GetInt32();
            shells = await ReadShellsAsync(server, environment).ConfigureAwait(false);
            return shells.Count != 0 && info.RootElement.GetProperty("clients").GetInt32() == 1;
        }, "the app to attach to its server").ConfigureAwait(false);

        app.Refresh();
        if (!app.CloseMainWindow())
        {
            throw new InvalidOperationException("The app has no window to close.");
        }

        using (var closing = new CancellationTokenSource(s_timeout))
        {
            await app.WaitForExitAsync(closing.Token).ConfigureAwait(false);
        }

        await VerifyRunningAsync(server, environment, pid, shells).ConfigureAwait(false);
        return (pid, shells);
    }

    private static async Task VerifyRunningAsync(string server, Dictionary<string, string> environment, int pid,
        Dictionary<string, int> shells)
    {
        await UntilAsync(async () =>
        {
            using var info = JsonDocument.Parse(await RunAsync(server, ["info", "--json"], environment)
                .ConfigureAwait(false));
            return info.RootElement.GetProperty("pid").GetInt32() != pid
                ? throw new InvalidOperationException("The server was replaced.")
                : info.RootElement.GetProperty("clients").GetInt32() == 0;
        }, "the closed app to release its attachment").ConfigureAwait(false);
        if (!SameProcesses(shells, await ReadShellsAsync(server, environment).ConfigureAwait(false)))
        {
            throw new InvalidOperationException("The shells running in the session changed.");
        }
    }

    private static async Task VerifyOutsidePackageAsync(int pid, Dictionary<string, int> shells)
    {
        string image = Process.GetProcessById(pid).MainModule?.FileName ?? string.Empty;
        string copies = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "weft",
            "server", PackageName + "_");
        if (!image.StartsWith(copies, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The installed app did not start its server from a copy outside the "
                + "package, which an upgrade would replace: " + image);
        }

        foreach (int process in shells.Values.Prepend(pid))
        {
            if (HasPackage(process))
            {
                throw new InvalidOperationException("Process " + process.ToString(CultureInfo.InvariantCulture)
                    + " runs inside the package, so removing the app would end it.");
            }
        }

        await Console.Out.WriteLineAsync("The installed app started its server from " + image
            + " outside the package, with no package identity.").ConfigureAwait(false);
    }

    private static bool HasPackage(int pid)
    {
        nint process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == 0)
        {
            throw new InvalidOperationException("Could not open process " + pid.ToString(CultureInfo.InvariantCulture));
        }

        try
        {
            int length = 0;
            return GetPackageFullName(process, ref length, 0) != AppModelErrorNoPackage;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    private static async Task InstallAsync(PackageManager manager, string package)
    {
        // Nothing is force-stopped: a package process still running would fail the install and reveal a leak.
        var options = new AddPackageOptions { AllowUnsigned = true };
        DeploymentResult result = await manager.AddPackageByUriAsync(new Uri(package), options).AsTask()
            .ConfigureAwait(false);
        if (!result.IsRegistered)
        {
            throw new InvalidOperationException("Installing " + package + " failed: " + result.ErrorText);
        }
    }

    private static async Task RemoveInstalledAsync(PackageManager manager)
    {
        foreach (global::Windows.ApplicationModel.Package installed in manager.FindPackagesForUser(string.Empty)
            .Where(item => item.Id.Name == PackageName).ToList())
        {
            DeploymentResult result = await manager.RemovePackageAsync(installed.Id.FullName).AsTask()
                .ConfigureAwait(false);
            if (result.ExtendedErrorCode is not null)
            {
                throw new InvalidOperationException("Removing the package failed: " + result.ErrorText);
            }
        }
    }

    private static async Task<Dictionary<string, int>> ReadShellsAsync(string server,
        IReadOnlyDictionary<string, string> environment)
    {
        using var blocks = JsonDocument.Parse(await RunAsync(server, ["blocks", "--json"], environment)
            .ConfigureAwait(false));
        return blocks.RootElement.GetProperty("blocks").EnumerateArray()
            .Where(block => block.TryGetProperty("pid", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number)
            .ToDictionary(block => block.GetProperty("id").GetString()!, block => block.GetProperty("pid").GetInt32(),
                StringComparer.Ordinal);
    }

    private static bool SameProcesses(Dictionary<string, int> expected, Dictionary<string, int> actual)
    {
        return expected.Count == actual.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out int value) && value == pair.Value);
    }

    /// <summary>
    /// Lets desktop apps capture windows without asking, as a Windows 11 desktop allows by default.
    /// </summary>
    /// <remarks>
    /// The window tests read their own pixels through Windows Graphics Capture. A fresh Windows Server runner has
    /// not granted that consent and has no one to grant it, so capture is denied. Only CI runs change it; a local
    /// run keeps the user's own privacy choice.
    /// </remarks>
    private static void AllowWindowCapture()
    {
        const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        string[] capabilities = ["graphicsCaptureProgrammatic", "graphicsCaptureWithoutBorder"];
        foreach (string capability in capabilities)
        {
            using (RegistryKey machine = Registry.LocalMachine.CreateSubKey(ConsentStore + capability))
            {
                machine.SetValue("Value", "Allow");
            }

            using (RegistryKey user = Registry.CurrentUser.CreateSubKey(ConsentStore + capability))
            {
                user.SetValue("Value", "Allow");
            }

            using RegistryKey unpackaged = Registry.CurrentUser.CreateSubKey(ConsentStore + capability + @"\NonPackaged");
            unpackaged.SetValue("Value", "Allow");
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, string description)
    {
        long deadline = Environment.TickCount64 + (long)s_timeout.TotalMilliseconds;
        while (!await condition().ConfigureAwait(false))
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Timed out waiting for " + description + ".");
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private static void PreserveFailureLog(string temporary, string output)
    {
        string log = Path.Join(temporary, "state", "server.log");
        if (File.Exists(log))
        {
            File.Copy(log, Path.Join(output, "failed-server.log"), overwrite: true);
        }
    }

    private static void DeleteWhenReleased(string directory)
    {
        for (int attempt = 0; attempt < 50 && Directory.Exists(directory); attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static async Task RunVisibleAsync(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(executable + " " + string.Join(' ', arguments)
                + " failed with exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode == 0 ? output : throw new InvalidOperationException(executable + " "
            + string.Join(' ', arguments) + " failed with exit code "
            + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetPackageFullName(nint process, ref int length, nint name);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
