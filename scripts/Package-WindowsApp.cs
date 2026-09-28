#!/usr/bin/env -S dotnet --
#:property TargetFramework=net11.0
#:property LangVersion=15.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Weft.Scripts;

/// <summary>
/// Wraps the built Windows app in an unsigned development MSIX without installing it.
/// </summary>
/// <remarks>
/// Windows installs an unsigned package only when its publisher carries the development OID, and an
/// executable unsigned package needs an administrator. The development identity differs from a future
/// signed release, so moving between them is a separate test.
/// </remarks>
internal static class PackageWindowsApp
{
    /// <summary>
    /// The publisher Windows requires for unsigned packages.
    /// </summary>
    internal const string Publisher = "CN=Weft Development, OID.2.25.311729368913984317654407730594956997722=1";

    private const string LogoList = "logos.resfiles";

    private static readonly XNamespace s_foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace s_uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace s_uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace s_desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace s_desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";
    private static readonly XNamespace s_restricted =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            await Console.Out.WriteLineAsync(
                "Packages artifacts/windows/win-<arch>/Weft as an unsigned development MSIX. "
                + "Usage: dotnet run --file scripts/Package-WindowsApp.cs [--arch x64|arm64] "
                + "[--version major.minor.build]")
                .ConfigureAwait(false);
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            await Console.Error.WriteLineAsync("Package the Windows app on Windows.").ConfigureAwait(false);
            return 1;
        }

        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string? version = null;
        for (int index = 0; index < args.Length; index += 2)
        {
            switch (args[index])
            {
                case "--arch" when index + 1 < args.Length && args[index + 1] is "x64" or "arm64":
                    arch = args[index + 1];
                    break;
                case "--version" when index + 1 < args.Length && Version.TryParse(args[index + 1], out _):
                    // Upgrade tests package a later version of the same build.
                    version = args[index + 1];
                    break;
                default:
                    await Console.Error.WriteLineAsync(
                        "Usage: dotnet run --file scripts/Package-WindowsApp.cs [--arch x64|arm64] "
                        + "[--version major.minor.build]").ConfigureAwait(false);
                    return 1;
            }
        }

        string root = Directory.GetCurrentDirectory();
        string output = Path.Join(root, "artifacts", "windows", "win-" + arch);
        string app = Path.Join(output, "Weft");
        if (!File.Exists(Path.Join(app, "Weft.exe")))
        {
            await Console.Error.WriteLineAsync("Build the app first: dotnet run --file scripts/Build-WindowsApp.cs")
                .ConfigureAwait(false);
            return 1;
        }

        version ??= (await CaptureAsync("dotnet",
            ["msbuild", "src/Weft.App/Weft.App.csproj", "-getProperty:VersionPrefix"])
            .ConfigureAwait(false)).Trim();
        var packageVersion = new Version(Version.Parse(version).ToString(3) + ".0");
        string stage = Path.Join(output, "package");
        if (Directory.Exists(stage))
        {
            Directory.Delete(stage, recursive: true);
        }

        foreach (string path in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
        {
            string target = Path.Join(stage, Path.GetRelativePath(app, path));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }

        string manifest = Path.Join(stage, "AppxManifest.xml");
        Manifest(packageVersion, arch).Save(manifest);
        string tools = FindTools();
        string configuration = Path.Join(output, "priconfig.xml");
        PriConfiguration().Save(configuration);
        // The shell resolves the manifest's Assets\ logo paths through the index, so each logo is listed by
        // its path in the package. A folder index would name them relative to Assets, leaving the taskbar
        // and Start with no icon.
        string logos = Path.Join(stage, LogoList);
        await File.WriteAllLinesAsync(logos,
            Directory.EnumerateFiles(Path.Join(stage, "Assets"), "*.png")
                .Select(path => Path.GetRelativePath(stage, path)))
            .ConfigureAwait(false);
        // The package's resource index merges the app's WinUI resources with the scaled logos.
        await RunAsync(Path.Join(tools, "makepri.exe"),
            ["new", "/pr", stage, "/cf", configuration, "/mn", manifest,
                "/of", Path.Join(stage, "resources.pri"), "/o"])
            .ConfigureAwait(false);
        File.Delete(logos);
        string package = Path.Join(root, "artifacts", "windows",
            string.Create(CultureInfo.InvariantCulture, $"Weft-{packageVersion.ToString(3)}-{arch}.msix"));
        await RunAsync(Path.Join(tools, "makeappx.exe"), ["pack", "/d", stage, "/p", package, "/o"])
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Packaged " + package).ConfigureAwait(false);
        return 0;
    }

    private static XDocument Manifest(Version version, string arch)
    {
        XNamespace f = s_foundation;
        XNamespace uap = s_uap;
        XNamespace uap3 = s_uap3;
        XNamespace desktop = s_desktop;
        XNamespace desktop6 = s_desktop6;
        XNamespace rescap = s_restricted;
        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(f + "Package",
                new XAttribute(XNamespace.Xmlns + "uap", uap),
                new XAttribute(XNamespace.Xmlns + "uap3", uap3),
                new XAttribute(XNamespace.Xmlns + "desktop", desktop),
                new XAttribute(XNamespace.Xmlns + "desktop6", desktop6),
                new XAttribute(XNamespace.Xmlns + "rescap", rescap),
                new XAttribute("IgnorableNamespaces", "uap uap3 desktop desktop6 rescap"),
                new XElement(f + "Identity",
                    new XAttribute("Name", "Weft.Development"),
                    new XAttribute("Publisher", Publisher),
                    new XAttribute("Version", version.ToString()),
                    new XAttribute("ProcessorArchitecture", arch)),
                new XElement(f + "Properties",
                    new XElement(f + "DisplayName", "Weft"),
                    new XElement(f + "PublisherDisplayName", "Weft development build"),
                    new XElement(f + "Logo", @"Assets\StoreLogo.png"),
                    // Sessions, sockets, and preferences live in the user's own profile, shared with the CLI
                    // and kept when the app is removed, rather than in a copy private to the package.
                    new XElement(desktop6 + "FileSystemWriteVirtualization", "disabled"),
                    new XElement(desktop6 + "RegistryWriteVirtualization", "disabled")),
                new XElement(f + "Dependencies",
                    new XElement(f + "TargetDeviceFamily",
                        new XAttribute("Name", "Windows.Desktop"),
                        new XAttribute("MinVersion", "10.0.17763.0"),
                        new XAttribute("MaxVersionTested", "10.0.26100.0"))),
                new XElement(f + "Resources", new XElement(f + "Resource", new XAttribute("Language", "en-US"))),
                new XElement(f + "Applications",
                    new XElement(f + "Application",
                        new XAttribute("Id", "Weft"),
                        new XAttribute("Executable", "Weft.exe"),
                        new XAttribute("EntryPoint", "Windows.FullTrustApplication"),
                        new XElement(uap + "VisualElements",
                            new XAttribute("DisplayName", "Weft"),
                            new XAttribute("Description",
                                "Durable terminal sessions that keep running when every window closes."),
                            new XAttribute("BackgroundColor", "transparent"),
                            new XAttribute("Square150x150Logo", @"Assets\Square150x150Logo.png"),
                            new XAttribute("Square44x44Logo", @"Assets\Square44x44Logo.png"),
                            new XElement(uap + "DefaultTile",
                                new XAttribute("Wide310x150Logo", @"Assets\Wide310x150Logo.png"))),
                        // Starting weft-desktop from a terminal opens the app with its package identity and the
                        // caller's environment, which also lets tests point it at private sessions.
                        new XElement(f + "Extensions",
                            new XElement(uap3 + "Extension",
                                new XAttribute("Category", "windows.appExecutionAlias"),
                                new XAttribute("Executable", "Weft.exe"),
                                new XAttribute("EntryPoint", "Windows.FullTrustApplication"),
                                new XElement(uap3 + "AppExecutionAlias",
                                    new XElement(desktop + "ExecutionAlias",
                                        new XAttribute("Alias", "weft-desktop.exe"))))))),
                new XElement(f + "Capabilities",
                    new XElement(rescap + "Capability", new XAttribute("Name", "runFullTrust")),
                    new XElement(rescap + "Capability", new XAttribute("Name", "unvirtualizedResources")))));
    }

    private static XDocument PriConfiguration()
    {
        static XElement Defaults()
        {
            (string Name, string Value)[] qualifiers =
            [
                ("Language", "en-US"), ("Contrast", "standard"), ("Scale", "100"), ("HomeRegion", "001"),
                ("TargetSize", "256"), ("LayoutDirection", "LTR"), ("Theme", "dark"), ("AlternateForm", ""),
                ("DXFeatureLevel", "DX9"), ("Configuration", ""), ("DeviceFamily", "Universal"), ("Custom", "")
            ];
            return new XElement("default",
                qualifiers.Select(qualifier => new XElement("qualifier", new XAttribute("name", qualifier.Name),
                    new XAttribute("value", qualifier.Value))));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("resources",
                new XAttribute("targetOsVersion", "10.0.0"),
                new XAttribute("majorVersion", "1"),
                new XElement("index",
                    new XAttribute("root", "\\"),
                    new XAttribute("startIndexAt", LogoList),
                    Defaults(),
                    new XElement("indexer-config",
                        new XAttribute("type", "RESFILES"),
                        new XAttribute("qualifierDelimiter", "."))),
                new XElement("index",
                    new XAttribute("root", "\\"),
                    new XAttribute("startIndexAt", "Weft.pri"),
                    Defaults(),
                    new XElement("indexer-config", new XAttribute("type", "PRI")))));
    }

    private static string FindTools()
    {
        string host = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string kits = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits", "10", "bin");
        string? tools = Directory.Exists(kits)
            ? Directory.EnumerateDirectories(kits, "10.*")
                .Select(directory => Path.Join(directory, host))
                .Where(directory => File.Exists(Path.Join(directory, "makeappx.exe"))
                    && File.Exists(Path.Join(directory, "makepri.exe")))
                .OrderByDescending(directory => Version.Parse(Path.GetFileName(Path.GetDirectoryName(directory)!)))
                .FirstOrDefault()
            : null;
        return tools
            ?? throw new InvalidOperationException("MakeAppx and MakePri were not found. Install the Windows SDK.");
    }

    private static async Task<string> CaptureAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode == 0 ? output : throw new InvalidOperationException(executable + " failed.");
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments)
    {
        // The packaging tools list every file they process; their output is shown only when they fail.
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start " + executable);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            await Console.Error.WriteLineAsync(await output.ConfigureAwait(false) + await error.ConfigureAwait(false))
                .ConfigureAwait(false);
            throw new InvalidOperationException(executable + " failed with exit code "
                + process.ExitCode.ToString(CultureInfo.InvariantCulture));
        }
    }
}
