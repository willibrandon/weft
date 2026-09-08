using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Mcp;

/// <summary>
/// Tools that let an agent list, create, drive, and read weft blocks.
/// </summary>
/// <param name="bridge">The shared control connection.</param>
[McpServerToolType]
public sealed class WeftTools(WeftBridge bridge)
{
    /// <summary>
    /// Lists sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One line per session.</returns>
    [McpServerTool(Name = "list_sessions"), Description("List weft sessions with their tabs, blocks, size, and whether a person is attached.")]
    public async Task<string> ListSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            SessionListResult result = await client.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (result.Sessions.Count == 0)
            {
                return "No sessions.";
            }

            return string.Join('\n', result.Sessions.Select(session =>
                string.Create(CultureInfo.InvariantCulture, $"{session.Name} ({session.Id}): {session.Tabs} tabs, {session.Blocks} blocks, {session.Width}x{session.Height}, {(session.Clients > 0 ? "attached" : "detached")}")));
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Lists blocks.
    /// </summary>
    /// <param name="target">A session or tab; every session when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One line per block.</returns>
    [McpServerTool(Name = "list_blocks"), Description("List blocks with their ids, state, size, process id, and title. A block is one terminal.")]
    public async Task<string> ListBlocksAsync(
        [Description("Session name, session id, or session:tab target. Omit for every session.")] string? target = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            BlockListResult result = await client.ListBlocksAsync(target, cancellationToken).ConfigureAwait(false);
            if (result.Blocks.Count == 0)
            {
                return "No blocks.";
            }

            return string.Join('\n', result.Blocks.Select(block =>
                string.Create(CultureInfo.InvariantCulture, $"{block.Id} in {block.Session}:{block.Tab} [{block.State.ToString().ToLowerInvariant()}{(block.ExitCode is { } code ? " " + code : string.Empty)}] {block.Width}x{block.Height}{(block.Pid is { } pid ? " pid " + pid : string.Empty)}: {block.Title}")));
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Creates a block by splitting an existing one.
    /// </summary>
    /// <param name="target">The block to split; the active block when omitted.</param>
    /// <param name="command">The command to run; the default shell when omitted.</param>
    /// <param name="below">Whether to split below instead of to the right.</param>
    /// <param name="cwd">The working directory.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new block id.</returns>
    [McpServerTool(Name = "create_block"), Description("Create a new block (terminal) by splitting an existing block. Returns the new block id, which later tools take as their target.")]
    public async Task<string> CreateBlockAsync(
        [Description("Block, tab, or session to split from. Omit for the active block of the latest session.")] string? target = null,
        [Description("Command and arguments to run, one string per argument. Omit to start the default shell.")] string[]? command = null,
        [Description("Split below instead of to the right.")] bool below = false,
        [Description("Working directory for the command.")] string? cwd = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            BlockInfo block = await client.SplitAsync(new BlockSplitParams
            {
                Target = target,
                Orientation = below ? SplitOrientation.TopBottom : SplitOrientation.LeftRight,
                Command = command is { Length: > 0 } ? command : null,
                Cwd = cwd,
                Focus = false
            }, cancellationToken).ConfigureAwait(false);
            return block.Id;
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Runs a command and waits for it to exit.
    /// </summary>
    /// <param name="command">The command and arguments.</param>
    /// <param name="target">The session to run in.</param>
    /// <param name="cwd">The working directory.</param>
    /// <param name="timeoutMs">How long to wait before returning with the block still running.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The exit code and the captured output.</returns>
    [McpServerTool(Name = "run"), Description("Run a command in a new block, wait for it to exit, and return its exit code and output. Output is truncated head and tail when large. If the timeout passes the block keeps running and its id is returned so you can watch it with wait_for and capture.")]
    public async Task<string> RunAsync(
        [Description("Command and arguments, one string per argument.")] string[] command,
        [Description("Session to run in. Omit for the latest session, which is created when none exists.")] string? target = null,
        [Description("Working directory.")] string? cwd = null,
        [Description("Milliseconds to wait for exit before returning. Defaults to 120000.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            var parameters = new BlockRunParams
            {
                Target = target,
                Command = command,
                Cwd = cwd,
                TimeoutMs = timeoutMs ?? 120_000
            };
            BlockRunResult result;
            try
            {
                result = await client.RunAsync(parameters, cancellationToken).ConfigureAwait(false);
            }
            catch (ProtocolException exception) when (exception.Code == ErrorCodes.NotFound && IsSessionName(target))
            {
                // A plain session name that does not exist yet is created, so an agent can start work with one call.
                await client.CreateSessionAsync(new SessionCreateParams { Name = target, Cwd = cwd }, cancellationToken).ConfigureAwait(false);
                result = await client.RunAsync(parameters, cancellationToken).ConfigureAwait(false);
            }

            var text = new StringBuilder();
            if (result.Completed)
            {
                text.Append(CultureInfo.InvariantCulture, $"exit code {result.ExitCode ?? 0} after {result.DurationMs} ms");
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"still running in block {result.Block} after {result.DurationMs} ms");
            }

            if (result.Truncated)
            {
                text.Append(CultureInfo.InvariantCulture, $" ({result.TotalBytes} bytes of output, {result.OmittedBytes} omitted)");
            }

            text.Append('\n').Append(result.Output);
            return text.ToString();
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Sends keys to a block.
    /// </summary>
    /// <param name="target">The block.</param>
    /// <param name="keys">Key names and text.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A confirmation.</returns>
    [McpServerTool(Name = "send_keys"), Description("Send keys to a block. Names such as Enter, Tab, Escape, Up, C-c, or F5 are encoded; anything else is typed as text. Use capture or wait_for afterwards to observe the result.")]
    public async Task<string> SendKeysAsync(
        [Description("Block id or session:tab.block target.")] string target,
        [Description("Keys and text in order, for example [\"ls -la\", \"Enter\"].")] string[] keys,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(keys);
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            await client.SendKeysAsync(new BlockSendKeysParams { Target = target, Keys = keys }, cancellationToken).ConfigureAwait(false);
            return string.Create(CultureInfo.InvariantCulture, $"sent {keys.Length} keys to {target}");
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Captures a block's screen.
    /// </summary>
    /// <param name="target">The block.</param>
    /// <param name="history">Scrollback lines to include.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The screen text.</returns>
    [McpServerTool(Name = "capture"), Description("Return the text currently on a block's screen, optionally with scrollback lines above it. The first line reports the revision, which wait_for can use to wait for the next change.")]
    public async Task<string> CaptureAsync(
        [Description("Block id or session:tab.block target.")] string target,
        [Description("Scrollback lines to include above the screen. Defaults to 0.")] int? history = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams { Target = target, History = history ?? 0 }, cancellationToken).ConfigureAwait(false);
            return string.Create(CultureInfo.InvariantCulture, $"revision {capture.Revision}, cursor {capture.CursorX},{capture.CursorY}, {capture.Width}x{capture.Height}\n") + string.Join('\n', capture.Lines);
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Waits for a pattern, exit, or change.
    /// </summary>
    /// <param name="target">The block.</param>
    /// <param name="pattern">A regular expression to wait for.</param>
    /// <param name="exit">Whether to return on exit.</param>
    /// <param name="revision">A revision to wait past.</param>
    /// <param name="timeoutMs">The timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    [McpServerTool(Name = "wait_for"), Description("Wait until a regular expression matches a line on the block's screen, the block's process exits, or its output revision passes a value. Returns the outcome and the matching line.")]
    public async Task<string> WaitForAsync(
        [Description("Block id or session:tab.block target.")] string target,
        [Description("Regular expression matched against each screen line.")] string? pattern = null,
        [Description("Return when the process exits.")] bool exit = false,
        [Description("Return once the output revision exceeds this value, as reported by capture.")] long? revision = null,
        [Description("Milliseconds to wait. Defaults to 30000.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            BlockWaitResult result = await client.WaitAsync(new BlockWaitParams { Target = target, Pattern = pattern, Exit = exit, Revision = revision, TimeoutMs = timeoutMs ?? 30_000 }, cancellationToken).ConfigureAwait(false);
            string outcome = result.Outcome.ToString().ToLowerInvariant();
            return string.Create(CultureInfo.InvariantCulture, $"{outcome} at revision {result.Revision}{(result.ExitCode is { } code ? ", exit code " + code : string.Empty)}{(result.Line is { } line ? "\n" + line : string.Empty)}");
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Closes a block.
    /// </summary>
    /// <param name="target">The block.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A confirmation.</returns>
    [McpServerTool(Name = "close_block"), Description("Close a block, ending its process. Closing the last block of a tab closes the tab; closing the last tab closes the session.")]
    public async Task<string> CloseBlockAsync(
        [Description("Block id or session:tab.block target.")] string target,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ControlClient client = await bridge.ClientAsync(cancellationToken).ConfigureAwait(false);
            await client.CloseBlockAsync(target, cancellationToken).ConfigureAwait(false);
            return "closed " + target;
        }
        catch (ProtocolException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
    private static bool IsSessionName(string? target) =>
        target is { Length: > 0 } && !target.Contains(':', StringComparison.Ordinal) && !target.Contains('.', StringComparison.Ordinal) && !BlockId.TryParse(target, out _);
}
