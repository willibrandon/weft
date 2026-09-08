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
    private readonly Dictionary<string, BlockView> _views = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private ControlClient? _control;
    private SessionMirror? _mirror;
    private Hex1bApp? _app;
    private CancellationTokenSource? _stopping;
    private string? _status;
    private bool _help;
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
    }

    /// <summary>
    /// Gets a message explaining why the client stopped, if it stopped on its own.
    /// </summary>
    public string? ExitMessage { get; private set; }

    /// <summary>
    /// Attaches, runs until detached, and cleans up.
    /// </summary>
    /// <param name="cancellationToken">Stops the client.</param>
    /// <returns>A task that completes after detaching.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stopping = stopping;
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
                await events.SubscribeAsync(attached.Seq, cancellationToken).ConfigureAwait(false);
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
                            await control.DetachAsync(attached.Client.Id, detachTimeout.Token).ConfigureAwait(false);
                        }
                        catch (ProtocolException)
                        {
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the outer terminal once running, for automation in tests.
    /// </summary>
    internal Hex1bTerminal? Terminal { get; private set; }

    private async Task RunTerminalAsync(CancellationToken cancellationToken)
    {
        Hex1bTerminalBuilder builder = Hex1bTerminal.CreateBuilder()
            .WithMouse()
            .WithHex1bApp(_ => { }, app =>
            {
                _app = app;
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
                await terminal.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
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
                    _app?.RequestStop();
                }

                _app?.Invalidate();
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            ExitMessage ??= "Lost the connection to the server.";
            _app?.RequestStop();
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

            _views[block.Id] = BlockView.Start(block.Id, block.SocketPath, block.Width, block.Height, _options.Name, () => _app?.Invalidate());
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
                _views.Remove(id);
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

    private void FocusView(string id)
    {
        BlockView? view;
        lock (_gate)
        {
            _views.TryGetValue(id, out view);
        }

        if (view is not null)
        {
            _app?.RequestFocus(node => node is TerminalNode terminal && terminal.Handle == view.Handle);
        }
    }

    private string? FocusedBlockId()
    {
        if (_app?.FocusedNode is not TerminalNode terminal)
        {
            return _mirror?.ActiveTab?.ActiveBlock;
        }

        lock (_gate)
        {
            foreach (BlockView view in _views.Values)
            {
                if (view.Handle == terminal.Handle)
                {
                    return view.Id;
                }
            }
        }

        return _mirror?.ActiveTab?.ActiveBlock;
    }

    private Hex1bWidget Render(RootContext ctx)
    {
        SessionMirror mirror = _mirror!;
        (int width, int height) = HostSize.Read(_options.Headless);
        int availableWidth = Math.Max(1, width);
        int availableHeight = Math.Max(1, height - 1);
        ReportSize(availableWidth, availableHeight);

        LayoutInfo layout = mirror.Layout;
        Hex1bWidget body;
        if (_help)
        {
            body = RenderHelp(ctx);
        }
        else
        {
            Hex1bWidget tree = RenderLayout(ctx, mirror, layout);
            bool fits = layout.Width <= availableWidth && layout.Height <= availableHeight;
            body = ctx.Align(fits ? Alignment.Center : Alignment.TopLeft, tree).Fill();
        }

        Hex1bWidget content = new BackgroundPanelWidget(s_panel, ctx.VStack(v => [body, RenderInfoBar(v, mirror, layout, _status)]));
        return content.InputBindings(bindings => RegisterBindings(bindings, mirror));
    }

    private Hex1bWidget RenderLayout<TParent>(WidgetContext<TParent> ctx, SessionMirror mirror, LayoutInfo layout)
        where TParent : Hex1bWidget
    {
        if (layout.Zoomed is { } zoomed && mirror.FindBlock(zoomed) is { } zoomedBlock)
        {
            return RenderBlock(ctx, zoomedBlock, layout.Width, layout.Height, layout.FrameSize > 0, mirror);
        }

        if (!LayoutSerializer.TryParse(layout.Serialized, out LayoutCell? root) || root is null)
        {
            return ctx.Center(ctx.Text("(no blocks)")).FixedWidth(layout.Width).FixedHeight(layout.Height);
        }

        return RenderCell(ctx, root, layout, mirror);
    }

    private Hex1bWidget RenderCell<TParent>(WidgetContext<TParent> ctx, LayoutCell cell, LayoutInfo layout, SessionMirror mirror)
        where TParent : Hex1bWidget
    {
        if (cell.IsLeaf)
        {
            BlockInfo? block = cell.Block is { } id ? mirror.FindBlock(id.ToString()) : null;
            if (block is null)
            {
                return ctx.Text(string.Empty).FixedWidth(cell.Width).FixedHeight(cell.Height);
            }

            return RenderBlock(ctx, block, cell.Width, cell.Height, layout.FrameSize > 0, mirror);
        }

        int spacing = layout.FrameSize > 0 ? 0 : 1;
        if (cell.Orientation == SplitOrientation.LeftRight)
        {
            return ctx.HStack(h =>
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
            }).FixedWidth(cell.Width).FixedHeight(cell.Height);
        }

        return ctx.VStack(v =>
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
        BlockView? view;
        lock (_gate)
        {
            _views.TryGetValue(block.Id, out view);
        }

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
        string title = " " + (active ? "● " : string.Empty) + block.Title + marker + " ";
        return ctx.Border(inner).Title(title).FixedWidth(width).FixedHeight(height);
    }

    private static AlignWidget RenderHelp<TParent>(WidgetContext<TParent> ctx)
        where TParent : Hex1bWidget
    {
        string[] lines =
        [
            "  Ctrl+B is the leader. Press it, then:",
            string.Empty,
            "  d        detach                 c        new tab",
            "  n / p    next / previous tab    1..9     select tab",
            "  v        split right            -        split below",
            "  x        close block            z        zoom block",
            "  h j k l  focus by direction     H J K L  resize by five",
            "  arrows   focus by direction     Space    next layout preset",
            "  Ctrl+B   send Ctrl+B            ?        toggle this help",
            string.Empty,
            "  Shift+PageUp scrolls a block. Mouse clicks focus, wheel scrolls.",
        ];
        return ctx.Center(ctx.Border(b => [b.VStack(v => [.. lines.Select(line => (Hex1bWidget)v.Text(line))])]).Title(" weft "));
    }

    private static InfoBarWidget RenderInfoBar<TParent>(WidgetContext<TParent> ctx, SessionMirror mirror, LayoutInfo layout, string? status)
        where TParent : Hex1bWidget
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        string tabText = string.Join("  ", tabs.Select(tab =>
            (tab.Active ? "[" : string.Empty) + tab.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + tab.Name + (tab.Active ? "]" : string.Empty)));
        string size = layout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×" + layout.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ctx.InfoBar(s =>
        [
            s.Section(" " + mirror.Session.Name + " "),
            s.Section(tabText),
            s.Spacer(),
            s.Section(status ?? string.Empty),
            s.Section(size),
            s.Section("Ctrl+B ?"),
            s.Section("help")
        ]).Divider(" ");
    }

    private void RegisterBindings(InputBindingsBuilder bindings, SessionMirror mirror)
    {
        Leader(bindings, Hex1bKey.D).Action(_ => Detach(), "Detach");
        Leader(bindings, Hex1bKey.C).Action(_ => Fire(client => client.CreateTabAsync(new TabCreateParams { Target = mirror.Session.Id }, CancellationToken.None)), "New tab");
        Leader(bindings, Hex1bKey.N).Action(_ => SelectTabRelative(mirror, 1), "Next tab");
        Leader(bindings, Hex1bKey.P).Action(_ => SelectTabRelative(mirror, -1), "Previous tab");
        Hex1bKey[] digits = [Hex1bKey.D1, Hex1bKey.D2, Hex1bKey.D3, Hex1bKey.D4, Hex1bKey.D5, Hex1bKey.D6, Hex1bKey.D7, Hex1bKey.D8, Hex1bKey.D9];
        for (int i = 0; i < digits.Length; i++)
        {
            int index = i + 1;
            Leader(bindings, digits[i]).Action(_ => SelectTabIndex(mirror, index), "Select tab");
        }

        Leader(bindings, Hex1bKey.V).Action(_ => Split(SplitOrientation.LeftRight), "Split right");
        Leader(bindings, Hex1bKey.OemMinus).Action(_ => Split(SplitOrientation.TopBottom), "Split below");
        Leader(bindings, Hex1bKey.X).Action(_ => WithFocused(id => Fire(client => client.CloseBlockAsync(id, CancellationToken.None))), "Close block");
        Leader(bindings, Hex1bKey.Z).Action(_ => WithFocused(id => Fire(client => client.ZoomAsync(new BlockZoomParams { Target = id }, CancellationToken.None))), "Zoom block");
        Leader(bindings, Hex1bKey.Spacebar).Action(_ => Fire(client => client.PresetAsync(new LayoutPresetParams { Target = mirror.Session.Id }, CancellationToken.None)), "Next layout");
        Leader(bindings, Hex1bKey.OemQuestion).Action(_ =>
        {
            _help = !_help;
            _app?.Invalidate();
        }, "Help");
        bindings.Ctrl().Key(Hex1bKey.B).Then().Ctrl().Key(Hex1bKey.B).OverridesCapture().Action(_ => SendLeaderKey(), "Send Ctrl+B");

        (Hex1bKey Key, LayoutDirection Direction)[] moves =
        [
            (Hex1bKey.H, LayoutDirection.Left), (Hex1bKey.J, LayoutDirection.Down), (Hex1bKey.K, LayoutDirection.Up), (Hex1bKey.L, LayoutDirection.Right),
            (Hex1bKey.LeftArrow, LayoutDirection.Left), (Hex1bKey.DownArrow, LayoutDirection.Down), (Hex1bKey.UpArrow, LayoutDirection.Up), (Hex1bKey.RightArrow, LayoutDirection.Right)
        ];
        foreach ((Hex1bKey key, LayoutDirection direction) in moves)
        {
            Leader(bindings, key).Action(_ => FocusDirection(direction), "Focus " + direction.ToString().ToUpperInvariant());
        }

        (Hex1bKey Key, LayoutDirection Direction)[] resizes =
        [
            (Hex1bKey.H, LayoutDirection.Left), (Hex1bKey.J, LayoutDirection.Down), (Hex1bKey.K, LayoutDirection.Up), (Hex1bKey.L, LayoutDirection.Right)
        ];
        foreach ((Hex1bKey key, LayoutDirection direction) in resizes)
        {
            bindings.Ctrl().Key(Hex1bKey.B).Then().Shift().Key(key).OverridesCapture()
                .Action(_ => WithFocused(id => Fire(client => client.ResizeAsync(new LayoutResizeParams { Target = id, Direction = direction, Amount = 5 }, CancellationToken.None))), "Resize " + direction.ToString().ToUpperInvariant());
        }
    }

    private static KeyStepBuilder Leader(InputBindingsBuilder bindings, Hex1bKey key) =>
        bindings.Ctrl().Key(Hex1bKey.B).Then().Key(key).OverridesCapture();

    private void Detach()
    {
        ExitMessage = null;
        _app?.RequestStop();
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

    private void SelectTabIndex(SessionMirror mirror, int index)
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        TabInfo? tab = tabs.FirstOrDefault(candidate => candidate.Index == index);
        if (tab is not null)
        {
            Fire(client => client.SelectTabAsync(tab.Id, CancellationToken.None));
        }
    }

    private void Split(SplitOrientation orientation) =>
        WithFocused(id => Fire(client => client.SplitAsync(new BlockSplitParams { Target = id, Orientation = orientation }, CancellationToken.None)));

    private void FocusDirection(LayoutDirection direction) =>
        WithFocused(id => Fire(async client =>
        {
            BlockInfo block = await client.FocusAsync(new BlockFocusParams { Target = id, Direction = direction }, CancellationToken.None).ConfigureAwait(false);
            FocusView(block.Id);
            return block;
        }));

    private void SendLeaderKey()
    {
        string? id = FocusedBlockId();
        BlockView? view = null;
        if (id is not null)
        {
            lock (_gate)
            {
                _views.TryGetValue(id, out view);
            }
        }

        if (view is not null)
        {
            _ = view.Handle.SendEventAsync(new Hex1bKeyEvent(Hex1bKey.B, '\x02', Hex1bModifiers.Control));
        }
    }

    private void WithFocused(Action<string> action)
    {
        if (FocusedBlockId() is { } id)
        {
            action(id);
        }
    }

    private void Fire<T>(Func<ControlClient, Task<T>> call)
    {
        if (_control is not { } control)
        {
            return;
        }

        _ = FireAsync(control, call);
    }

    private async Task FireAsync<T>(ControlClient control, Func<ControlClient, Task<T>> call)
    {
        try
        {
            await call(control).ConfigureAwait(false);
            _status = null;
        }
        catch (ProtocolException exception)
        {
            _status = exception.Message;
        }
        catch (OperationCanceledException)
        {
        }

        _app?.Invalidate();
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

    /// <summary>
    /// Gets the latest error message from a failed action, for display.
    /// </summary>
    public string? Status => _status;
}
