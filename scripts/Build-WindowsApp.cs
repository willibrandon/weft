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
/// Builds the native Windows app layout without installing or launching it.
/// </summary>
internal static class BuildWindowsApp
{
    private static readonly string[] s_serverFiles = ["hex1bpty.exe", "conpty.dll", "OpenConsole.exe"];

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync(
                "Builds artifacts/windows/win-<arch>/Weft with the Reactor app, the Native AOT server, "
                + "fonts, and icons. "
                + "Usage: dotnet run --file scripts/Build-WindowsApp.cs [--arch x64|arm64]").ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("Build the Windows app on Windows.").ConfigureAwait(false);
            return 1;
        }

        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (args is ["--arch", "arm64" or "x64"])
        {
            arch = args[1];
        }
        else if (args.Length != 0)
        {
            await Console.Error.WriteLineAsync(
                "Usage: dotnet run --file scripts/Build-WindowsApp.cs [--arch x64|arm64]").ConfigureAwait(false);
            return 1;
        }

        string root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Join(root, "Weft.slnx")))
        {
            await Console.Error.WriteLineAsync("Run this command from the Weft repository root.").ConfigureAwait(false);
            return 1;
        }

        string rid = "win-" + arch;
        string output = Path.Join(root, "artifacts", "windows", rid);
        string app = Path.Join(output, "Weft");
        string appPublish = Path.Join(output, "app");
        string serverPublish = Path.Join(output, "server");
        await RunAsync("dotnet",
            ["publish", "src/Weft.App/Weft.App.csproj", "-c", "Release", "-r", rid, "-o", serverPublish])
            .ConfigureAwait(false);
        await RunAsync("dotnet",
            ["publish", "src/Weft.Desktop.Windows/Weft.Desktop.Windows.csproj",
                "-c", "Release", "-r", rid, "-o", appPublish])
            .ConfigureAwait(false);

        // Only replace this script's derived output, never an installed app.
        if (Directory.Exists(app))
        {
            Directory.Delete(app, recursive: true);
        }

        CopyDirectory(appPublish, app, path => Path.GetExtension(path) is not (".pdb" or ".xml"));
        string server = Path.Join(app, "weft-server.exe");
        File.Copy(Path.Join(serverPublish, "weft.exe"), server);
        foreach (string file in s_serverFiles)
        {
            File.Copy(Path.Join(serverPublish, file), Path.Join(app, file), overwrite: true);
        }

        // The app and server names differ even on a case-insensitive file system, and the app must not
        // replace the server.
        byte[] published = await HashAsync(Path.Join(serverPublish, "weft.exe")).ConfigureAwait(false);
        byte[] bundled = await HashAsync(server).ConfigureAwait(false);
        byte[] gui = await HashAsync(Path.Join(app, "Weft.exe")).ConfigureAwait(false);
        if (!published.AsSpan().SequenceEqual(bundled) || gui.AsSpan().SequenceEqual(bundled))
        {
            Directory.Delete(app, recursive: true);
            throw new InvalidOperationException(
                "The app layout does not contain a distinct, unmodified server. The invalid layout was removed.");
        }

        await BundleFontsAsync(root, Path.Join(app, "Fonts")).ConfigureAwait(false);
        string executable = Path.Join(app, "Weft.exe");
        if (RuntimeInformation.OSArchitecture == Architecture.X64 && arch == "arm64")
        {
            throw new InvalidOperationException(
                "Build the ARM64 app on ARM64 Windows, which can also build and run the x64 app.");
        }

        await RunAsync(executable, ["--render-icon", Path.Join(app, "Assets")]).ConfigureAwait(false);
        int rejected = await ExitCodeAsync(executable, ["server", "--detached"]).ConfigureAwait(false);
        if (rejected != 64)
        {
            Directory.Delete(app, recursive: true);
            throw new InvalidOperationException("The app accepted server arguments. The invalid layout was removed.");
        }

        await Console.Out.WriteLineAsync("Built " + app).ConfigureAwait(false);
        return 0;
    }

    private static void CopyDirectory(string source, string destination, Func<string, bool> include)
    {
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(include))
        {
            string target = Path.Join(destination, Path.GetRelativePath(source, path));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private static async Task BundleFontsAsync(string root, string destination)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Weft-Build");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/microsoft/cascadia-code/releases/latest");
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
                var download = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                using Stream stream = await http.GetStreamAsync(download).ConfigureAwait(false);
                using (FileStream file = File.Create(temporary))
                {
                    await stream.CopyToAsync(file).ConfigureAwait(false);
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
            ZipArchiveEntry entry = zip.GetEntry("ttf/" + font)
                ?? throw new InvalidDataException("The font release is missing " + font);
            await entry.ExtractToFileAsync(Path.Join(destination, font), overwrite: true).ConfigureAwait(false);
        }

        string tag = release.RootElement.GetProperty("tag_name").GetString()!;
        var licenseUri = new Uri("https://raw.githubusercontent.com/microsoft/cascadia-code/"
            + Uri.EscapeDataString(tag) + "/LICENSE");
        string license = await http.GetStringAsync(licenseUri).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Join(destination, "LICENSE.txt"), license).ConfigureAwait(false);
        byte[] hash = await HashAsync(archive).ConfigureAwait(false);
        string source = "https://github.com/microsoft/cascadia-code/releases/tag/" + tag + "\n" + name
            + "\nSHA256: " + Convert.ToHexStringLower(hash) + "\n";
        await File.WriteAllTextAsync(Path.Join(destination, "source.txt"), source).ConfigureAwait(false);
    }

    private static async Task<byte[]> HashAsync(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream).ConfigureAwait(false);
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments)
    {
        int exitCode = await ExitCodeAsync(executable, arguments).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(executable + " failed with exit code "
                + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static async Task<int> ExitCodeAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}
