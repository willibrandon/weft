using System.Diagnostics;
using System.Globalization;
using Weft.Protocol;

namespace Weft.Server;

/// <summary>
/// Runs configured shell commands when events fire, passing the event through environment variables.
/// </summary>
internal sealed class HookRunner : IDisposable
{
    private readonly IReadOnlyDictionary<string, string> _hooks;
    private readonly EventSubscription _subscription;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _pump;

    /// <summary>
    /// Subscribes to the event log and starts running hooks.
    /// </summary>
    /// <param name="events">The event log.</param>
    /// <param name="hooks">Event names mapped to commands.</param>
    internal HookRunner(EventLog events, IReadOnlyDictionary<string, string> hooks)
    {
        _hooks = hooks;
        _subscription = events.Subscribe(null);
        _pump = PumpAsync();
    }

    /// <summary>
    /// Stops running hooks.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _subscription.Dispose();
        _stopping.Dispose();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (ProtocolMessage message in _subscription.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                if (message.Event is { } name && _hooks.TryGetValue(name, out string? command))
                {
                    Run(name, command, message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Run(string name, string command, ProtocolMessage message)
    {
        ProcessStartInfo start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.Environment["WEFT_EVENT"] = name;
        start.Environment["WEFT_SEQ"] = (message.Seq ?? 0).ToString(CultureInfo.InvariantCulture);
        Describe(name, message, start.Environment);
        try
        {
            using var process = Process.Start(start);
            process?.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ServerLog.Warn("Hook for " + name + " failed to start: " + exception.Message);
        }
    }

    private static void Describe(string name, ProtocolMessage message, IDictionary<string, string?> environment)
    {
        try
        {
            if (name.StartsWith("session.", StringComparison.Ordinal))
            {
                SessionInfo session = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.SessionEventData).Session;
                environment["WEFT_SESSION"] = session.Name;
                environment["WEFT_SESSION_ID"] = session.Id;
            }
            else if (name.StartsWith("tab.", StringComparison.Ordinal))
            {
                TabInfo tab = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.TabEventData).Tab;
                environment["WEFT_SESSION_ID"] = tab.Session;
                environment["WEFT_TAB"] = tab.Id;
                environment["WEFT_TITLE"] = tab.Name;
            }
            else if (name.StartsWith("block.", StringComparison.Ordinal))
            {
                BlockInfo block = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.BlockEventData).Block;
                environment["WEFT_SESSION_ID"] = block.Session;
                environment["WEFT_TAB"] = block.Tab;
                environment["WEFT_BLOCK"] = block.Id;
                environment["WEFT_TITLE"] = block.Title;
                environment["WEFT_STATE"] = block.State.ToString().ToLowerInvariant();
                environment["WEFT_COMMAND"] = block.Command;
                if (block.ExitCode is { } code)
                {
                    environment["WEFT_EXIT_CODE"] = code.ToString(CultureInfo.InvariantCulture);
                }
            }
            else if (name.StartsWith("client.", StringComparison.Ordinal))
            {
                ClientInfo client = ProtocolCodec.FromElement(message.Data, ProtocolJsonContext.Default.ClientEventData).Client;
                environment["WEFT_SESSION_ID"] = client.Session;
                environment["WEFT_CLIENT"] = client.Id;
            }
        }
        catch (ProtocolException)
        {
        }
    }
}
