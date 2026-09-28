using Hex1b;
using Hex1b.Input;
using Hex1b.Nodes;
using Hex1b.Theming;
using Hex1b.Widgets;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// The interactive client: renders a session's tabs and blocks, routes input, and drives layout commands.
/// </summary>
public sealed class AttachApp
{
    private static readonly Hex1bColor s_panel = Hex1bColor.FromRgb(24, 24, 28);
    private static readonly Hex1bColor s_terminal = Hex1bColor.FromRgb(0, 0, 0);
    private readonly AttachOptions _options;
    private readonly BindingTable _bindings;
    private readonly Dictionary<string, BlockView> _views = [with(StringComparer.Ordinal)];
    private readonly Lock _gate = new();
    // Session names are trimmed by the server, so an entry that starts with a space can never collide with one.
    private const string NewSessionEntry = " + new session";
    private ControlClient? _control;
    private RootContext? _root;
    private SessionMirror? _mirror;
    private PopupStack? _helpPopups;
    private const int ActivationIntervalMs = 1000;
    private long _lastActivation;
    private bool _locked;
    private bool _focusedOnce;
    private int _reportedWidth;
    private int _reportedHeight;

    /// <summary>
    /// Initializes the client.
    /// </summary>
    /// <param name="options">The attach options.</param>
    public AttachApp(AttachOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _bindings = BindingTable.Build(options.Config);
        if (_bindings.Warnings.Count > 0)
        {
            Status = _bindings.Warnings[0];
        }
    }

    /// <summary>
    /// Gets a message explaining why the client stopped, if it stopped on its own.
    /// </summary>
    public string? ExitMessage { get; private set; }

    /// <summary>
    /// Gets the session the user asked to switch to, when the client stopped for a switch.
    /// </summary>
    public string? SwitchTarget { get; private set; }

    /// <summary>
    /// Gets the latest error message from a failed action, for display.
    /// </summary>
    public string? Status { get; private set; }

    /// <summary>
    /// Gets the outer terminal once running, for automation in tests.
    /// </summary>
    internal Hex1bTerminal? Terminal { get; private set; }

    /// <summary>
    /// Gets the Hex1b application once running, for diagnostics in tests.
    /// </summary>
    internal Hex1bApp? App { get; private set; }

