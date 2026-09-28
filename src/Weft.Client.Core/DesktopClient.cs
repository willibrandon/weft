using System.Diagnostics;
using System.Threading.Channels;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// Serializes native window commands and publishes bounded snapshots from an existing Weft session.
/// </summary>
public sealed class DesktopClient : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<DesktopCommand> _commands = Channel.CreateBounded<DesktopCommand>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<string, DesktopTerminal> _terminals = [with(StringComparer.Ordinal)];
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly Lock _notificationGate = new();
    private Action? _frameReady;
    private readonly IReadOnlyList<string>? _shellCommand;
    private DesktopFrame? _frame;
    private DesktopFrame? _lastFrame;
    private readonly Lock _commandGate = new();
    private bool _accepting = true;
    private string? _sessionId;
    private int _width;
    private int _height;
    private int _dirty = 1;
    private long _frameStarted;

    /// <summary>
    /// Starts connecting on a worker; construction never blocks the native UI thread.
    /// </summary>
    /// <param name="runtimeDirectory">The server runtime directory.</param>
    /// <param name="executablePath">The CLI to start if there is no server.</param>
    /// <param name="width">The initial viewport columns.</param>
    /// <param name="height">The initial viewport rows.</param>
    /// <param name="shell">An optional shell for newly created terminals.</param>
    public DesktopClient(string runtimeDirectory, string executablePath, int width, int height, string? shell = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(runtimeDirectory);
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        ValidateSize(width, height);
        _shellCommand = string.IsNullOrWhiteSpace(shell) ? null : [shell];
        _width = width;
        _height = height;
        Completion = Task.Run(() => RunAsync(runtimeDirectory, executablePath));
    }

    /// <summary>
    /// Gets the worker completion, including release of all owned connections.
    /// </summary>
    public Task Completion { get; }

    /// <summary>
    /// Takes the latest immutable frame, or null when nothing has changed.
    /// </summary>
    /// <returns>The most recent frame.</returns>
    public DesktopFrame? TakeFrame()
    {
        return Interlocked.Exchange(ref _frame, null);
    }

    /// <summary>
    /// Installs a nonblocking notification; clearing it waits for an in-flight callback to finish.
    /// </summary>
    /// <param name="callback">A callback that schedules UI work without polling synchronously.</param>
    public void SetFrameReady(Action? callback)
    {
        lock (_notificationGate)
        {
            _frameReady = callback;
            if (Volatile.Read(ref _frame) is not null)
            {
                callback?.Invoke();
            }
        }
    }

    /// <summary>
    /// Enqueues an ordered command without blocking; false means it was not accepted.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether the command was accepted.</returns>
    public bool TrySend(DesktopCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_commandGate)
        {
            bool accepted = _accepting && !_stopping.IsCancellationRequested && _commands.Writer.TryWrite(command);
            if (accepted)
            {
                Invalidate();
            }
            return accepted;
        }
    }

    /// <summary>
    /// Requests detach without blocking the native UI thread.
    /// </summary>
    public void Close()
    {
        SetFrameReady(null);
        _ = _commands.Writer.TryComplete();
        _stopping.Cancel();
    }

    /// <summary>
    /// Detaches and waits for all local resources to be released.
    /// </summary>
    /// <returns>The cleanup task.</returns>
    public async ValueTask DisposeAsync()
    {
        Close();
        await Completion.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(string runtimeDirectory, string executablePath)
    {
        int attempts = 0;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    await RunConnectionAsync(runtimeDirectory, executablePath, attempts == 0).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or ProtocolException
                    or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    lock (_commandGate)
                    {
                        _accepting = false;
                        while (_commands.Reader.TryRead(out _))
                        {
                            // Uncertain input is discarded, never replayed after reconnecting.
                        }
                    }

                    DesktopFrame stale = _lastFrame ?? new DesktopFrame(false, "Weft", null, null, [], [], [], null);
                    Publish(stale with { Connected = false, Error = "Reconnecting… " + exception.Message });
                    ClientLog.Debug(exception.ToString());
                }
                finally
                {
                    await ReleaseTerminalsAsync().ConfigureAwait(false);
                }

                attempts++;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, attempts)), _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            ClientLog.Debug("Desktop client detached.");
        }
        finally
        {
            _ = _commands.Writer.TryComplete();
        }
    }

    private async Task RunConnectionAsync(string runtimeDirectory, string executablePath, bool initial)
    {
        CancellationToken token = _stopping.Token;
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(token);
        connecting.CancelAfter(TimeSpan.FromSeconds(15));
        ControlClient control = await ConnectAsync(runtimeDirectory, executablePath, initial, connecting.Token).ConfigureAwait(false);
        await using (control.ConfigureAwait(false))
        {
            if (control.Hello.Protocol != ProtocolVersion.Current)
            {
                throw new InvalidOperationException("The running server uses a different protocol. Finish running work before explicitly restarting the server with this app's version.");
            }
            control.EventReceived += Invalidate;
            IReadOnlyList<SessionInfo> sessions = (await control.ListSessionsAsync(connecting.Token).ConfigureAwait(false)).Sessions;
            SessionMirror? mirror = null;
            long snapshotSequence = 0;
            if (initial || sessions.Any(session => session.Id == _sessionId))
            {
                SessionAttachResult attached = await control.AttachAsync(new SessionAttachParams
                {
                    Target = _sessionId,
                    Width = _width,
                    Height = _height,
                    Name = "Weft desktop"
                }, connecting.Token).ConfigureAwait(false);
                mirror = new SessionMirror(attached);
                _sessionId = attached.Session.Id;
                snapshotSequence = attached.Seq;
                sessions = (await control.ListSessionsAsync(connecting.Token).ConfigureAwait(false)).Sessions;
            }
            _ = await control.SubscribeAsync(snapshotSequence == 0 ? null : snapshotSequence, connecting.Token).ConfigureAwait(false);
            string? error = null;
            lock (_commandGate)
            {
                _accepting = true;
            }
            Invalidate();
            do
            {
                // Limit each batch so sustained input cannot starve output or cancellation.
                for (int count = 0; count < 64 && _commands.Reader.TryRead(out DesktopCommand? command); count++)
                {
                    try
                    {
                        if (command.Operation is "session" or "newSession")
                        {
                            string? sessionId = command.Target;
                            if (command.Operation == "newSession")
                            {
                                SessionInfo created = await control.CreateSessionAsync(new SessionCreateParams
                                {
                                    Name = command.Text,
                                    Command = _shellCommand
                                }, token).ConfigureAwait(false);
                                sessionId = created.Id;
                            }

                            ArgumentException.ThrowIfNullOrEmpty(sessionId);
                            SessionAttachResult next = await control.AttachAsync(new SessionAttachParams
                            {
                                Target = sessionId,
                                Width = _width,
                                Height = _height,
                                Name = "Weft desktop"
                            }, token).ConfigureAwait(false);
                            if (mirror is { Closed: false })
                            {
                                _ = await control.DetachAsync(mirror.Client.Id, token).ConfigureAwait(false);
                            }
                            mirror = new SessionMirror(next);
                            _sessionId = next.Session.Id;
                            snapshotSequence = next.Seq;
                            sessions = (await control.ListSessionsAsync(token).ConfigureAwait(false)).Sessions;
                        }
                        else if (command.Operation == "sessions")
                        {
                            sessions = (await control.ListSessionsAsync(token).ConfigureAwait(false)).Sessions;
                        }
                        else if (mirror is { Closed: false })
                        {
                            await ExecuteAsync(control, mirror, command, token).ConfigureAwait(false);
                            if (command.Operation == "resize")
                            {
                                _width = command.Width;
                                _height = command.Height;
                            }
                            else if (command.Operation == "closeSession"
                                && (command.Target is null || string.Equals(command.Target, mirror.Session.Id, StringComparison.Ordinal)))
                            {
                                // The block sockets close before the session event necessarily arrives.
                                // A successful close reply is already authoritative for this attachment.
                                mirror = null;
                                _sessionId = null;
                                await ReleaseTerminalsAsync().ConfigureAwait(false);
                                sessions = (await control.ListSessionsAsync(token).ConfigureAwait(false)).Sessions;
                            }
                        }
                        else if (command.Operation == "resize")
                        {
                            ValidateSize(command.Width, command.Height);
                            _width = command.Width;
                            _height = command.Height;
                        }
                        error = null;
                    }
                    catch (Exception exception) when (exception is ProtocolException or ArgumentException)
                    {
                        error = exception.Message;
                    }

                    Invalidate();
                }

                if (_commands.Reader.TryPeek(out _))
                {
                    Invalidate();
                }

                while (control.Events.TryRead(out ProtocolMessage? message))
                {
                    if (message.Seq > snapshotSequence)
                    {
                        _ = mirror?.Apply(message);
                    }

                    if (message.Event is ProtocolEvents.SessionCreated or ProtocolEvents.SessionRenamed or ProtocolEvents.SessionClosed)
                    {
                        sessions = (await control.ListSessionsAsync(token).ConfigureAwait(false)).Sessions;
                    }
                    Invalidate();
                }

                if (control.Closed.IsCompleted || control.Events.Completion.IsCompleted)
                {
                    throw new IOException("The server connection closed.");
                }

                if (mirror is null || mirror.Closed)
                {
                    _sessionId = null;
                    await ReleaseTerminalsAsync().ConfigureAwait(false);
                    if (Interlocked.Exchange(ref _dirty, 0) != 0)
                    {
                        Publish(new DesktopFrame(true, "Weft", null, null, [], [], sessions, null));
                    }
                    continue;
                }

                await SynchronizeAsync(mirror).ConfigureAwait(false);
                string? terminalError = _terminals.Values.Select(terminal => terminal.Error).FirstOrDefault(value => value is not null);
                if (terminalError is not null && !string.Equals(error, terminalError, StringComparison.Ordinal))
                {
                    error = terminalError;
                    Invalidate();
                }

                if (Interlocked.Exchange(ref _dirty, 0) != 0)
                {
                    _frameStarted = Stopwatch.GetTimestamp();
                    List<DesktopBlockFrame> blocks = [];
                    foreach (BlockPlacement placement in mirror.Layout.Tiled.Concat(mirror.Layout.Floating)
                        .Where(placement => _terminals.ContainsKey(placement.Id) && mirror.FindBlock(placement.Id) is not null))
                    {
                        blocks.Add(_terminals[placement.Id].Capture(mirror.FindBlock(placement.Id)!, placement, mirror.Layout.FrameSize,
                            string.Equals(placement.Id, mirror.ActiveTab?.ActiveBlock, StringComparison.Ordinal)));
                    }

                    Publish(new DesktopFrame(true, mirror.Session.Name, mirror.Session.ActiveTab, error,
                        mirror.Tabs, blocks, sessions, mirror.Session.Id));
                }
            }
            while (await WaitForChangeAsync(token).ConfigureAwait(false));
        }
    }

    private static async Task<ControlClient> ConnectAsync(string runtimeDirectory, string executablePath, bool initial, CancellationToken token)
    {
        if (initial)
        {
            ClientLog.Debug("Connecting the desktop window.");
            return await ServerLauncher.ConnectOrStartAsync(runtimeDirectory, executablePath, token).ConfigureAwait(false);
        }

        return await ControlClient.ConnectAsync(WeftPaths.ControlSocketPath(runtimeDirectory), token).ConfigureAwait(false);
    }

    private void Publish(DesktopFrame frame)
    {
        _lastFrame = frame;
        _ = Interlocked.Exchange(ref _frame, frame);
        lock (_notificationGate)
        {
            _frameReady?.Invoke();
        }
    }

    private async Task<bool> WaitForChangeAsync(CancellationToken token)
    {
        _ = await _wake.Reader.ReadAsync(token).ConfigureAwait(false);
        // Leave headroom for a 60 Hz producer whose completed frames arrive between display ticks.
        // Native views coalesce presentation; idle terminals still wait for an actual change.
        TimeSpan remaining = TimeSpan.FromSeconds(1d / 120) - Stopwatch.GetElapsedTime(_frameStarted);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, token).ConfigureAwait(false);
        }
        while (_wake.Reader.TryRead(out _))
        {
            // Coalesce wakeups while retaining all terminal updates and ordered commands.
        }
        return true;
    }

    private async Task ReleaseTerminalsAsync()
    {
        foreach (DesktopTerminal terminal in _terminals.Values)
        {
            await terminal.DisposeAsync().ConfigureAwait(false);
        }

        _terminals.Clear();
    }

    private async Task SynchronizeAsync(SessionMirror mirror)
    {
        var visible = mirror.Layout.Tiled.Concat(mirror.Layout.Floating).Select(placement => placement.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _terminals.Keys.Where(id => !visible.Contains(id)
            || (_terminals[id].Error is not null && mirror.FindBlock(id)?.State == BlockState.Running)).ToArray())
        {
            DesktopTerminal terminal = _terminals[id];
            _ = _terminals.Remove(id);
            await terminal.DisposeAsync().ConfigureAwait(false);
        }

        foreach (string id in visible.Where(id => !_terminals.ContainsKey(id) && mirror.FindBlock(id) is not null))
        {
            _terminals.Add(id, new DesktopTerminal(mirror.FindBlock(id)!, Invalidate));
            Invalidate();
        }
    }

    private async Task ExecuteAsync(ControlClient control, SessionMirror mirror, DesktopCommand command, CancellationToken token)
    {
        string? target = command.Target ?? mirror.ActiveTab?.ActiveBlock;
        switch (command.Operation)
        {
            case "reconnect":
                throw new IOException("Reattaching to the running session.");
            case "select":
                if (target is not null && _terminals.TryGetValue(target, out DesktopTerminal? selecting))
                {
                    selecting.Select(command.Text ?? "cell", command.X, command.Y);
                }
                break;
            case "scroll":
            case "scrollTo":
                if (target is not null && _terminals.TryGetValue(target, out DesktopTerminal? scrolling))
                {
                    scrolling.Scroll(command.Y, command.Operation == "scroll");
                }
                break;
            case "live":
                if (target is not null && _terminals.TryGetValue(target, out DesktopTerminal? following))
                {
                    following.Resume();
                }
                break;
            case "mouse":
                if (target is not null && _terminals.TryGetValue(target, out DesktopTerminal? mouse))
                {
                    await mouse.MouseAsync(command, token).ConfigureAwait(false);
                }
                break;
            case "find":
                if (target is not null && _terminals.TryGetValue(target, out DesktopTerminal? searching))
                {
                    searching.Find(command.Text ?? string.Empty, command.Y);
                }
                break;
            case "text":
                _ = await control.TypeAsync(new BlockTextParams { Target = target, Text = command.Text ?? string.Empty }, token).ConfigureAwait(false);
                break;
            case "key":
                _ = await control.SendKeysAsync(new BlockSendKeysParams { Target = target, Keys = [command.Text ?? string.Empty] }, token).ConfigureAwait(false);
                break;
            case "paste":
                _ = await control.PasteAsync(new BlockTextParams { Target = target, Text = command.Text ?? string.Empty }, token).ConfigureAwait(false);
                break;
            case "resize":
                ValidateSize(command.Width, command.Height);
                _ = await control.SetSizeAsync(new SessionSetSizeParams { Client = mirror.Client.Id, Width = command.Width, Height = command.Height }, token).ConfigureAwait(false);
                break;
            case "newTab":
                TabInfo tab = await control.CreateTabAsync(new TabCreateParams { Target = mirror.Session.Id, Command = _shellCommand }, token).ConfigureAwait(false);
                _ = await control.SelectTabAsync(tab.Id, token).ConfigureAwait(false);
                break;
            case "tab":
                ArgumentException.ThrowIfNullOrEmpty(command.Target);
                _ = await control.SelectTabAsync(command.Target, token).ConfigureAwait(false);
                break;
            case "focus":
                _ = await control.FocusAsync(new BlockFocusParams { Target = target }, token).ConfigureAwait(false);
                break;
            case "splitRight":
            case "splitBelow":
                _ = await control.SplitAsync(new BlockSplitParams
                {
                    Target = target,
                    Command = _shellCommand,
                    Orientation = command.Operation == "splitRight" ? SplitOrientation.LeftRight : SplitOrientation.TopBottom
                }, token).ConfigureAwait(false);
                break;
            case "zoom":
                _ = await control.ZoomAsync(new BlockZoomParams { Target = target }, token).ConfigureAwait(false);
                break;
            case "renameSession":
                _ = await control.RenameSessionAsync(new SessionRenameParams { Target = command.Target ?? mirror.Session.Id, Name = command.Text ?? string.Empty }, token).ConfigureAwait(false);
                break;
            case "renameTab":
                _ = await control.RenameTabAsync(new TabRenameParams { Target = command.Target ?? mirror.Session.ActiveTab, Name = command.Text ?? string.Empty }, token).ConfigureAwait(false);
                break;
            case "renameBlock":
                _ = await control.RenameBlockAsync(new BlockRenameParams { Target = target, Title = command.Text }, token).ConfigureAwait(false);
                break;
            case "closeSession":
                _ = await control.CloseSessionAsync(command.Target ?? mirror.Session.Id, token).ConfigureAwait(false);
                break;
            case "closeTab":
                _ = await control.CloseTabAsync(command.Target ?? mirror.Session.ActiveTab, token).ConfigureAwait(false);
                break;
            case "closeBlock":
                _ = await control.CloseBlockAsync(target, token).ConfigureAwait(false);
                break;
            case "resizePane":
                if (!Enum.TryParse(command.Text, ignoreCase: true, out LayoutDirection direction) || !Enum.IsDefined(direction))
                {
                    throw new ArgumentException("Unknown pane edge.", nameof(command));
                }
                _ = await control.ResizeAsync(new LayoutResizeParams { Target = target, Direction = direction, Amount = Math.Clamp(command.X, -500, 500) }, token).ConfigureAwait(false);
                break;
            case "float":
                _ = await control.FloatAsync(new BlockFloatParams { Target = target }, token).ConfigureAwait(false);
                break;
            case "tile":
                _ = await control.TileAsync(target, token).ConfigureAwait(false);
                break;
            case "layout":
                _ = await control.PresetAsync(new LayoutPresetParams { Target = mirror.Session.ActiveTab }, token).ConfigureAwait(false);
                break;
            case "sync":
                _ = await control.SyncTabAsync(new TabSyncParams { Target = mirror.Session.ActiveTab, Enabled = mirror.ActiveTab?.Synchronized != true }, token).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException("Unknown desktop operation.", nameof(command));
        }
    }

    private void Invalidate()
    {
        _ = Interlocked.Exchange(ref _dirty, 1);
        _ = _wake.Writer.TryWrite(true);
    }

    private static void ValidateSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 500);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 300);
    }
}
