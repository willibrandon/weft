using System.Diagnostics;
using System.Net.Sockets;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// Connects to a running server or starts one detached and waits for it.
/// </summary>
public static class ServerLauncher
{
    /// <summary>
    /// Connects when a server is listening.
    /// </summary>
    /// <param name="socketPath">The control socket path.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The client, or null when nothing answers.</returns>
    public static async Task<ControlClient?> TryConnectAsync(string socketPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(socketPath))
        {
            return null;
        }

        try
        {
            return await ControlClient.ConnectAsync(socketPath, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ProtocolException)
        {
            return null;
        }
    }

    /// <summary>
    /// Connects to the server, starting a detached one first when needed.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The client.</returns>
    /// <exception cref="InvalidOperationException">The server could not be started.</exception>
    public static Task<ControlClient> ConnectOrStartAsync(string runtimeDirectory, CancellationToken cancellationToken)
    {
        return ConnectOrStartAsync(runtimeDirectory, null, cancellationToken);
    }

    /// <summary>
    /// Connects to the server, starting the supplied executable when needed.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <param name="executablePath">The bundled CLI path, or null to use the current CLI.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The connected control client.</returns>
    public static async Task<ControlClient> ConnectOrStartAsync(string runtimeDirectory, string? executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string socketPath = WeftPaths.ControlSocketPath(runtimeDirectory);
        ControlClient? client = await TryConnectAsync(socketPath, cancellationToken).ConfigureAwait(false);
        if (client is not null)
        {
            return client;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Start(runtimeDirectory, executablePath);
        long deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            client = await TryConnectAsync(socketPath, cancellationToken).ConfigureAwait(false);
            if (client is not null)
            {
                return client;
            }
        }

        throw new InvalidOperationException("The weft server did not start within ten seconds. Check the server log under " + WeftPaths.ResolveStateDirectory() + ".");
    }

    private static void Start(string runtimeDirectory, string? executablePath)
    {
        string executable = executablePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("The weft executable path is unknown.");
        if (executablePath is not null && Environment.ProcessPath is { } current
            && string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The desktop app cannot start itself as the server. Rebuild the app bundle.");
        }

        List<string> arguments = [];
        string host = Path.GetFileNameWithoutExtension(executable);
        if (executablePath is null && string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Framework-dependent launch: the entry assembly must be passed to the dotnet host.
            arguments.Add(Environment.GetCommandLineArgs()[0]);
        }

        arguments.AddRange(["server", "--detached", "--socket-dir", runtimeDirectory]);
        if (OperatingSystem.IsWindows())
        {
            _ = WindowsServerProcess.Start(executable, arguments);
            return;
        }

        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The weft server process could not be started.");
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
    }
}