    /// <summary>
    /// Attaches, runs until detached, and cleans up.
    /// </summary>
    /// <param name="cancellationToken">Stops the client.</param>
    /// <returns>A task that completes after detaching.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ControlClient control = await ControlClient.ConnectAsync(_options.SocketPath, cancellationToken).ConfigureAwait(false);
        await using (control.ConfigureAwait(false))
        {
            _control = control;
            (int width, int height) = HostSize.Read(_options.Headless);
            _reportedWidth = width;
            _reportedHeight = height - 1;
            SessionAttachResult attached = await control.AttachAsync(new SessionAttachParams
            {
                Target = _options.Target,
                Width = _reportedWidth,
                Height = _reportedHeight,
                ReadOnly = _options.ReadOnly,
                Name = _options.Name
            }, cancellationToken).ConfigureAwait(false);
            _mirror = new SessionMirror(attached);
            foreach (BlockInfo block in attached.Blocks)
            {
                EnsureView(block);
            }

            ControlClient events = await ControlClient.ConnectAsync(_options.SocketPath, cancellationToken).ConfigureAwait(false);
            await using (events.ConfigureAwait(false))
            {
                _ = await events.SubscribeAsync(attached.Seq, cancellationToken).ConfigureAwait(false);
                Task pump = PumpEventsAsync(events, stopping.Token);
                try
                {
                    await RunTerminalAsync(stopping.Token).ConfigureAwait(false);
                }
                finally
                {
                    await stopping.CancelAsync().ConfigureAwait(false);
                    while (!pump.IsCompleted)
                    {
                        await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
                    }

                    await DisposeViewsAsync().ConfigureAwait(false);
                    if (!_mirror.Closed)
                    {
                        try
                        {
                            using var detachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            _ = await control.DetachAsync(attached.Client.Id, detachTimeout.Token).ConfigureAwait(false);
                        }
                        catch (ProtocolException)
                        {
                            ClientLog.Debug("EnsureView ignored ProtocolException.");
                        }
                        catch (OperationCanceledException)
                        {
                            ClientLog.Debug("EnsureView ignored OperationCanceledException.");
                        }
                    }
                }
            }
        }
    }

    private static Hex1bTheme ThemeFor(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "OCEAN" => Hex1bThemes.Ocean,
            "HIGH-CONTRAST" or "HIGHCONTRAST" => Hex1bThemes.HighContrast,
            "SUNSET" => Hex1bThemes.Sunset,
            _ => Hex1bThemes.Default
        };
    }

    private async Task RunTerminalAsync(CancellationToken cancellationToken)
    {
        Hex1bTheme theme = ThemeFor(_options.Config.Theme);
        Hex1bTerminalBuilder builder = Hex1bTerminal.CreateBuilder()
            .WithMouse()
            .AddWorkloadFilter(new InputActivityFilter(OnUserInput))
            .WithHex1bApp(options => options.Theme = theme, app =>
            {
                App = app;
                return Render;
            });
        if (_options.Headless is { } headless)
        {
            builder = builder.WithHeadless().WithDimensions(headless.Width, headless.Height);
        }

        Hex1bTerminal terminal = builder.Build();
        await using (terminal.ConfigureAwait(false))
        {
            Terminal = terminal;
            try
            {
                _ = await terminal.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ClientLog.Debug("RunTerminalAsync ignored OperationCanceledException.");
            }
            finally
            {
                Terminal = null;
            }
        }
    }

    private async Task PumpEventsAsync(ControlClient events, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ProtocolMessage message in events.Events.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_mirror is not { } mirror)
                {
                    continue;
                }

                if (message.Event is ProtocolEvents.SessionResized or ProtocolEvents.ClientAttached)
                {
                    // Another client may have become the size authority; the next input here must reclaim it at once.
                    Volatile.Write(ref _lastActivation, 0);
                }

                (BlockInfo Block, bool Created)? change = mirror.Apply(message);
                if (change is { } affected)
                {
                    if (string.Equals(message.Event, ProtocolEvents.BlockClosed, StringComparison.Ordinal))
                    {
                        await RemoveViewAsync(affected.Block.Id).ConfigureAwait(false);
                    }
                    else if (affected.Created)
                    {
                        EnsureView(affected.Block);
                    }

                    if (string.Equals(message.Event, ProtocolEvents.BlockFocused, StringComparison.Ordinal) && affected.Block.Active)
                    {
                        FocusView(affected.Block.Id);
                    }
                }

                if (mirror.Closed)
                {
                    ExitMessage = "The session was closed.";
                    App?.RequestStop();
                }

                // Output flows through each block's own stream; only a new activity marker needs a redraw.
                if (!string.Equals(message.Event, ProtocolEvents.BlockOutput, StringComparison.Ordinal) || mirror.ActivityChanged)
                {
                    App?.Invalidate();
                }
            }
        }
        catch (OperationCanceledException)
        {
            ClientLog.Debug("FocusView ignored OperationCanceledException.");
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            ExitMessage ??= "Lost the connection to the server.";
            App?.RequestStop();
        }
    }

    private void EnsureView(BlockInfo block)
    {
        lock (_gate)
        {
            if (_views.ContainsKey(block.Id))
            {
                return;
            }

            var view = BlockView.Start(block.Id, block.SocketPath, block.Width, block.Height, _options.Name, () => App?.Invalidate());
            _views[block.Id] = view;
            if (!_options.ReadOnly)
            {
                view.Handle.TextCopied += text => Fire(client => client.SetPasteAsync(text, CancellationToken.None));
            }
        }
    }

    private async Task RemoveViewAsync(string id)
    {
        BlockView? view = TakeView(id);
        if (view is not null)
        {
            await view.DisposeAsync().ConfigureAwait(false);
        }
    }

    private BlockView? TakeView(string id)
    {
        lock (_gate)
        {
            if (_views.TryGetValue(id, out BlockView? view))
            {
                _ = _views.Remove(id);
                return view;
            }

            return null;
        }
    }

    private async Task DisposeViewsAsync()
    {
        List<BlockView> views;
        lock (_gate)
        {
            views = [.. _views.Values];
            _views.Clear();
        }

        foreach (BlockView view in views)
        {
            await view.DisposeAsync().ConfigureAwait(false);
        }
    }

    private BlockView? ViewFor(string? id)
    {
        if (id is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _views.GetValueOrDefault(id);
        }
    }

    private void OnUserInput()
    {
        // Any input marks this client as the most recently active one, which the latest size policy follows.
        // Actions activate through Execute as well; this covers ordinary typing that goes straight to a block.
        // A read-only viewer never becomes the size authority, whatever it presses.
        if (_options.ReadOnly)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastActivation) < ActivationIntervalMs)
        {
            return;
        }

        Volatile.Write(ref _lastActivation, now);
        if (_mirror is { } mirror)
        {
            Fire(client => client.ActivateAsync(mirror.Client.Id, CancellationToken.None));
        }
    }

    private void FocusView(string id)
    {
        if (_options.ReadOnly)
        {
            return;
        }

        BlockView? view = ViewFor(id);
        if (view is not null)
        {
            App?.RequestFocus(node => node is TerminalNode terminal && terminal.Handle == view.Handle);
        }
    }

    /// <summary>
    /// Describes focus, views, and mirror state for test diagnostics.
    /// </summary>
    /// <returns>A single line of state.</returns>
    internal string DebugState()
    {
        string views;
        lock (_gate)
        {
            views = string.Join(",", _views.Keys);
        }

        return "focusedBlock=" + (FocusedBlockId() ?? "none") + " views=[" + views + "] activeBlock=" + (_mirror?.ActiveTab?.ActiveBlock ?? "none") + " control=" + (_control is null ? "none" : "ok");
    }

    private string? FocusedBlockId()
    {
        if (App?.FocusedNode is TerminalNode terminal)
        {
            lock (_gate)
            {
                BlockView? match = _views.Values.FirstOrDefault(view => view.Handle == terminal.Handle);
                if (match is not null)
                {
                    return match.Id;
                }
            }
        }

        return _mirror?.ActiveTab?.ActiveBlock;
    }

    private Hex1bWidget Render(RootContext ctx)
    {
        SessionMirror mirror = _mirror!;
        _root = ctx;
        if (!_focusedOnce && mirror.ActiveTab?.ActiveBlock is { } initial && ViewFor(initial) is not null)
        {
            _focusedOnce = true;
            FocusView(initial);
        }

        (int width, int height) = HostSize.Read(_options.Headless);
        int availableWidth = Math.Max(1, width);
        int availableHeight = Math.Max(1, height - 1);
        ReportSize(availableWidth, availableHeight);

        LayoutInfo layout = mirror.Layout;
        ZStackWidget content = ctx.ZStack(z =>
        {
            Hex1bWidget tiled = RenderLayout(z, mirror, layout);
            bool fits = layout.Width <= availableWidth && layout.Height <= availableHeight;
            List<Hex1bWidget> layers = [z.Align(fits ? Alignment.Center : Alignment.TopLeft, tiled).Fill()];

            // Floating blocks keep their server coordinates relative to the session, so when the session is
            // centered in a larger viewport they move with it. A zoomed block already fills the session.
            int offsetX = fits ? (availableWidth - layout.Width) / 2 : 0;
            int offsetY = fits ? (availableHeight - layout.Height) / 2 : 0;
            foreach ((BlockPlacement placement, BlockInfo block) in layout.Floating
                .Where(placement => !string.Equals(placement.Id, layout.Zoomed, StringComparison.Ordinal))
                .Select(placement => (placement, mirror.FindBlock(placement.Id)))
                .Where(pair => pair.Item2 is not null)
                .Select(pair => (pair.placement, pair.Item2!)))
            {
                layers.Add(z.Float(RenderBlock(z, block, placement.Width, placement.Height, layout.FrameSize > 0, mirror)).Absolute(placement.X + offsetX, placement.Y + offsetY));
            }

            return [.. layers];
        });

        Hex1bWidget body = new BackgroundPanelWidget(s_panel, ctx.VStack(v => [content.Fill(), RenderInfoBar(v, mirror, layout, Status, _locked, _bindings)]));
        return body.InputBindings(bindings => RegisterBindings(bindings, mirror));
    }

    private Hex1bWidget RenderLayout<TParent>(WidgetContext<TParent> ctx, SessionMirror mirror, LayoutInfo layout)
        where TParent : Hex1bWidget
    {
        return layout.Zoomed is { } zoomed && mirror.FindBlock(zoomed) is { } zoomedBlock
            ? RenderBlock(ctx, zoomedBlock, layout.Width, layout.Height, layout.FrameSize > 0, mirror)
            : !LayoutSerializer.TryParse(layout.Serialized, out LayoutCell? root) || root is null
            ? ctx.Center(ctx.Text(layout.Floating.Count > 0 ? string.Empty : "(no blocks)")).FixedWidth(layout.Width).FixedHeight(layout.Height)
            : RenderCell(ctx, root, layout, mirror);
    }

    private Hex1bWidget RenderCell<TParent>(WidgetContext<TParent> ctx, LayoutCell cell, LayoutInfo layout, SessionMirror mirror)
        where TParent : Hex1bWidget
    {
        if (cell.IsLeaf)
        {
            BlockInfo? block = cell.Block is { } id ? mirror.FindBlock(id.ToString()) : null;
            return block is null
                ? ctx.Text(string.Empty).FixedWidth(cell.Width).FixedHeight(cell.Height)
                : RenderBlock(ctx, block, cell.Width, cell.Height, layout.FrameSize > 0, mirror);
        }

        int spacing = layout.FrameSize > 0 ? 0 : 1;
        return cell.Orientation == SplitOrientation.LeftRight
            ? ctx.HStack(h =>
            {
                List<Hex1bWidget> children = [];
                for (int i = 0; i < cell.Children.Count; i++)
                {
                    if (i > 0 && spacing > 0)
                    {
                        children.Add(h.Text("│").FixedWidth(1).FixedHeight(cell.Height));
                    }

                    children.Add(RenderCell(h, cell.Children[i], layout, mirror));
                }

                return [.. children];
            }).FixedWidth(cell.Width).FixedHeight(cell.Height)
            : ctx.VStack(v =>
        {
            List<Hex1bWidget> children = [];
            for (int i = 0; i < cell.Children.Count; i++)
            {
                if (i > 0 && spacing > 0)
                {
                    children.Add(v.Text(new string('─', cell.Width)).FixedWidth(cell.Width).FixedHeight(1));
                }

                children.Add(RenderCell(v, cell.Children[i], layout, mirror));
            }

            return [.. children];
        }).FixedWidth(cell.Width).FixedHeight(cell.Height);
    }

    private Hex1bWidget RenderBlock<TParent>(WidgetContext<TParent> ctx, BlockInfo block, int width, int height, bool framed, SessionMirror mirror)
        where TParent : Hex1bWidget
    {
        BlockView? view = ViewFor(block.Id);
        int innerWidth = Math.Max(1, framed ? width - 2 : width);
        int innerHeight = Math.Max(1, framed ? height - 2 : height);
        Hex1bWidget inner;
        if (view is null || view.Disconnected)
        {
            inner = ctx.Center(ctx.Text(view is null ? "connecting" : "disconnected")).FixedWidth(innerWidth).FixedHeight(innerHeight);
        }
        else
        {
            view.Resize(innerWidth, innerHeight);
            inner = ctx.Terminal(view.Handle).Background(s_terminal).CopyModeBindings().FixedWidth(innerWidth).FixedHeight(innerHeight);
        }

        if (!framed)
        {
            return inner;
        }

        bool active = string.Equals(mirror.ActiveTab?.ActiveBlock, block.Id, StringComparison.Ordinal);
        string marker = block.State == BlockState.Exited ? " [exited " + (block.ExitCode ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" : string.Empty;
        string title = " " + (active ? "● " : string.Empty) + block.Title + marker + (block.Floating ? " ◈" : string.Empty) + " ";
        return ctx.Border(inner).Title(title).FixedWidth(width).FixedHeight(height);
    }

    private InfoBarWidget RenderInfoBar<TParent>(WidgetContext<TParent> ctx, SessionMirror mirror, LayoutInfo layout, string? status, bool locked, BindingTable bindings)
        where TParent : Hex1bWidget
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        string tabText = string.Join("  ", tabs.Select(tab =>
            (tab.Active ? "[" : string.Empty) + tab.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + tab.Name + (mirror.HasActivity(tab.Id) ? "*" : string.Empty) + (tab.Active ? "]" : string.Empty)));
        string size = layout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×" + layout.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string hint = locked ? bindings.ChordFor(ClientActions.Lock) + " unlock" : string.Empty;
        string helpChord = locked ? string.Empty : bindings.ChordFor(ClientActions.Palette);
        string helpLabel = string.IsNullOrEmpty(helpChord) ? "Help" : helpChord + " Help";
        if (mirror.ActiveTab is { Synchronized: true })
        {
            size = "SYNC " + size;
        }
        return ctx.InfoBar(s =>
        [
            s.Section(" " + mirror.Session.Name + " "),
            s.Section(tabText),
            s.Spacer(),
            s.Section(locked ? "LOCKED" : status ?? string.Empty),
            s.Section(size),
            s.Section(hint),
            s.Section(b => b.Button(helpLabel).OnClick(args => Execute(ClientActions.Palette, args.Context, mirror))),
            s.Section(b => b.Button("Exit weft").OnClick(args => Execute(ClientActions.Detach, args.Context, mirror)))
        ]).Divider(" ");
    }

    private void RegisterBindings(InputBindingsBuilder bindings, SessionMirror mirror)
    {
        if (_helpPopups is { HasPopups: true })
        {
            return;
        }

        HashSet<KeyStroke> claimed = [];
        foreach ((KeyChord chord, string action) in _bindings.Bindings)
        {
            if (_locked && !string.Equals(action, ClientActions.Lock, StringComparison.Ordinal))
            {
                continue;
            }

            if (_options.ReadOnly && !ClientActions.IsReadOnlySafe(action))
            {
                continue;
            }

            string captured = action;
            _ = claimed.Add(chord.Steps[0]);
            BuildShortcut(bindings, chord)?.OverridesCapture().Action(context => Execute(captured, context, mirror), captured);
        }

        if (_options.ReadOnly)
        {
            SwallowUnclaimed(bindings, claimed);
        }
    }

    private static void SwallowUnclaimed(InputBindingsBuilder bindings, HashSet<KeyStroke> claimed)
    {
        // A read-only client never forwards keys, even if a click focused a block: every stroke it can name is
        // bound to nothing except the configured bindings for harmless client actions.
        foreach (KeyStroke stroke in KeyMap.AllStrokes().Where(stroke => !claimed.Contains(stroke)))
        {
            BuildShortcut(bindings, new KeyChord([stroke]))?.OverridesCapture().Action(_ => { }, "Read-only");
        }
    }

    private static KeyStepBuilder? BuildShortcut(InputBindingsBuilder bindings, KeyChord chord)
    {
        KeyStroke stroke = chord.Steps[0];
        if (KeyMap.ToHex1bKey(stroke.Key) is not { } key)
        {
            return null;
        }

        KeyStepBuilder builder = bindings.Key(key);
        if (stroke.Modifiers.HasFlag(KeyModifiers.Control))
        {
            builder = builder.Ctrl();
        }

        if (stroke.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            builder = builder.Alt();
        }

        if (stroke.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            builder = builder.Shift();
        }

        return builder;
    }

    private void Execute(string action, InputBindingActionContext context, SessionMirror mirror)
    {
        // The palette and pickers route here too, so the read-only allowlist is enforced once, in one place.
        if (_options.ReadOnly && !ClientActions.IsReadOnlySafe(action))
        {
            Status = "read-only";
            App?.Invalidate();
            return;
        }

        if (!_options.ReadOnly)
        {
            Fire(client => client.ActivateAsync(mirror.Client.Id, CancellationToken.None));
        }

        switch (action)
        {
            case ClientActions.Detach:
                ExitMessage = null;
                App?.RequestStop();
                break;
            case ClientActions.TabNew:
                Fire(client => client.CreateTabAsync(new TabCreateParams { Target = mirror.Session.Id }, CancellationToken.None));
                break;
            case ClientActions.TabNext:
                SelectTabRelative(mirror, 1);
                break;
            case ClientActions.TabPrevious:
                SelectTabRelative(mirror, -1);
                break;
            case ClientActions.TabClose:
                if (mirror.ActiveTab is { } closing)
                {
                    Fire(client => client.CloseTabAsync(closing.Id, CancellationToken.None));
                }

                break;
            case ClientActions.TabRename:
                if (mirror.ActiveTab is { } renaming)
                {
                    Prompt(context, "Tab name", renaming.Name, name => Fire(client => client.RenameTabAsync(new TabRenameParams { Target = renaming.Id, Name = name }, CancellationToken.None)));
                }

                break;
            case ClientActions.SplitRight:
                Split(SplitOrientation.LeftRight);
                break;
            case ClientActions.SplitDown:
                Split(SplitOrientation.TopBottom);
                break;
            case ClientActions.BlockClose:
                WithFocused(id => Fire(client => client.CloseBlockAsync(id, CancellationToken.None)));
                break;
            case ClientActions.BlockZoom:
                WithFocused(id => Fire(client => client.ZoomAsync(new BlockZoomParams { Target = id }, CancellationToken.None)));
                break;
            case ClientActions.BlockFloat:
                WithFocused(id =>
                {
                    bool floating = mirror.FindBlock(id)?.Floating ?? false;
                    Fire(client => floating
                        ? client.TileAsync(id, CancellationToken.None)
                        : client.FloatAsync(new BlockFloatParams { Target = id }, CancellationToken.None));
                });
                break;
            case ClientActions.BlockRename:
                WithFocused(id => Prompt(context, "Block title", mirror.FindBlock(id)?.Title ?? string.Empty, title => Fire(client => client.RenameBlockAsync(new BlockRenameParams { Target = id, Title = title }, CancellationToken.None))));
                break;
            case ClientActions.CopyMode:
                ViewFor(FocusedBlockId())?.Handle.EnterCopyMode();
                App?.Invalidate();
                break;
            case ClientActions.Paste:
                WithFocused(id => Fire(async client =>
                {
                    PasteBuffer buffer = await client.GetPasteAsync(CancellationToken.None).ConfigureAwait(false);
                    return await client.PasteAsync(new BlockTextParams { Target = id, Text = buffer.Text }, CancellationToken.None).ConfigureAwait(false);
                }));
                break;
            case ClientActions.FocusLeft:
                FocusDirection(LayoutDirection.Left);
                break;
            case ClientActions.FocusRight:
                FocusDirection(LayoutDirection.Right);
                break;
            case ClientActions.FocusUp:
                FocusDirection(LayoutDirection.Up);
                break;
            case ClientActions.FocusDown:
                FocusDirection(LayoutDirection.Down);
                break;
            case ClientActions.ResizeLeft:
                Resize(LayoutDirection.Left);
                break;
            case ClientActions.ResizeRight:
                Resize(LayoutDirection.Right);
                break;
            case ClientActions.ResizeUp:
                Resize(LayoutDirection.Up);
                break;
            case ClientActions.ResizeDown:
                Resize(LayoutDirection.Down);
                break;
            case ClientActions.LayoutNext:
                Fire(client => client.PresetAsync(new LayoutPresetParams { Target = mirror.Session.Id }, CancellationToken.None));
                break;
            case ClientActions.SessionPick:
                PickSession(context, mirror);
                break;
            case ClientActions.TabSync:
                if (mirror.ActiveTab is { } syncing)
                {
                    Fire(client => client.SyncTabAsync(new TabSyncParams { Target = syncing.Id }, CancellationToken.None));
                }

                break;
            case ClientActions.SessionRename:
                Prompt(context, "Session name", mirror.Session.Name, name => Fire(client => client.RenameSessionAsync(new SessionRenameParams { Target = mirror.Session.Id, Name = name }, CancellationToken.None)));
                break;
            case ClientActions.TabPick:
                PickTabOrBlock(context, mirror);
                break;
            case ClientActions.Lock:
                if (!_locked && string.IsNullOrEmpty(_bindings.ChordFor(ClientActions.Lock)))
                {
                    // Without a chord to unlock, locking would strand the client.
                    Status = "lock has no key bound";
                    App?.Invalidate();
                    break;
                }

                _locked = !_locked;
                App?.Invalidate();
                break;
            case ClientActions.Palette:
                ShowPalette(context, mirror);
                break;
            case var numbered when ClientActions.TabNumber(numbered) is { } number:
                SelectTabNumber(mirror, number);
                break;
            default:
                break;
        }
    }

    private void Prompt(InputBindingActionContext context, string label, string initial, Action<string> onSubmit)
    {
        PopupStack popups = context.Popups;
        RootContext ctx = _root!;
        _ = popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
        [
            b.VStack(v =>
            [
                v.Text(" " + label + " "),
                v.TextBox(initial).OnSubmit(e =>
                {
                    _ = popups.Pop();
                    onSubmit(e.Text);
                }).FixedWidth(40),
                v.Text(" Enter to apply, Escape to cancel ")
            ])
        ]).Title(" weft ")), popups));
    }

    private static Hex1bWidget Dismissable(Hex1bWidget content, PopupStack popups)
    {
        return content.InputBindings(bindings => bindings.Key(Hex1bKey.Escape).Action(_ => popups.Pop(), "Dismiss"));
    }

    private void ShowPalette(InputBindingActionContext context, SessionMirror mirror)
    {
        if (_helpPopups is { HasPopups: true })
        {
            ClosePalette();
            return;
        }

        context.ReleaseCapture();
        // A mouse click focuses the Help button. Restore the block before opening the popup so
        // dismissing it returns keyboard input to the terminal instead of leaving it on the button.
        if (ViewFor(FocusedBlockId()) is { } view)
        {
            _ = context.FocusWhere(node => node is TerminalNode terminal && terminal.Handle == view.Handle);
        }

        PopupStack popups = context.Popups;
        _helpPopups = popups;
        List<PaletteEntry> entries =
        [
            .. ClientActions.Defaults
                .Where(item => !string.Equals(item.Action, ClientActions.Palette, StringComparison.Ordinal))
                .Where(item => !_options.ReadOnly || ClientActions.IsReadOnlySafe(item.Action))
                .Where(item => !string.Equals(item.Action, ClientActions.Lock, StringComparison.Ordinal) || !string.IsNullOrEmpty(_bindings.ChordFor(ClientActions.Lock)))
                .Select(item => new PaletteEntry(item.Action, _locked && string.Equals(item.Action, ClientActions.Lock, StringComparison.Ordinal) ? "Enable weft shortcuts" : item.Description, _bindings.ChordFor(item.Action)))
        ];
        _ = popups.Push(() =>
        {
            RootContext ctx = _root!;
            (int width, int height) = HostSize.Read(_options.Headless);
            int visibleItems = Math.Min(entries.Count, Math.Clamp(height - 8, 1, 16));
            return ctx.Border(b =>
            [
                b.SelectionPrompt(entries)
                    .Prompt("Search commands")
                    .MaxVisibleItems(visibleItems)
                    .OnSelected(entry =>
                    {
                        _ = popups.Pop();
                        _helpPopups = null;
                        if (FocusedBlockId() is { } id)
                        {
                            FocusView(id);
                        }

                        Execute(entry.Action, context, mirror);
                    }).FixedHeight(visibleItems + 2),
                b.Text(" Type to filter. ↑↓ choose. Enter runs. ").FixedHeight(1),
                b.HStack(h =>
                [
                    h.Text(" Esc closes Help ").FillWidth(),
                    h.Button("[ Close ]").OnClick(_ => ClosePalette())
                ]).FixedHeight(1)
            ]).Title(" Help and commands ")
                .FixedWidth(Math.Max(1, Math.Min(64, width - 4)))
                .FixedHeight(visibleItems + 6)
                .InputBindings(bindings =>
                {
                    // Dialog shortcuts take precedence over input capture and text predictions.
                    // This binding exists only while Help is open; terminal Esc stays untouched.
                    bindings.Key(Hex1bKey.Escape).Global().OverridesCapture().Action(_ => ClosePalette(), "Close help");
                    foreach ((KeyChord chord, _) in _bindings.Bindings.Where(binding => string.Equals(binding.Action, ClientActions.Palette, StringComparison.Ordinal)))
                    {
                        BuildShortcut(bindings, chord)?.Global().OverridesCapture().Action(_ => ClosePalette(), "Close help");
                    }

                    foreach ((KeyChord chord, _) in _bindings.Bindings.Where(binding => string.Equals(binding.Action, ClientActions.Detach, StringComparison.Ordinal)))
                    {
                        BuildShortcut(bindings, chord)?.Global().OverridesCapture().Action(args => Execute(ClientActions.Detach, args, mirror), "Exit weft");
                    }
                });
        }).AsBarrier();
        App?.RequestFocus(node => node is TextBoxNode);
    }

    private void ClosePalette()
    {
        if (_helpPopups is not { } popups)
        {
            return;
        }

        _ = popups.Pop();
        _helpPopups = null;
        if (FocusedBlockId() is { } id)
        {
            FocusView(id);
        }

        App?.Invalidate();
    }

    private void PickSession(InputBindingActionContext context, SessionMirror mirror)
    {
        PopupStack popups = context.Popups;
        RootContext ctx = _root!;
        Fire(async client =>
        {
            SessionListResult result = await client.ListSessionsAsync(CancellationToken.None).ConfigureAwait(false);
            // A read-only viewer may switch between sessions but never create one.
            List<string> names = [.. result.Sessions.Select(session => session.Name)];
            if (!_options.ReadOnly)
            {
                names.Add(NewSessionEntry);
            }

            _ = popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
            [
                b.SelectionPrompt(names).Prompt("session").MaxVisibleItems(12).OnSelected(name =>
                {
                    _ = popups.Pop();
                    if (string.Equals(name, mirror.Session.Name, StringComparison.Ordinal))
                    {
                        return;
                    }

                    SwitchTarget = string.Equals(name, NewSessionEntry, StringComparison.Ordinal) ? string.Empty : name;
                    ExitMessage = null;
                    App?.RequestStop();
                })
            ]).Title(" sessions ")), popups));
            App?.Invalidate();
            return result;
        });
    }

    private void PickTabOrBlock(InputBindingActionContext context, SessionMirror mirror)
    {
        PopupStack popups = context.Popups;
        List<(string Label, string Tab, string? Block)> items = [];
        foreach (TabInfo tab in mirror.Tabs)
        {
            items.Add((tab.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + tab.Name, tab.Id, null));
            foreach (BlockInfo block in mirror.Blocks.Where(block => string.Equals(block.Tab, tab.Id, StringComparison.Ordinal)).OrderBy(block => block.Index))
            {
                items.Add(("    " + block.Id + "  " + block.Title, tab.Id, block.Id));
            }
        }

        List<string> labels = [.. items.Select(item => item.Label)];
        RootContext ctx = _root!;
        _ = popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
        [
            b.SelectionPrompt(labels).Prompt("go to").MaxVisibleItems(16).OnSelected(label =>
            {
                _ = popups.Pop();
                (_, string tab, string? block) = items.First(item => string.Equals(item.Label, label, StringComparison.Ordinal));
                Fire(async client =>
                {
                    _ = await client.SelectTabAsync(tab, CancellationToken.None).ConfigureAwait(false);
                    if (block is not null)
                    {
                        BlockInfo focused = await client.FocusAsync(new BlockFocusParams { Target = block }, CancellationToken.None).ConfigureAwait(false);
                        FocusView(focused.Id);
                    }

                    return EmptyResult.Instance;
                });
            })
        ]).Title(" tabs and blocks ")), popups));
    }

    private void SelectTabRelative(SessionMirror mirror, int delta)
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        if (tabs.Count == 0)
        {
            return;
        }

        int current = tabs.ToList().FindIndex(tab => tab.Active);
        int next = ((current < 0 ? 0 : current) + delta + tabs.Count) % tabs.Count;
        Fire(client => client.SelectTabAsync(tabs[next].Id, CancellationToken.None));
    }

    private void Split(SplitOrientation orientation)
    {
        WithFocused(id =>
        {
            ClientLog.Debug("split " + id + " " + orientation);
            Fire(client => client.SplitAsync(new BlockSplitParams { Target = id, Orientation = orientation }, CancellationToken.None));
        });
    }

    private void Resize(LayoutDirection direction)
    {
        WithFocused(id => Fire(client => client.ResizeAsync(new LayoutResizeParams { Target = id, Direction = direction, Amount = 5 }, CancellationToken.None)));
    }

    private void FocusDirection(LayoutDirection direction)
    {
        WithFocused(id => Fire(async client =>
        {
            BlockInfo block = await client.FocusAsync(new BlockFocusParams { Target = id, Direction = direction }, CancellationToken.None).ConfigureAwait(false);
            FocusView(block.Id);
            return block;
        }));
    }

    private void SelectTabNumber(SessionMirror mirror, int number)
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        if (number >= 1 && number <= tabs.Count)
        {
            Fire(client => client.SelectTabAsync(tabs[number - 1].Id, CancellationToken.None));
        }
    }

    private void WithFocused(Action<string> action)
    {
        if (FocusedBlockId() is { } id)
        {
            action(id);
        }
        else
        {
            ClientLog.Debug("No focused block; the action was skipped.");
        }
    }

    private void Fire<T>(Func<ControlClient, Task<T>> call)
    {
        if (_control is { } control)
        {
            _ = FireAsync(control, call);
        }
    }

    private async Task FireAsync<T>(ControlClient control, Func<ControlClient, Task<T>> call)
    {
        try
        {
            _ = await call(control).ConfigureAwait(false);
            Status = null;
            ClientLog.Debug("action completed.");
        }
        catch (ProtocolException exception)
        {
            Status = exception.Message;
            ClientLog.Debug("action failed: " + exception.Message);
        }
        catch (OperationCanceledException)
        {
            ClientLog.Debug("action ignored OperationCanceledException.");
        }

        App?.Invalidate();
    }

    private void ReportSize(int width, int height)
    {
        if (width == _reportedWidth && height == _reportedHeight)
        {
            return;
        }

        _reportedWidth = width;
        _reportedHeight = height;
        if (_mirror is { } mirror)
        {
            Fire(client => client.SetSizeAsync(new SessionSetSizeParams { Client = mirror.Client.Id, Width = width, Height = height }, CancellationToken.None));
        }
    }
}
