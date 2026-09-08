using System.CommandLine;
using System.Globalization;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Block commands: split, input, capture, wait, run, focus, zoom, close, kill, rename.
/// </summary>
internal static class BlockCommands
{
    /// <summary>Creates the split command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateSplit()
    {
        var command = new Command("split", "Split a block to create another.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to split.");
        var down = new Option<bool>("--down") { Description = "Split below instead of to the right." };
        var before = new Option<bool>("--before") { Description = "Place the new block before the target." };
        var size = new Option<int?>("--size") { Description = "Size of the new block in cells." };
        var cwd = new Option<string?>("--cwd") { Description = "Working directory." };
        var noFocus = new Option<bool>("--no-focus") { Description = "Keep focus on the current block." };
        Argument<string[]> run = CommonOptions.Command();
        command.Arguments.Add(target);
        command.Options.Add(down);
        command.Options.Add(before);
        command.Options.Add(size);
        command.Options.Add(cwd);
        command.Options.Add(noFocus);
        command.Arguments.Add(run);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            string[] arguments = parseResult.GetValue(run) ?? [];
            BlockInfo block = await client.SplitAsync(new BlockSplitParams
            {
                Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)),
                Orientation = parseResult.GetValue(down) ? SplitOrientation.TopBottom : SplitOrientation.LeftRight,
                Before = parseResult.GetValue(before),
                Size = parseResult.GetValue(size),
                Cwd = parseResult.GetValue(cwd),
                Focus = !parseResult.GetValue(noFocus),
                Command = arguments.Length > 0 ? arguments : null
            }, cancellationToken).ConfigureAwait(false);
            context.Write(block, ProtocolJsonContext.Default.BlockInfo, value => [value.Id]);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the send command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateSend()
    {
        var command = new Command("send", "Send keys to a block; names like Enter, C-c, Up are encoded, other text is typed.");
        var target = new Option<string?>("--target", "-t") { Description = "Block to send to." };
        var literal = new Option<bool>("--literal", "-l") { Description = "Type every argument literally." };
        var keys = new Argument<string[]>("keys") { Description = "Keys and text.", Arity = ArgumentArity.OneOrMore };
        command.Options.Add(target);
        command.Options.Add(literal);
        command.Arguments.Add(keys);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.SendKeysAsync(new BlockSendKeysParams
            {
                Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)),
                Keys = parseResult.GetValue(keys) ?? [],
                Literal = parseResult.GetValue(literal)
            }, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the type command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateType()
    {
        var command = new Command("type", "Type literal text into a block.");
        var target = new Option<string?>("--target", "-t") { Description = "Block to type into." };
        var text = new Argument<string>("text") { Description = "The text." };
        command.Options.Add(target);
        command.Arguments.Add(text);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.TypeAsync(new BlockTextParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)), Text = parseResult.GetValue(text) ?? string.Empty }, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the paste command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreatePaste()
    {
        var command = new Command("paste", "Paste text into a block, or the server paste buffer when no text is given.");
        var target = new Option<string?>("--target", "-t") { Description = "Block to paste into." };
        var text = new Argument<string?>("text") { Description = "The text; reads standard input when omitted and it is not a terminal.", Arity = ArgumentArity.ZeroOrOne };
        command.Options.Add(target);
        command.Arguments.Add(text);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            string? value = parseResult.GetValue(text);
            if (value is null && Console.IsInputRedirected)
            {
                value = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            value ??= (await client.GetPasteAsync(cancellationToken).ConfigureAwait(false)).Text;
            await client.PasteAsync(new BlockTextParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)), Text = value }, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the capture command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateCapture()
    {
        var command = new Command("capture", "Print a block's screen.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to capture.");
        var history = new Option<int>("--history") { Description = "Scrollback lines to include." };
        var ansi = new Option<bool>("--ansi") { Description = "Keep styling escape sequences." };
        command.Arguments.Add(target);
        command.Options.Add(history);
        command.Options.Add(ansi);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            BlockCaptureResult capture = await client.CaptureAsync(new BlockCaptureParams
            {
                Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)),
                History = parseResult.GetValue(history),
                Format = parseResult.GetValue(ansi) ? CaptureFormat.Ansi : CaptureFormat.Text
            }, cancellationToken).ConfigureAwait(false);
            context.Write(capture, ProtocolJsonContext.Default.BlockCaptureResult, value => value.Lines);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the wait command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateWait()
    {
        var command = new Command("wait", "Wait for a pattern, exit, or change on a block.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to watch.");
        var pattern = new Option<string?>("--for") { Description = "Regular expression to wait for on screen." };
        var exit = new Option<bool>("--exit") { Description = "Return when the process exits." };
        var revision = new Option<long?>("--revision") { Description = "Return once the output revision passes this value." };
        var timeout = new Option<int>("--timeout") { Description = "Timeout in milliseconds.", DefaultValueFactory = _ => 30_000 };
        command.Arguments.Add(target);
        command.Options.Add(pattern);
        command.Options.Add(exit);
        command.Options.Add(revision);
        command.Options.Add(timeout);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            BlockWaitResult result = await client.WaitAsync(new BlockWaitParams
            {
                Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)),
                Pattern = parseResult.GetValue(pattern),
                Exit = parseResult.GetValue(exit),
                Revision = parseResult.GetValue(revision),
                TimeoutMs = parseResult.GetValue(timeout)
            }, cancellationToken).ConfigureAwait(false);
            context.Write(result, ProtocolJsonContext.Default.BlockWaitResult, value =>
                [value.Outcome.ToString().ToLowerInvariant() + (value.Line is { } line ? "  " + line : string.Empty) + (value.ExitCode is { } code ? "  exit " + code.ToString(CultureInfo.InvariantCulture) : string.Empty)]);
            if (result.Outcome == WaitOutcome.Timeout)
            {
                throw new ProtocolException(ErrorCodes.Timeout, "timed out");
            }
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the run command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateRun()
    {
        var command = new Command("run", "Run a command in a new block and wait for it to exit.");
        var target = new Option<string?>("--session", "-s") { Description = "Session or tab to run in." };
        var cwd = new Option<string?>("--cwd") { Description = "Working directory." };
        var timeout = new Option<int>("--timeout") { Description = "Timeout in milliseconds before returning with the block still running.", DefaultValueFactory = _ => 600_000 };
        var keep = new Option<bool>("--keep") { Description = "Keep the block open after the command exits." };
        var run = new Argument<string[]>("command") { Description = "Command and arguments.", Arity = ArgumentArity.OneOrMore };
        command.Options.Add(target);
        command.Options.Add(cwd);
        command.Options.Add(timeout);
        command.Options.Add(keep);
        command.Arguments.Add(run);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeWithCodeAsync(parseResult, async (context, client) =>
        {
            BlockRunResult result = await client.RunAsync(new BlockRunParams
            {
                Target = parseResult.GetValue(target),
                Command = parseResult.GetValue(run) ?? [],
                Cwd = parseResult.GetValue(cwd),
                TimeoutMs = parseResult.GetValue(timeout),
                Close = !parseResult.GetValue(keep)
            }, cancellationToken).ConfigureAwait(false);
            context.Write(result, ProtocolJsonContext.Default.BlockRunResult, value => [value.Output]);
            if (!context.Json && !result.Completed)
            {
                await Console.Error.WriteLineAsync("weft: still running in " + result.Block).ConfigureAwait(false);
            }

            return result.Completed ? Math.Clamp(result.ExitCode ?? 0, 0, 255) : 124;
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the focus command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateFocus()
    {
        var command = new Command("focus", "Focus a block, optionally by direction from it.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to focus or move from.");
        Option<LayoutDirection?> direction = DirectionOption();
        command.Arguments.Add(target);
        command.Options.Add(direction);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            BlockInfo block = await client.FocusAsync(new BlockFocusParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)), Direction = parseResult.GetValue(direction) }, cancellationToken).ConfigureAwait(false);
            context.Write(block, ProtocolJsonContext.Default.BlockInfo, value => [value.Id]);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the zoom command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateZoom()
    {
        var command = new Command("zoom", "Toggle zoom on a block.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to zoom.");
        command.Arguments.Add(target);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            TabInfo tab = await client.ZoomAsync(new BlockZoomParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)) }, cancellationToken).ConfigureAwait(false);
            context.Write(tab, ProtocolJsonContext.Default.TabInfo, value => [value.Zoomed is { } zoomed ? "zoomed " + zoomed : "unzoomed"]);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the close command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateClose()
    {
        var command = new Command("close", "Close a block, ending its process.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to close.");
        command.Arguments.Add(target);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.CloseBlockAsync(CommonOptions.EffectiveTarget(parseResult.GetValue(target)), cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the kill command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateKill()
    {
        var command = new Command("kill", "Send a signal to a block's process.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to signal.");
        var signal = new Option<int>("--signal", "-s") { Description = "Signal number.", DefaultValueFactory = _ => 15 };
        command.Arguments.Add(target);
        command.Options.Add(signal);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            await client.KillBlockAsync(new BlockKillParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)), Signal = parseResult.GetValue(signal) }, cancellationToken).ConfigureAwait(false);
            context.Write(EmptyResult.Instance, ProtocolJsonContext.Default.EmptyResult, _ => []);
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the rename command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateRename()
    {
        var command = new Command("rename", "Pin a block's title, or a session's or tab's name.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block, tab, or session.");
        var name = new Argument<string>("name") { Description = "The new title or name; empty unpins a block title." };
        var session = new Option<bool>("--session") { Description = "Rename the session instead of a block." };
        var tab = new Option<bool>("--tab") { Description = "Rename the tab instead of a block." };
        command.Arguments.Add(target);
        command.Arguments.Add(name);
        command.Options.Add(session);
        command.Options.Add(tab);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            string? effective = CommonOptions.EffectiveTarget(parseResult.GetValue(target));
            string value = parseResult.GetValue(name) ?? string.Empty;
            if (parseResult.GetValue(session))
            {
                SessionInfo renamed = await client.RenameSessionAsync(new SessionRenameParams { Target = effective, Name = value }, cancellationToken).ConfigureAwait(false);
                context.Write(renamed, ProtocolJsonContext.Default.SessionInfo, item => [SessionCommands.FormatSession(item)]);
            }
            else if (parseResult.GetValue(tab))
            {
                TabInfo renamed = await client.RenameTabAsync(new TabRenameParams { Target = effective, Name = value }, cancellationToken).ConfigureAwait(false);
                context.Write(renamed, ProtocolJsonContext.Default.TabInfo, item => [item.Id + "  " + item.Name]);
            }
            else
            {
                BlockInfo renamed = await client.RenameBlockAsync(new BlockRenameParams { Target = effective, Title = value }, cancellationToken).ConfigureAwait(false);
                context.Write(renamed, ProtocolJsonContext.Default.BlockInfo, item => [item.Id + "  " + item.Title]);
            }
        }, cancellationToken));
        return command;
    }

    /// <summary>
    /// Creates a direction option accepting left, right, up, or down.
    /// </summary>
    /// <returns>The option.</returns>
    internal static Option<LayoutDirection?> DirectionOption()
    {
        var option = new Option<LayoutDirection?>("--direction", "-d")
        {
            Description = "left, right, up, or down.",
            CustomParser = result =>
            {
                string? token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
                return token?.ToUpperInvariant() switch
                {
                    "LEFT" => LayoutDirection.Left,
                    "RIGHT" => LayoutDirection.Right,
                    "UP" => LayoutDirection.Up,
                    "DOWN" => LayoutDirection.Down,
                    _ => null
                };
            }
        };
        return option;
    }
}
