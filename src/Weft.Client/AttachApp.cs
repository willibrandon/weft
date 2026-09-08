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
    private readonly Dictionary<string, BlockView> _views = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    // Session names are trimmed by the server, so an entry that starts with a space can never collide with one.
    private const string NewSessionEntry = " + new session";
    private ControlClient? _control;
    private RootContext? _root;
    private SessionMirror? _mirror;
    private Hex1bApp? _app;
    private CancellationTokenSource? _stopping;
    private string? _status;
    private bool _locked;
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
            _status = _bindings.Warnings[0];
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
    public string? Status => _status;

    /// <summary>
    /// Gets the outer terminal once running, for automation in tests.
    /// </summary>
    internal Hex1bTerminal? Terminal { get; private set; }

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

    private static Hex1bTheme ThemeFor(string name) => name.ToUpperInvariant() switch
    {
        "OCEAN" => Hex1bThemes.Ocean,
        "HIGH-CONTRAST" or "HIGHCONTRAST" => Hex1bThemes.HighContrast,
        "SUNSET" => Hex1bThemes.Sunset,
        _ => Hex1bThemes.Default
    };

    private async Task RunTerminalAsync(CancellationToken cancellationToken)
    {
        Hex1bTheme theme = ThemeFor(_options.Config.Theme);
        Hex1bTerminalBuilder builder = Hex1bTerminal.CreateBuilder()
            .WithMouse()
            .WithHex1bApp(options => options.Theme = theme, app =>
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

            var view = BlockView.Start(block.Id, block.SocketPath, block.Width, block.Height, _options.Name, () => _app?.Invalidate());
            view.Handle.TextCopied += text => Fire(client => client.SetPasteAsync(text, CancellationToken.None));
            _views[block.Id] = view;
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

    private void FocusView(string id)
    {
        BlockView? view = ViewFor(id);
        if (view is not null)
        {
            _app?.RequestFocus(node => node is TerminalNode terminal && terminal.Handle == view.Handle);
        }
    }

    private string? FocusedBlockId()
    {
        if (_app?.FocusedNode is TerminalNode terminal)
        {
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
        }

        return _mirror?.ActiveTab?.ActiveBlock;
    }

    private Hex1bWidget Render(RootContext ctx)
    {
        SessionMirror mirror = _mirror!;
        _root = ctx;
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
            foreach (BlockPlacement placement in layout.Floating)
            {
                if (!string.Equals(placement.Id, layout.Zoomed, StringComparison.Ordinal) && mirror.FindBlock(placement.Id) is { } block)
                {
                    layers.Add(z.Float(RenderBlock(z, block, placement.Width, placement.Height, layout.FrameSize > 0, mirror)).Absolute(placement.X + offsetX, placement.Y + offsetY));
                }
            }

            return [.. layers];
        });

        Hex1bWidget body = new BackgroundPanelWidget(s_panel, ctx.VStack(v => [content.Fill(), RenderInfoBar(v, mirror, layout, _status, _locked, _bindings)]));
        return body.InputBindings(bindings => RegisterBindings(bindings, mirror));
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
            return ctx.Center(ctx.Text(layout.Floating.Count > 0 ? string.Empty : "(no blocks)")).FixedWidth(layout.Width).FixedHeight(layout.Height);
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

    private static InfoBarWidget RenderInfoBar<TParent>(WidgetContext<TParent> ctx, SessionMirror mirror, LayoutInfo layout, string? status, bool locked, BindingTable bindings)
        where TParent : Hex1bWidget
    {
        IReadOnlyList<TabInfo> tabs = mirror.Tabs;
        string tabText = string.Join("  ", tabs.Select(tab =>
            (tab.Active ? "[" : string.Empty) + tab.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + tab.Name + (tab.Active ? "]" : string.Empty)));
        string size = layout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×" + layout.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string hint = locked ? bindings.ChordFor(ClientActions.Lock) + " unlock" : bindings.ChordFor(ClientActions.Palette) + " help";
        return ctx.InfoBar(s =>
        [
            s.Section(" " + mirror.Session.Name + " "),
            s.Section(tabText),
            s.Spacer(),
            s.Section(locked ? "LOCKED" : status ?? string.Empty),
            s.Section(size),
            s.Section(hint)
        ]).Divider(" ");
    }

    private void RegisterBindings(InputBindingsBuilder bindings, SessionMirror mirror)
    {
        foreach ((KeyChord chord, string action) in _bindings.Bindings)
        {
            if (_locked && !string.Equals(action, ClientActions.Lock, StringComparison.Ordinal))
            {
                continue;
            }

            KeyStepBuilder? step = BuildSteps(bindings, chord);
            if (step is null)
            {
                continue;
            }

            string captured = action;
            step.OverridesCapture().Action(context => Execute(captured, context, mirror), captured);
        }
    }

    private static KeyStepBuilder? BuildSteps(InputBindingsBuilder bindings, KeyChord chord)
    {
        KeyStepBuilder? builder = null;
        foreach (KeyStroke stroke in chord.Steps)
        {
            if (KeyMap.ToHex1bKey(stroke.Key) is not { } key)
            {
                return null;
            }

            KeyStepBuilder step = builder is null ? bindings.Key(key) : builder.Then().Key(key);
            if (stroke.Modifiers.HasFlag(KeyModifiers.Control))
            {
                step = step.Ctrl();
            }

            if (stroke.Modifiers.HasFlag(KeyModifiers.Alt))
            {
                step = step.Alt();
            }

            if (stroke.Modifiers.HasFlag(KeyModifiers.Shift))
            {
                step = step.Shift();
            }

            builder = step;
        }

        return builder;
    }

    private void Execute(string action, InputBindingActionContext context, SessionMirror mirror)
    {
        switch (action)
        {
            case ClientActions.Detach:
                ExitMessage = null;
                _app?.RequestStop();
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
                _app?.Invalidate();
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
            case ClientActions.SessionRename:
                Prompt(context, "Session name", mirror.Session.Name, name => Fire(client => client.RenameSessionAsync(new SessionRenameParams { Target = mirror.Session.Id, Name = name }, CancellationToken.None)));
                break;
            case ClientActions.TabPick:
                PickTabOrBlock(context, mirror);
                break;
            case ClientActions.Lock:
                _locked = !_locked;
                _app?.Invalidate();
                break;
            case ClientActions.Palette:
                ShowPalette(context, mirror);
                break;
            case ClientActions.SendLeader:
                SendLeaderKey();
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
        popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
        [
            b.VStack(v =>
            [
                v.Text(" " + label + " "),
                v.TextBox(initial).OnSubmit(e =>
                {
                    popups.Pop();
                    onSubmit(e.Text);
                }).FixedWidth(40),
                v.Text(" Enter to apply, Escape to cancel ")
            ])
        ]).Title(" weft ")), popups));
    }

    private static Hex1bWidget Dismissable(Hex1bWidget content, PopupStack popups) =>
        content.InputBindings(bindings => bindings.Key(Hex1bKey.Escape).Action(_ => popups.Pop(), "Dismiss"));

    private void ShowPalette(InputBindingActionContext context, SessionMirror mirror)
    {
        PopupStack popups = context.Popups;
        RootContext ctx = _root!;
        List<PaletteEntry> entries = [.. ClientActions.Defaults.Select(item => new PaletteEntry(item.Action, item.Description, _bindings.ChordFor(item.Action)))];
        popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
        [
            b.SelectionPrompt(entries)
                .Prompt("weft")
                .MaxVisibleItems(16)
                .OnSelected(entry =>
                {
                    popups.Pop();
                    Execute(entry.Action, context, mirror);
                })
        ]).Title(" commands ")), popups));
    }

    private void PickSession(InputBindingActionContext context, SessionMirror mirror)
    {
        PopupStack popups = context.Popups;
        RootContext ctx = _root!;
        Fire(async client =>
        {
            SessionListResult result = await client.ListSessionsAsync(CancellationToken.None).ConfigureAwait(false);
            List<string> names = [.. result.Sessions.Select(session => session.Name), NewSessionEntry];
            popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
            [
                b.SelectionPrompt(names).Prompt("session").MaxVisibleItems(12).OnSelected(name =>
                {
                    popups.Pop();
                    if (string.Equals(name, mirror.Session.Name, StringComparison.Ordinal))
                    {
                        return;
                    }

                    SwitchTarget = string.Equals(name, NewSessionEntry, StringComparison.Ordinal) ? string.Empty : name;
                    ExitMessage = null;
                    _app?.RequestStop();
                })
            ]).Title(" sessions ")), popups));
            _app?.Invalidate();
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
        popups.Push(() => Dismissable(ctx.Center(ctx.Border(b =>
        [
            b.SelectionPrompt(labels).Prompt("go to").MaxVisibleItems(16).OnSelected(label =>
            {
                popups.Pop();
                (string _, string tab, string? block) = items.First(item => string.Equals(item.Label, label, StringComparison.Ordinal));
                Fire(async client =>
                {
                    await client.SelectTabAsync(tab, CancellationToken.None).ConfigureAwait(false);
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

    private void Split(SplitOrientation orientation) =>
        WithFocused(id => Fire(client => client.SplitAsync(new BlockSplitParams { Target = id, Orientation = orientation }, CancellationToken.None)));

    private void Resize(LayoutDirection direction) =>
        WithFocused(id => Fire(client => client.ResizeAsync(new LayoutResizeParams { Target = id, Direction = direction, Amount = 5 }, CancellationToken.None)));

    private void FocusDirection(LayoutDirection direction) =>
        WithFocused(id => Fire(async client =>
        {
            BlockInfo block = await client.FocusAsync(new BlockFocusParams { Target = id, Direction = direction }, CancellationToken.None).ConfigureAwait(false);
            FocusView(block.Id);
            return block;
        }));

    private void SendLeaderKey()
    {
        BlockView? view = ViewFor(FocusedBlockId());
        if (view is null)
        {
            return;
        }

        // A multi-stroke leader is sent stroke by stroke, in order, on one queue.
        _ = SendStrokesAsync(view, _bindings.Leader.Steps);
    }

    private static async Task SendStrokesAsync(BlockView view, IReadOnlyList<KeyStroke> strokes)
    {
        foreach (KeyStroke stroke in strokes)
        {
            if (KeyMap.ToKeyEvent(stroke) is { } keyEvent)
            {
                await view.Handle.SendEventAsync(keyEvent).ConfigureAwait(false);
            }
        }
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
}
