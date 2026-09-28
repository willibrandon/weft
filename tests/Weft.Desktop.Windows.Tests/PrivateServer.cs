using System.Diagnostics;
using System.Text.Json;
using Weft.Core;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Points windows opened during one test at their own server, state, and shell configuration.
/// </summary>
/// <remarks>
/// A window starts the server on demand, detached from the test process, exactly as the app does. Disposal
/// shuts that server down and removes its files.
/// </remarks>
internal sealed class PrivateServer : IAsyncDisposable
{
    private PrivateServer(string root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets the directory that holds the socket, state, and configuration.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Gets the directory that holds the control socket.
    /// </summary>
    internal string RuntimeDirectory => Path.Join(Root, "run");

    /// <summary>
    /// Prepares a new server location; the first window opened afterward starts it.
    /// </summary>
    /// <returns>The server location.</returns>
    internal static async Task<PrivateServer> CreateAsync()
    {
        // Short names keep the socket path within the platform's limit.
        string root = Path.Join(DesktopApp.Root, Guid.NewGuid().ToString("N")[..8]);
        _ = Directory.CreateDirectory(root);
        string config = Path.Join(root, "config.json");
        await File.WriteAllTextAsync(config, "{\"shell\": \"" + JsonEncodedText.Encode(DesktopApp.Shell) + "\"}")
            .ConfigureAwait(true);
        Environment.SetEnvironmentVariable(WeftPaths.SocketDirectoryVariable, Path.Join(root, "run"));
        Environment.SetEnvironmentVariable(WeftPaths.StateDirectoryVariable, Path.Join(root, "state"));
        Environment.SetEnvironmentVariable(WeftPaths.ConfigPathVariable, config);
        return new PrivateServer(root);
    }

    /// <summary>
    /// Shuts the server down and removes its files.
    /// </summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (File.Exists(Path.Join(RuntimeDirectory, "weft.sock")))
        {
            var start = new ProcessStartInfo(DesktopApp.Server) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("shutdown");
            start.Environment[WeftPaths.SocketDirectoryVariable] = RuntimeDirectory;
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("The server could not be started to shut it down.");
            await process.WaitForExitAsync().ConfigureAwait(true);
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                // Shells and the server release their files as they exit.
                await Task.Delay(100).ConfigureAwait(true);
            }
        }
    }
}
