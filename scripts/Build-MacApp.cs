#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Weft.Scripts;

/// <summary>
/// Builds the native macOS app bundle without launching it.
/// </summary>
internal static class BuildMacApp
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync("Builds a self-contained Weft.app with AppKit and the Native AOT client. --prepare-analysis prepares dependencies and a Swift compiler response file for CodeQL. Usage: dotnet run --file scripts/Build-MacApp.cs [--arch arm64|x64] [--prepare-analysis]").ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsMacOS())
        {
            await Console.Error.WriteLineAsync("Build the macOS app on macOS.").ConfigureAwait(false);
            return 1;
        }

        bool prepareAnalysis = args.Contains("--prepare-analysis", StringComparer.Ordinal);
        args = [.. args.Where(argument => argument != "--prepare-analysis")];
        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (args is ["--arch", "arm64" or "x64"])
        {
            arch = args[1];
        }
        else if (args.Length != 0)
        {
            await Console.Error.WriteLineAsync("Usage: dotnet run --file scripts/Build-MacApp.cs [--arch arm64|x64]").ConfigureAwait(false);
            return 1;
        }

        string root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Join(root, "Weft.slnx")))
        {
            await Console.Error.WriteLineAsync("Run this command from the Weft repository root.").ConfigureAwait(false);
            return 1;
        }

        string rid = "osx-" + arch;
        string output = Path.Join(root, "artifacts", "macos", rid);
        string app = Path.Join(output, "Weft.app");
        string contents = Path.Join(app, "Contents");
        string executables = Path.Join(contents, "MacOS");
        string libraries = Path.Join(contents, "Frameworks");
        string source = Path.Join(root, "native", "Weft.Desktop.Mac");
        string clientPublish = Path.Join(output, "client");
        string serverPublish = Path.Join(output, "server");
        await RunAsync("dotnet", ["publish", "src/Weft.Client.Native/Weft.Client.Native.csproj",
            "-c", "Release", "-r", rid, "-o", clientPublish]).ConfigureAwait(false);
        await RunAsync("dotnet", ["publish", "src/Weft.App/Weft.App.csproj",
            "-c", "Release", "-r", rid, "-o", serverPublish]).ConfigureAwait(false);

        // Only replace this script's derived output, never a running installed app.
        if (Directory.Exists(app))
        {
            Directory.Delete(app, recursive: true);
        }

        _ = Directory.CreateDirectory(executables);
        _ = Directory.CreateDirectory(libraries);
        File.Copy(Path.Join(source, "Info.plist"), Path.Join(contents, "Info.plist"));
        string appVersion = (await CaptureAsync("dotnet", ["msbuild", "src/Weft.App/Weft.App.csproj", "-getProperty:VersionPrefix"]).ConfigureAwait(false)).Trim();
        if (!Version.TryParse(appVersion, out _))
        {
            throw new InvalidDataException("The application VersionPrefix must be a numeric bundle version.");
        }
        foreach (string key in new[] { "CFBundleShortVersionString", "CFBundleVersion" })
        {
            await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Add :" + key + " string " + appVersion, Path.Join(contents, "Info.plist")]).ConfigureAwait(false);
        }
        await BundleFontsAsync(root, Path.Join(contents, "Resources", "Fonts")).ConfigureAwait(false);
        foreach (string path in Directory.EnumerateFiles(serverPublish).Where(path => Path.GetFileName(path) == "weft" || Path.GetExtension(path) == ".dylib"))
        {
            string name = Path.GetFileName(path) == "weft" ? "weft-server" : Path.GetFileName(path);
            File.Copy(path, Path.Join(executables, name), overwrite: true);
        }

        foreach (string path in Directory.EnumerateFiles(clientPublish, "*.dylib"))
        {
            File.Copy(path, Path.Join(libraries, Path.GetFileName(path)), overwrite: true);
        }

        string library = Path.Join(libraries, "libWeft.Client.Native.dylib");
        await RunAsync("install_name_tool", ["-id", "@rpath/libWeft.Client.Native.dylib", library]).ConfigureAwait(false);
        // Match the strictest minimum declared by the bundled native binaries.
        Version minimum = new(0, 0);
        foreach (string binary in Directory.EnumerateFiles(libraries, "*.dylib").Append(Path.Join(executables, "weft-server")))
        {
            string metadata = await CaptureAsync("xcrun", ["vtool", "-show-build", binary]).ConfigureAwait(false);
            foreach (string line in metadata.Split('\n'))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts is ["minos", string value] && Version.TryParse(value, out Version? version) && version > minimum)
                {
                    minimum = version;
                }
            }
        }

        if (minimum.Major == 0)
        {
            throw new InvalidOperationException("Could not determine the bundled libraries' macOS deployment target.");
        }

        await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Add :LSMinimumSystemVersion string " + minimum, Path.Join(contents, "Info.plist")]).ConfigureAwait(false);
        List<string> swiftArguments =
        [
            "swiftc", "-swift-version", "6", "-O", "-warnings-as-errors",
            "-module-cache-path", Path.Join(output, "module-cache"),
            "-target", (arch == "arm64" ? "arm64" : "x86_64") + "-apple-macosx" + minimum,
            "-import-objc-header", Path.Join(source, "WeftNative.h"),
            "-framework", "AppKit", "-Xlinker", "-rpath", "-Xlinker", "@executable_path/../Frameworks",
            library, "-o", Path.Join(executables, "Weft")
        ];
        swiftArguments.AddRange(Directory.EnumerateFiles(Path.Join(source, "Sources"), "*.swift").Order(StringComparer.Ordinal));
        if (prepareAnalysis)
        {
            // CodeQL traces Swift compilation directly; dependency preparation runs before tracing begins.
            string responseFile = Path.Join(root, "artifacts", "macos", "swift-analysis.rsp");
            await File.WriteAllLinesAsync(responseFile, swiftArguments.Skip(1).Select(argument =>
                "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("Prepared Swift analysis: " + responseFile).ConfigureAwait(false);
            return 0;
        }
        byte[] serverHash = await HashAsync(Path.Join(executables, "weft-server")).ConfigureAwait(false);
        await RunAsync("xcrun", swiftArguments).ConfigureAwait(false);
        byte[] serverAfter = await HashAsync(Path.Join(executables, "weft-server")).ConfigureAwait(false);
        byte[] appHash = await HashAsync(Path.Join(executables, "Weft")).ConfigureAwait(false);
        if (!serverHash.AsSpan().SequenceEqual(serverAfter) || appHash.AsSpan().SequenceEqual(serverAfter))
        {
            Directory.Delete(app, recursive: true);
            throw new InvalidOperationException("The GUI build replaced the bundled server. The invalid app bundle was removed.");
        }

        foreach (string binary in Directory.EnumerateFiles(libraries, "*.dylib")
            .Concat(Directory.EnumerateFiles(executables, "*.dylib")).Append(Path.Join(executables, "weft-server")))
        {
            await RunAsync("codesign", ["--force", "--sign", "-", binary]).ConfigureAwait(false);
        }

        string iconSet = Path.Join(output, "Weft.iconset");
        await RunAsync(Path.Join(executables, "Weft"), ["--render-icon", iconSet]).ConfigureAwait(false);
        await RunAsync("iconutil", ["-c", "icns", iconSet, "-o", Path.Join(contents, "Resources", "Weft.icns")]).ConfigureAwait(false);
        await RunAsync("codesign", ["--force", "--sign", "-", app]).ConfigureAwait(false);
        await RunAsync("codesign", ["--verify", "--deep", "--strict", app]).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Built " + app).ConfigureAwait(false);
        return 0;
    }

    private static async Task BundleFontsAsync(string root, string destination)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Weft-Build");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/microsoft/cascadia-code/releases/latest");
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            // Authenticate only the API lookup, never release asset or license downloads.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        string releaseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var release = JsonDocument.Parse(releaseText);
        JsonElement asset = release.RootElement.GetProperty("assets").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString()!.EndsWith(".zip", StringComparison.Ordinal));
        string name = Path.GetFileName(asset.GetProperty("name").GetString()!);
        string cache = Path.Join(root, "artifacts", "fonts");
        _ = Directory.CreateDirectory(cache);
        string archive = Path.Join(cache, name);
        if (!File.Exists(archive) || new FileInfo(archive).Length != asset.GetProperty("size").GetInt64())
        {
            string temporary = archive + ".download";
            try
            {
                using Stream download = await http.GetStreamAsync(new Uri(asset.GetProperty("browser_download_url").GetString()!)).ConfigureAwait(false);
                using (FileStream output = File.Create(temporary))
                {
                    await download.CopyToAsync(output).ConfigureAwait(false);
                }

                File.Move(temporary, archive, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        _ = Directory.CreateDirectory(destination);
        using ZipArchive zip = await ZipFile.OpenReadAsync(archive).ConfigureAwait(false);
        foreach (string font in new[] { "CascadiaMonoNF.ttf", "CascadiaMonoNFItalic.ttf" })
        {
            ZipArchiveEntry entry = zip.GetEntry("ttf/" + font) ?? throw new InvalidDataException("The font release is missing " + font);
            await entry.ExtractToFileAsync(Path.Join(destination, font), overwrite: true).ConfigureAwait(false);
        }

        string tag = release.RootElement.GetProperty("tag_name").GetString()!;
        string license = await http.GetStringAsync(new Uri("https://raw.githubusercontent.com/microsoft/cascadia-code/" + Uri.EscapeDataString(tag) + "/LICENSE")).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Join(destination, "LICENSE.txt"), license).ConfigureAwait(false);
        byte[] hash = await HashAsync(archive).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Join(destination, "source.txt"),
            "https://github.com/microsoft/cascadia-code/releases/tag/" + tag + "\n" + name + "\nSHA256: " + Convert.ToHexStringLower(hash) + "\n").ConfigureAwait(false);
    }

    private static async Task<byte[]> HashAsync(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream).ConfigureAwait(false);
    }

    private static async Task<string> CaptureAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode == 0 ? output : throw new InvalidOperationException(executable + " could not inspect the native binary.");
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(executable + " failed with exit code " + process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
