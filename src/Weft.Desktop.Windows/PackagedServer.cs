using Weft.Client;

namespace Weft.Desktop.Windows;

/// <summary>
/// Starts the bundled server from a copy outside an installed package, whose files Windows replaces on upgrade.
/// </summary>
/// <remarks>
/// The server keeps sessions running after every window closes, including while the app is upgraded or removed.
/// A packaged app therefore copies the server and its console host into the user's local data once per package
/// version and starts that copy. Copies from other versions are removed once no server runs from them.
/// </remarks>
internal static class PackagedServer
{
    private static readonly string[] s_files = ["weft-server.exe", "hex1bpty.exe", "conpty.dll", "OpenConsole.exe"];

    /// <summary>
    /// Gets the server to start: the bundled one, or its copy outside the package when the app runs from one.
    /// </summary>
    /// <param name="bundled">The server beside the app.</param>
    /// <returns>The server path.</returns>
    internal static string Resolve(string bundled)
    {
        if (PackageName() is not { } package)
        {
            return bundled;
        }

        string root = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "weft",
            "server");
        string target = Path.Join(root, package);
        string source = Path.GetDirectoryName(bundled)!;
        if (!IsComplete(source, target))
        {
            string partial = target + ".partial-"
                + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = Directory.CreateDirectory(partial);
            foreach (string file in s_files)
            {
                File.Copy(Path.Join(source, file), Path.Join(partial, file), overwrite: true);
            }

            try
            {
                Directory.Move(partial, target);
            }
            catch (IOException) when (IsComplete(source, target))
            {
                // Another window finished the same copy first.
                Directory.Delete(partial, recursive: true);
            }
        }

        RemoveOtherVersions(root, target);
        return Path.Join(target, s_files[0]);
    }

    private static bool IsComplete(string source, string target)
    {
        return s_files.All(file => new FileInfo(Path.Join(target, file)) is { Exists: true } copy
            && copy.Length == new FileInfo(Path.Join(source, file)).Length);
    }

    private static void RemoveOtherVersions(string root, string current)
    {
        foreach (string directory in Directory.EnumerateDirectories(root)
            .Where(path => !string.Equals(path, current, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A server still runs from this copy; it is removed after that server stops.
                ClientLog.Debug("Kept server copy " + directory + ": " + exception.Message);
            }
        }
    }

    private static unsafe string? PackageName()
    {
        int length = 0;
        if (NativeMethods.GetCurrentPackageFullName(ref length, null) == NativeMethods.AppModelErrorNoPackage)
        {
            return null;
        }

        char* name = stackalloc char[length];
        return NativeMethods.GetCurrentPackageFullName(ref length, name) == 0 ? new string(name, 0, length - 1) : null;
    }
}
