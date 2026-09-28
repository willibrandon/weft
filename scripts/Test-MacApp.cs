#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weft.Scripts;

/// <summary>
/// Runs the AppKit smoke harness against a private server.
/// </summary>
internal static class TestMacApp
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync("Tests the built macOS app against an isolated server. --package checks disk-image installation, upgrade, and removal. --record-output records six seconds of Nushell redraws. --profile-memory captures vmmap and heap reports. --qualify runs workload, window, and crash checks. --accessibility checks the external accessibility tree and an already running VoiceOver. --graphics-app runs an existing rbirds executable and measures Kitty and Sixel animation. Usage: dotnet run --file scripts/Test-MacApp.cs [--arch arm64|x64] [--package] [--record-output|--profile-memory|--qualify|--accessibility|--graphics-app PATH]").ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Run the native AppKit test on macOS.");
        }

        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string? graphicsApp = null;
        int graphicsIndex = Array.IndexOf(args, "--graphics-app");
        if (graphicsIndex >= 0)
        {
            if (graphicsIndex + 1 >= args.Length || !File.Exists(args[graphicsIndex + 1]))
            {
                throw new ArgumentException("--graphics-app requires the path to a built rbirds executable.");
            }
            graphicsApp = Path.GetFullPath(args[graphicsIndex + 1]);
            args = [.. args.Where((_, index) => index != graphicsIndex && index != graphicsIndex + 1)];
        }
        bool package = args.Contains("--package", StringComparer.Ordinal);
        bool recording = args.Contains("--record-output", StringComparer.Ordinal);
        bool profiling = args.Contains("--profile-memory", StringComparer.Ordinal);
        bool qualificationOnly = args.Contains("--qualify", StringComparer.Ordinal);
        bool accessibility = args.Contains("--accessibility", StringComparer.Ordinal);
        args = [.. args.Where(argument => argument is not ("--package" or "--record-output" or "--profile-memory" or "--qualify" or "--accessibility"))];
        if (args is ["--arch", "arm64" or "x64"])
        {
            arch = args[1];
        }
        else if (args.Length != 0)
        {
            throw new ArgumentException("Usage: dotnet run --file scripts/Test-MacApp.cs [--arch arm64|x64] [--package] [--record-output|--profile-memory|--qualify|--accessibility|--graphics-app PATH]");
        }
        if (new[] { recording, profiling, qualificationOnly, accessibility, graphicsApp is not null }.Count(value => value) > 1)
        {
            throw new ArgumentException("Choose one of output recording, memory profiling, qualification, accessibility, or graphics application measurement.");
        }

        string root = Directory.GetCurrentDirectory();
        string output = Path.Join(root, "artifacts", "macos", "osx-" + arch);
        _ = Directory.CreateDirectory(output);
        File.Delete(Path.Join(output, "failed-server.log"));
        await File.WriteAllTextAsync(Path.Join(output, "qualification-environment.txt"),
            $"OS: {RuntimeInformation.OSDescription}\nHost architecture: {RuntimeInformation.OSArchitecture}\nDriver architecture: {RuntimeInformation.ProcessArchitecture}\nApp target: {arch}\n").ConfigureAwait(false);
        string contents = Path.Join(output, "Weft.app", "Contents");
        string cli = Path.Join(contents, "MacOS", "weft-server");
        string bundledCli = cli;
        string source = Path.Join(root, "native", "Weft.Desktop.Mac");
        string test = Path.Join(output, "Weft.Mac.Smoke");
        string temporary = Path.Join("/tmp", "weft-mac-" + Guid.NewGuid().ToString("N")[..10]);
        _ = Directory.CreateDirectory(temporary);
        string config = Path.Join(temporary, "config.json");
        await File.WriteAllTextAsync(config, """{"shell":"/bin/sh"}""").ConfigureAwait(false);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WEFT_SOCKET_DIR"] = Path.Join(temporary, "run"),
            ["WEFT_STATE_DIR"] = Path.Join(temporary, "state"),
            ["WEFT_CONFIG"] = config
        };
        try
        {
            if (package)
            {
                string mount = Path.Join(temporary, "volume");
                _ = Directory.CreateDirectory(mount);
                _ = await RunAsync("hdiutil", ["attach", Path.Join(output, "Weft.dmg"), "-readonly", "-nobrowse", "-mountpoint", mount], environment).ConfigureAwait(false);
                try
                {
                    string installed = Path.Join(temporary, "Applications", "Weft.app");
                    _ = await RunAsync("ditto", [Path.Join(mount, "Weft.app"), installed], environment).ConfigureAwait(false);
                    contents = Path.Join(installed, "Contents");
                    cli = Path.Join(contents, "MacOS", "weft-server");
                    _ = await RunAsync("codesign", ["--verify", "--deep", "--strict", installed], environment).ConfigureAwait(false);
                }
                finally
                {
                    _ = await RunAsync("hdiutil", ["detach", mount], environment).ConfigureAwait(false);
                }
            }
            string minimum = await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Print :LSMinimumSystemVersion", Path.Join(contents, "Info.plist")], environment).ConfigureAwait(false);
            List<string> arguments =
            [
                "swiftc", "-swift-version", "6", "-O", "-warnings-as-errors",
                "-enable-batch-mode", "-j", Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-module-cache-path", Path.Join(output, "module-cache"),
                "-target", (arch == "arm64" ? "arm64" : "x86_64") + "-apple-macosx" + minimum.Trim(),
                "-import-objc-header", Path.Join(source, "WeftNative.h"),
                "-framework", "AppKit", "-Xlinker", "-rpath", "-Xlinker", Path.Join(contents, "Frameworks"),
                Path.Join(contents, "Frameworks", "libWeft.Client.Native.dylib"), "-o", test
            ];
            string[] swiftSources =
            [
                .. Directory.EnumerateFiles(Path.Join(source, "Tests"), "*.swift")
                    .Concat(Directory.EnumerateFiles(Path.Join(source, "Sources"), "*.swift").Where(path => Path.GetFileName(path) != "main.swift"))
                    .Order(StringComparer.Ordinal)
            ];
            string objects = Path.Join(output, "test-objects");
            _ = Directory.CreateDirectory(objects);
            // Explicit object paths keep the batch compiler and linker on the same outputs.
            var outputMap = new JsonObject();
            foreach (string path in swiftSources)
            {
                outputMap[path] = new JsonObject { ["object"] = Path.Join(objects, Path.GetFileNameWithoutExtension(path) + ".o") };
            }
            string outputMapPath = Path.Join(objects, "output-map.json");
            await File.WriteAllTextAsync(outputMapPath, outputMap.ToJsonString()).ConfigureAwait(false);
            arguments.AddRange(["-output-file-map", outputMapPath]);
            arguments.AddRange(swiftSources);
            _ = await RunAsync("xcrun", arguments, environment).ConfigureAwait(false);
            if (accessibility)
            {
                await VerifyAccessibilityAsync(test, cli, environment).ConfigureAwait(false);
                return 0;
            }
            List<string> testArguments = [cli, Path.Join(output, "native-smoke.png")];
            if (graphicsApp is not null)
            {
                testArguments.AddRange(["--graphics-app", graphicsApp]);
            }
            if (qualificationOnly)
            {
                testArguments[1] = Path.Join(output, "qualification.json");
                testArguments.Add("--qualify");
            }
            if (recording)
            {
                testArguments.Add("--record-output");
            }
            if (profiling)
            {
                testArguments.Add("--measure");
                testArguments.Add("--profile-memory");
            }
            string result = await RunAsync(test, testArguments, environment).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(result.Trim()).ConfigureAwait(false);
            if (!recording && !profiling && !qualificationOnly && graphicsApp is null)
            {
                string sample = await RunAsync(test, [cli, Path.Join(output, "resources.png"), "--measure"], environment).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(sample.Trim()).ConfigureAwait(false);
                string qualification = await RunAsync(test, [cli, Path.Join(output, "qualification.json"), "--qualify"], environment).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(qualification.Trim()).ConfigureAwait(false);
                await VerifyCrashRecoveryAsync(test, cli, environment).ConfigureAwait(false);
            }
            if (qualificationOnly)
            {
                await VerifyCrashRecoveryAsync(test, cli, environment).ConfigureAwait(false);
            }
            if (package && !recording && !profiling && graphicsApp is null)
            {
                await VerifyInstallLifecycleAsync(output, temporary, test, bundledCli, environment).ConfigureAwait(false);
            }
        }
        catch
        {
            await PreserveFailureProfileAsync(cli, output, environment).ConfigureAwait(false);
            PreserveFailureLog(temporary, output);
            throw;
        }
        finally
        {
            if (File.Exists(Path.Join(temporary, "run", "weft.sock")))
            {
                _ = await RunAsync(bundledCli, ["shutdown"], environment).ConfigureAwait(false);
            }

            Directory.Delete(temporary, recursive: true);
        }

        return 0;
    }

    private static void PreserveFailureLog(string temporary, string output)
    {
        try
        {
            string log = Path.Join(temporary, "state", "server.log");
            if (File.Exists(log))
            {
                File.Copy(log, Path.Join(output, "failed-server.log"), overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not preserve the private server log: " + exception.Message);
        }
    }

    private static async Task PreserveFailureProfileAsync(string cli, string output, Dictionary<string, string> environment)
    {
        if (!File.Exists(Path.Join(environment["WEFT_SOCKET_DIR"], "weft.sock")))
        {
            return;
        }

        try
        {
            using var info = JsonDocument.Parse(await RunAsync(cli, ["info", "--json"], environment).ConfigureAwait(false));
            string pid = info.RootElement.GetProperty("pid").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = await RunAsync("sample", [pid, "3", "-file", Path.Join(output, "qualification-failure-server-sample.txt")], environment).ConfigureAwait(false);
            string map = await RunAsync("vmmap", ["-summary", pid], environment).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Join(output, "qualification-failure-server-memory.txt"), map).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or JsonException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Could not profile the private server after failure: " + exception.Message).ConfigureAwait(false);
        }
    }

    private static async Task VerifyAccessibilityAsync(string test, string cli, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(test) { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("unused");
        start.ArgumentList.Add("--accessibility-window");
        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }
        string voiceOver = await RunAsync("osascript", ["-e", "tell application \"System Events\" to exists process \"VoiceOver\""], environment).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the accessibility window.");
        try
        {
            if (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) != "Accessibility window ready")
            {
                throw new InvalidOperationException("The accessibility window did not become ready.");
            }
            string inspect = $$"""
                tell application "System Events"
                    tell (first application process whose unix id is {{process.Id}})
                        set terminal to text area 1 of window 1
                        set content to value of attribute "AXValue" of terminal
                        if content does not contain "Accessible terminal café 日本語" then error "Terminal text is missing from the accessibility tree"
                        set focused of terminal to true
                        return "External accessibility tree exposes terminal text and accepts focus."
                    end tell
                end tell
                """;
            await Console.Out.WriteLineAsync((await RunAsync("osascript", ["-e", inspect], environment).ConfigureAwait(false)).Trim()).ConfigureAwait(false);
            if (voiceOver.Trim() == "false")
            {
                await Console.Out.WriteLineAsync("VoiceOver is off; its interaction check was not run. Enable it and allow AppleScript control in VoiceOver Utility before repeating this optional check.").ConfigureAwait(false);
                return;
            }
            string read = """
                with timeout of 5 seconds
                    tell application "VoiceOver"
                        set spoken to text under cursor of keyboard cursor
                        if spoken does not contain "Accessible terminal" then error "VoiceOver did not reach the terminal text"
                        return spoken
                    end tell
                end timeout
                """;
            _ = await RunAsync("osascript", ["-e", inspect], environment).ConfigureAwait(false);
            _ = await RunAsync("osascript", ["-e", read], environment).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("VoiceOver reached the terminal's accessible text.").ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task VerifyCrashRecoveryAsync(string test, string cli, Dictionary<string, string> environment)
    {
        using var info = JsonDocument.Parse(await RunAsync(cli, ["info", "--json"], environment).ConfigureAwait(false));
        int server = info.RootElement.GetProperty("pid").GetInt32();
        Dictionary<string, int> processes = await ReadProcessesAsync(cli, environment).ConfigureAwait(false);
        var start = new ProcessStartInfo(test) { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("unused");
        start.ArgumentList.Add("--crash-client");
        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the crash client.");
        try
        {
            string? ready = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (ready != "Crash client attached")
            {
                throw new InvalidOperationException("The crash client did not attach: " + ready);
            }
            process.Kill();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        _ = await RunAsync(test, [cli, "unused", "--reattach-only"], environment).ConfigureAwait(false);
        await VerifyRunningAsync(cli, environment, server, processes).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Forced client termination preserved the server and every shell; a fresh client reattached.").ConfigureAwait(false);
    }

    private static async Task VerifyInstallLifecycleAsync(string output, string temporary, string test, string cli, Dictionary<string, string> environment)
    {
        using var info = JsonDocument.Parse(await RunAsync(cli, ["info", "--json"], environment).ConfigureAwait(false));
        int pid = info.RootElement.GetProperty("pid").GetInt32();
        Dictionary<string, int> processes = await ReadProcessesAsync(cli, environment).ConfigureAwait(false);
        string config = await File.ReadAllTextAsync(environment["WEFT_CONFIG"]).ConfigureAwait(false);
        string installed = Path.Join(temporary, "Applications", "Weft.app");
        string replacement = Path.Join(temporary, "Applications", "Replacement.app");
        string previous = Path.Join(temporary, "Applications", "Previous.app");
        _ = await RunAsync("ditto", [Path.Join(output, "Weft.app"), replacement], environment).ConfigureAwait(false);
        Directory.Move(installed, previous);
        Directory.Move(replacement, installed);
        Directory.Delete(previous, recursive: true);
        await VerifyRunningAsync(cli, environment, pid, processes).ConfigureAwait(false);
        Directory.Delete(installed, recursive: true);
        await VerifyRunningAsync(cli, environment, pid, processes).ConfigureAwait(false);
        if (config != await File.ReadAllTextAsync(environment["WEFT_CONFIG"]).ConfigureAwait(false)
            || !Directory.Exists(environment["WEFT_STATE_DIR"]))
        {
            throw new InvalidOperationException("Removing the app changed user configuration or session data.");
        }
        _ = await RunAsync("ditto", [Path.Join(output, "Weft.app"), installed], environment).ConfigureAwait(false);
        _ = await RunAsync("codesign", ["--verify", "--deep", "--strict", installed], environment).ConfigureAwait(false);
        string restoredCli = Path.Join(installed, "Contents", "MacOS", "weft-server");
        _ = await RunAsync(test, [restoredCli, Path.Join(output, "reinstalled-smoke.png"), "--reattach-only"], environment).ConfigureAwait(false);
        await VerifyRunningAsync(restoredCli, environment, pid, processes).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Upgrade, removal, and reinstall preserved the server, shell processes, configuration, and session data.").ConfigureAwait(false);
    }

    private static async Task VerifyRunningAsync(string cli, IReadOnlyDictionary<string, string> environment, int pid, Dictionary<string, int> expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            using var info = JsonDocument.Parse(await RunAsync(cli, ["info", "--json"], environment).ConfigureAwait(false));
            if (info.RootElement.GetProperty("pid").GetInt32() != pid)
            {
                throw new InvalidOperationException("App installation replaced the running server.");
            }
            if (info.RootElement.GetProperty("clients").GetInt32() == 0)
            {
                break;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("Closing the native app retained a client attachment.");
            }
            await Task.Delay(25).ConfigureAwait(false);
        }
        Dictionary<string, int> actual = await ReadProcessesAsync(cli, environment).ConfigureAwait(false);
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out int value) || value != pair.Value))
        {
            throw new InvalidOperationException("App installation changed running shell processes.");
        }
    }

    private static async Task<Dictionary<string, int>> ReadProcessesAsync(string cli, IReadOnlyDictionary<string, string> environment)
    {
        using var blocks = JsonDocument.Parse(await RunAsync(cli, ["blocks", "--json"], environment).ConfigureAwait(false));
        return blocks.RootElement.GetProperty("blocks").EnumerateArray()
            .ToDictionary(block => block.GetProperty("id").GetString()!, block => block.GetProperty("pid").GetInt32(), StringComparer.Ordinal);
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in environment)
        {
            start.Environment[key] = value;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        try
        {
            string output;
            if (start.ArgumentList.Contains("--profile-memory"))
            {
                var captured = new StringBuilder();
                while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is string line)
                {
                    _ = captured.AppendLine(line);
                    if (line.StartsWith("Memory profile ready:", StringComparison.Ordinal))
                    {
                        string pid = process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string directory = Path.GetDirectoryName(executable)!;
                        string map = await RunAsync("vmmap", ["-summary", pid], environment).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Join(directory, "memory-vmmap.txt"), map, timeout.Token).ConfigureAwait(false);
                        string heap = await RunAsync("heap", [pid], environment).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Join(directory, "memory-heap.txt"), heap, timeout.Token).ConfigureAwait(false);
                    }
                }
                output = captured.ToString();
            }
            else
            {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : throw new InvalidOperationException(executable + " failed: " + output);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
