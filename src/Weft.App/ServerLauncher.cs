using System.Diagnostics;
using System.Net.Sockets;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Connects to a running server or starts one detached and waits for it.
/// </summary>
internal static class ServerLauncher
{
    /// <summary>
    /// Connects when a server is listening.
    /// </summary>
    /// <param name="socketPath">The control socket path.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The client, or null when nothing answers.</returns>
    internal static async Task<ControlClient?> TryConnectAsync(string socketPath, CancellationToken cancellationToken)
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
    internal static async Task<ControlClient> ConnectOrStartAsync(string runtimeDirectory, CancellationToken cancellationToken)
    {
        string socketPath = WeftPaths.ControlSocketPath(runtimeDirectory);
        ControlClient? client = await TryConnectAsync(socketPath, cancellationToken).ConfigureAwait(false);
        if (client is not null)
        {
            return client;
        }

        Start(runtimeDirectory);
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

    private static void Start(string runtimeDirectory)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The weft executable path is unknown.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string host = Path.GetFileNameWithoutExtension(executable);
        if (string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Framework-dependent launch: the entry assembly must be passed to the dotnet host.
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }

        start.ArgumentList.Add("server");
        start.ArgumentList.Add("--detached");
        start.ArgumentList.Add("--socket-dir");
        start.ArgumentList.Add(runtimeDirectory);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The weft server process could not be started.");
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
    }
}
