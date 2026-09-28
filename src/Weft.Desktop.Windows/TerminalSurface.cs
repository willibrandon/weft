using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Weft.Client;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Weft.Desktop.Windows;

/// <summary>
/// Draws terminal snapshots and translates Windows text, pointer, and clipboard input into terminal commands.
/// </summary>
/// <remarks>
/// Terminal state and escape-sequence interpretation belong to the shared client core. Frames arrive
/// imperatively and invalidate only changed rows, so output never passes through UI reconciliation.
/// </remarks>
internal sealed partial class TerminalSurface : UserControl, IDisposable
{
    private const float Inset = 2;
    private readonly Grid _root = new();
    private readonly CanvasVirtualControl _canvas = new();
    private readonly Canvas _overlay = new();
    private readonly TerminalInput _input;
    private readonly TerminalImages _images;
    private readonly Dictionary<string, TerminalScrollBar> _scrollers = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, double> _wheelRemainders = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _pendingSelections = [with(StringComparer.Ordinal)];
    private readonly DispatcherQueueTimer _blink;
    private readonly DispatcherQueueTimer _autoscroll;
    private readonly UISettings _settings = new();
    private readonly TerminalRenderer _renderer;
    private bool _cursorLit = true;
    private bool _windowActive = true;
    private bool _composing;
    private bool _discardComposition;
    private string? _compositionBlock;
    private bool _suppressText;
    private (DesktopBlockFrame Block, int Index, Point Point)? _anchor;
    private bool _dragged;
    private string? _selectionBlock;
    private Point? _selectionPoint;
    private (string Block, string Edge, Point Origin, int Sent)? _resizing;
    private string? _pointerBlock;
    private int _pointerButton;
    private int _clicks;
    private long _lastClick;
    private Point _lastClickPoint;
    private bool _released;

    /// <summary>
    /// Creates a surface with the preferred font and colors.
    /// </summary>
    public TerminalSurface()
    {
        _images = new TerminalImages(Invalidate);
        Font = TerminalFont.Create(CanvasDevice.GetSharedDevice(), PreferencesStore.Current.FontFamily,
            PreferencesStore.Current.FontSize);
        _renderer = new TerminalRenderer(Font, _images);
        _input = new TerminalInput(this);
        _canvas.UseSharedDevice = true;
        _canvas.ClearColor = TerminalAppearance.Background;
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        _canvas.CreateResources += OnCreateResources;
        _overlay.Children.Add(_input);
        _root.Children.Add(_canvas);
        _root.Children.Add(_overlay);
        _root.Background = new SolidColorBrush(TerminalAppearance.Background);
        Content = _root;
        IsTabStop = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
        SizeChanged += (_, _) => UpdateGrid();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndGesture();
        PointerWheelChanged += OnPointerWheelChanged;
        _input.PreviewKeyDown += OnInputKeyDown;
        _input.TextChanged += OnInputTextChanged;
        _input.TextCompositionStarted += (_, _) => BeginComposition();
        _input.TextCompositionEnded += (_, _) => EndComposition();
        _input.GotFocus += (_, _) => SetFocused(true);
        _input.LostFocus += (_, _) => SetFocused(false);
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _blink = dispatcher.CreateTimer();
        _blink.Interval = TimeSpan.FromMilliseconds(600);
        _blink.Tick += (_, _) =>
        {
            _cursorLit = !_cursorLit;
            InvalidateCursor(ActiveBlock);
        };
        _autoscroll = dispatcher.CreateTimer();
        _autoscroll.Interval = TimeSpan.FromMilliseconds(30);
        _autoscroll.Tick += (_, _) => ScrollSelection();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            // Turning animation effects off in Windows settings stops cursor blinking.
            _settings.AnimationsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(() => UpdateBlink(reset: true));
        }
        PreferencesStore.Changed += RefreshPreferences;
    }

    /// <summary>
    /// Raised with an ordered terminal command for the attached session.
    /// </summary>
    internal event Action<DesktopCommand>? CommandRequested;

    /// <summary>
    /// Raised with a catalog action and its explicit pane target, such as from the context menu.
    /// </summary>
    internal event Action<string, string>? ActionRequested;

    /// <summary>
    /// Raised when the visible grid changes size, with its columns and rows.
    /// </summary>
    internal event Action<int, int>? GridChanged;

    /// <summary>
    /// Gets the current grid, used when attaching.
    /// </summary>
    internal (int Columns, int Rows) CellGrid { get; private set; }

    /// <summary>
    /// Gets the latest frame.
    /// </summary>
    internal DesktopFrame? Frame { get; private set; }

    /// <summary>
    /// Gets the active block, or the first block when none is active.
    /// </summary>
    internal DesktopBlockFrame? ActiveBlock =>
        Frame?.Blocks.FirstOrDefault(block => block.Active)
            ?? (Frame is { Blocks.Count: > 0 } frame ? frame.Blocks[0] : null);

    /// <summary>
    /// Gets the font used for the grid.
    /// </summary>
    internal TerminalFont Font { get; private set; }

    /// <summary>
    /// Gets whether the terminal has keyboard focus.
    /// </summary>
    internal bool HasInputFocus { get; private set; }

    /// <summary>
    /// Gets the decoded raster bytes retained for qualification; this is not process memory.
    /// </summary>
    internal int RasterCacheBytes => _images.RetainedBytes;

    /// <summary>
    /// Gets the number of retained rasters.
    /// </summary>
    internal int RasterCacheCount => _images.Count;

    /// <summary>
    /// Gets the selected text in any visible pane.
    /// </summary>
    internal string SelectedText =>
        Frame?.Blocks.FirstOrDefault(block => block.Selection is not null)?.Selection?.Text ?? string.Empty;

    /// <summary>
    /// Applies a frame without changing text covered by an existing selection.
    /// </summary>
    /// <param name="frame">The frame.</param>
    internal void Update(DesktopFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_released)
        {
            return;
        }

        DesktopFrame? previous = Frame;
        DesktopBlockFrame? before = ActiveBlock;
        Frame = frame;
        DesktopBlockFrame? after = ActiveBlock;
        if (_composing && !string.Equals(_compositionBlock, after?.Id, StringComparison.Ordinal))
        {
            // Composition belongs to its originating pane and is cancelled when focus moves.
            CancelComposition();
        }

        _images.Retain(frame.Blocks.SelectMany(block => block.Textures).Select(TerminalImages.KeyOf)
            .ToHashSet(StringComparer.Ordinal));
        if (_selectionBlock is not null && !frame.Blocks.Any(block => block.Id == _selectionBlock))
        {
            EndGesture();
        }

        bool cursorMoved = before?.Id != after?.Id || before?.CursorX != after?.CursorX
            || before?.CursorY != after?.CursorY || before?.CursorShape != after?.CursorShape;
        UpdateBlink(reset: cursorMoved);
        UpdateScrollers();
        PositionInput();
        InvalidateChanges(previous, frame);
        if (FrameworkElementAutomationPeer.FromElement(_input) is TerminalAutomationPeer peer)
        {
            peer.Refresh(previous?.Blocks.FirstOrDefault(block => block.Active)?.Selection != after?.Selection);
        }
    }

    /// <summary>
    /// Records whether the owning window is active, which controls cursor blinking.
    /// </summary>
    /// <param name="active">Whether the window is active.</param>
    internal void SetWindowActive(bool active)
    {
        _windowActive = active;
        UpdateBlink(reset: true);
    }

    /// <summary>
    /// Gives keyboard focus to the terminal.
    /// </summary>
    internal void FocusTerminal()
    {
        _ = _input.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Changes the font size by whole device-independent pixels.
    /// </summary>
    /// <param name="amount">The change.</param>
    internal void ChangeFontSize(int amount)
    {
        float size = Math.Clamp(Font.Size + amount, 8, 40);
        PreferencesStore.Update(preferences => preferences with { FontSize = size });
    }

    /// <summary>
    /// Returns a pane to live output and releases any retained selection.
    /// </summary>
    /// <param name="id">The pane, or null for the active pane.</param>
    internal void Resume(string? id = null)
    {
        ClearSelection();
        string? target = id ?? ActiveBlock?.Id;
        if (target is not null && _scrollers.TryGetValue(target, out TerminalScrollBar? scroller))
        {
            scroller.Follow();
        }

        Send(new DesktopCommand("live", target));
    }

    /// <summary>
    /// Clears every visible selection and ends any selection gesture.
    /// </summary>
    internal void ClearSelection()
    {
        EndGesture();
        IReadOnlyList<DesktopBlockFrame> blocks = Frame?.Blocks ?? [];
        foreach (DesktopBlockFrame block in blocks.Where(block =>
            block.Selection is not null || _pendingSelections.Contains(block.Id)))
        {
            Send(new DesktopCommand("select", block.Id, "clear"));
        }

        _pendingSelections.Clear();
    }

    /// <summary>
    /// Copies the retained selection as plain text.
    /// </summary>
    internal void Copy()
    {
        string text = SelectedText;
        if (text.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// Pastes clipboard text into a pane, returning it to live output first.
    /// </summary>
    /// <param name="target">The pane, or null for the active pane.</param>
    internal void Paste(string? target = null)
    {
        _ = PasteAsync(target);
    }

    private async Task PasteAsync(string? target)
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            PasteText(await content.GetTextAsync(), target);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException)
        {
            ClientLog.Debug("The clipboard could not be read: " + exception.Message);
        }
    }

    /// <summary>
    /// Pastes text into a pane after returning it to live output.
    /// </summary>
    /// <param name="text">The text, with any line endings.</param>
    /// <param name="target">The pane, or null for the active pane.</param>
    internal void PasteText(string text, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? pane = target ?? ActiveBlock?.Id;
        Resume(pane);
        // Each line ends with Enter, as terminals paste. Windows consoles, unlike Unix terminal drivers, do not
        // take a line feed as Enter, and a Windows line ending would otherwise press it twice.
        Send(new DesktopCommand("paste", pane, text.ReplaceLineEndings("\r")));
    }

    /// <summary>
    /// Selects the active pane's retained text.
    /// </summary>
    internal void SelectAll()
    {
        if (ActiveBlock is { Cells.Count: > 0 } block)
        {
            _ = _pendingSelections.Add(block.Id);
            Send(new DesktopCommand("select", block.Id, "all"));
        }
    }

    /// <summary>
    /// Requests a selection from accessibility clients without moving the process cursor.
    /// </summary>
    /// <param name="first">The first selected cell index.</param>
    /// <param name="last">The last selected cell index.</param>
    internal void SelectCells(int first, int last)
    {
        if (ActiveBlock is not { } block)
        {
            return;
        }

        if (last < first)
        {
            ClearSelection();
            return;
        }

        _ = _pendingSelections.Add(block.Id);
        Send(new DesktopCommand("select", block.Id, "cell", X: first % block.Width, Y: first / block.Width));
        Send(new DesktopCommand("select", block.Id, "extend", X: last % block.Width, Y: last / block.Width));
    }

    /// <summary>
    /// Gets a cell's rectangle in surface coordinates.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="index">The row-major cell index.</param>
    /// <returns>The rectangle.</returns>
    internal Rect CellRect(DesktopBlockFrame block, int index)
    {
        ArgumentNullException.ThrowIfNull(block);
        Rect content = _renderer.ContentRect(block, Inset);
        int column = index % block.Width;
        int row = index / block.Width;
        return new Rect(content.X + (column * Font.CellWidth), content.Y + (row * Font.CellHeight), Font.CellWidth,
            Font.CellHeight);
    }

    /// <summary>
    /// Gets the cell under a point in surface coordinates.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="point">The point.</param>
    /// <returns>The clamped row-major cell index.</returns>
    internal int CellIndex(DesktopBlockFrame block, Point point)
    {
        ArgumentNullException.ThrowIfNull(block);
        Rect content = _renderer.ContentRect(block, Inset);
        int x = Math.Clamp((int)((point.X - content.X) / Font.CellWidth), 0, block.Width - 1);
        int y = Math.Clamp((int)((point.Y - content.Y) / Font.CellHeight), 0, block.Height - 1);
        return (y * block.Width) + x;
    }

    /// <summary>
    /// Releases frames, caches, timers, and drawing resources; a released surface cannot be reused.
    /// </summary>
    internal void ReleaseResources()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _blink.Stop();
        _autoscroll.Stop();
        PreferencesStore.Changed -= RefreshPreferences;
        EndGesture();
        Frame = null;
        _pendingSelections.Clear();
        foreach (TerminalScrollBar scroller in _scrollers.Values)
        {
            _ = _overlay.Children.Remove(scroller.Control);
        }

        _scrollers.Clear();
        _wheelRemainders.Clear();
        _renderer.Dispose();
        _images.Dispose();
        Font.Dispose();
        _canvas.RegionsInvalidated -= OnRegionsInvalidated;
        _canvas.CreateResources -= OnCreateResources;
        _canvas.RemoveFromVisualTree();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseResources();
    }

    private void Send(DesktopCommand command)
    {
        CommandRequested?.Invoke(command);
    }

    private void OnCreateResources(CanvasVirtualControl sender, CanvasCreateResourcesEventArgs args)
    {
        // A new device invalidates every brush and bitmap created on the previous one.
        _renderer.ReleaseDeviceResources();
        _canvas.Invalidate();
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var scene = new TerminalScene(Frame ?? EmptyFrame, TerminalAppearance.Background, TerminalAppearance.Foreground,
            TerminalAppearance.Cursor, TerminalAppearance.Selection, _cursorLit, Inset);
        foreach (Rect region in args.InvalidatedRegions)
        {
            using CanvasDrawingSession session = sender.CreateDrawingSession(region);
            _renderer.Draw(session, region, scene);
        }
    }

    private static DesktopFrame EmptyFrame { get; } = new(false, "Weft", null, null, [], [], [], null);

    private void Invalidate()
    {
        if (!_released)
        {
            _canvas.Invalidate();
        }
    }

    /// <summary>
    /// Preserves unchanged rows in the retained canvas, including when only the prompt or caret changes.
    /// </summary>
    private void InvalidateChanges(DesktopFrame? previous, DesktopFrame frame)
    {
        if (previous is null || previous.Blocks.Count != frame.Blocks.Count)
        {
            Invalidate();
            return;
        }

        for (int blockIndex = 0; blockIndex < frame.Blocks.Count; blockIndex++)
        {
            DesktopBlockFrame old = previous.Blocks[blockIndex];
            DesktopBlockFrame block = frame.Blocks[blockIndex];
            if (old.Id != block.Id || old.X != block.X || old.Y != block.Y || old.Width != block.Width
                || old.Height != block.Height || old.Title != block.Title || old.Active != block.Active
                || old.ViewVersion != block.ViewVersion
                || old.SearchQuery != block.SearchQuery || old.Selection != block.Selection
                || !old.Images.SequenceEqual(block.Images) || old.Cells.Count != block.Cells.Count)
            {
                Invalidate();
                return;
            }

            Rect content = _renderer.ContentRect(block, Inset);
            for (int row = 0; row < block.Height; row++)
            {
                int start = row * block.Width;
                for (int column = 0; column < block.Width; column++)
                {
                    if (old.Cells[start + column] != block.Cells[start + column])
                    {
                        _canvas.Invalidate(new Rect(content.X, content.Y + (row * Font.CellHeight), content.Width,
                            Font.CellHeight));
                        break;
                    }
                }
            }

            if (old.CursorX != block.CursorX || old.CursorY != block.CursorY || old.CursorVisible != block.CursorVisible
                || old.CursorShape != block.CursorShape)
            {
                InvalidateCursor(old);
                InvalidateCursor(block);
            }
        }
    }

    private void InvalidateCursor(DesktopBlockFrame? block)
    {
        if (block is { Active: true } && !_released)
        {
            _canvas.Invalidate(CellRect(block, (block.CursorY * block.Width) + block.CursorX));
        }
    }

    private void UpdateGrid()
    {
        double width = ActualWidth - (Inset * 2);
        double height = ActualHeight - (Inset * 2);
        (int Columns, int Rows) grid = (Math.Clamp((int)(width / Font.CellWidth), 4, 500),
            Math.Clamp((int)(height / Font.CellHeight), 4, 300));
        if (width <= 0 || height <= 0 || grid == CellGrid)
        {
            return;
        }

        CellGrid = grid;
        Invalidate();
        GridChanged?.Invoke(grid.Columns, grid.Rows);
    }

    private void RefreshPreferences()
    {
        TerminalFont previous = Font;
        Font = TerminalFont.Create(CanvasDevice.GetSharedDevice(), PreferencesStore.Current.FontFamily,
            PreferencesStore.Current.FontSize);
        _renderer.Font = Font;
        previous.Dispose();
        _canvas.ClearColor = TerminalAppearance.Background;
        _root.Background = new SolidColorBrush(TerminalAppearance.Background);
        CellGrid = default;
        UpdateGrid();
        UpdateScrollers();
        PositionInput();
        Invalidate();
    }

    /// <summary>
    /// Blinks only a focused, visible caret; steady DECSCUSR styles and composition keep it lit.
    /// </summary>
    private void UpdateBlink(bool reset)
    {
        DesktopBlockFrame? cursor = ActiveBlock;
        bool blinking = cursor is { CursorVisible: true, Selection: null } && cursor.CursorShape is 0 or 1 or 3 or 5
            && !_composing && _windowActive && HasInputFocus && _settings.AnimationsEnabled && !_released;
        if (reset || !blinking)
        {
            _blink.Stop();
            if (!_cursorLit)
            {
                _cursorLit = true;
                InvalidateCursor(cursor);
            }
        }

        if (blinking && !_blink.IsRunning)
        {
            _blink.Start();
        }
    }

    private void SetFocused(bool focused)
    {
        HasInputFocus = focused;
        UpdateBlink(reset: true);
    }

    private void PositionInput()
    {
        if (ActiveBlock is not { } block)
        {
            return;
        }

        Rect cell = CellRect(block, (block.CursorY * block.Width) + block.CursorX);
        Canvas.SetLeft(_input, cell.X);
        Canvas.SetTop(_input, cell.Y);
        _input.Height = Font.CellHeight;
        _input.MinWidth = Font.CellWidth;
        _input.FontSize = Font.Size;
        _input.FontFamily = new FontFamily(Font.FormatFamily);
        _input.Foreground = new SolidColorBrush(TerminalAppearance.Foreground);
    }

    private void UpdateScrollers()
    {
        IReadOnlyList<DesktopBlockFrame> blocks = Frame?.Blocks ?? [];
        foreach (string id in _scrollers.Keys.Where(id => !blocks.Any(block => block.Id == id)).ToArray())
        {
            _ = _overlay.Children.Remove(_scrollers[id].Control);
            _ = _scrollers.Remove(id);
            _ = _wheelRemainders.Remove(id);
        }

        foreach (DesktopBlockFrame block in blocks)
        {
            if (!_scrollers.TryGetValue(block.Id, out TerminalScrollBar? scroller))
            {
                string id = block.Id;
                scroller = new TerminalScrollBar(offset => Send(new DesktopCommand("scrollTo", id, Y: offset)));
                _overlay.Children.Insert(0, scroller.Control);
                _scrollers[id] = scroller;
            }

            scroller.Update(block, _renderer.ContentRect(block, Inset));
        }
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        VirtualKeyModifiers modifiers = Modifiers();
        bool altGraph = IsDown(VirtualKey.RightMenu) && modifiers.HasFlag(VirtualKeyModifiers.Control);
        e.Handled = HandleKey(e.Key, modifiers, altGraph);
    }

    /// <summary>
    /// Sends a key that produces no text, leaving printable keys to the text input.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    /// <param name="altGraph">Whether the right Alt key is down as AltGr.</param>
    /// <returns>Whether the key was consumed.</returns>
    internal bool HandleKey(VirtualKey key, VirtualKeyModifiers modifiers, bool altGraph)
    {
        if (_composing)
        {
            // An input method owns Enter, Backspace, and arrows until it commits or cancels.
            return false;
        }

        if (key == VirtualKey.Insert && modifiers == VirtualKeyModifiers.Control)
        {
            Copy();
            return true;
        }

        if (key == VirtualKey.Insert && modifiers == VirtualKeyModifiers.Shift)
        {
            Paste();
            return true;
        }

        if (TerminalKeys.Name(key, modifiers, altGraph) is not { } name)
        {
            return false;
        }

        ReturnToLive();
        UpdateBlink(reset: true);
        Send(new DesktopCommand("key", ActiveBlock?.Id, name));
        return true;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressText || _composing || _input.Text.Length == 0)
        {
            return;
        }

        string text = _input.Text;
        ClearInputText();
        if (_discardComposition)
        {
            return;
        }

        ReturnToLive();
        UpdateBlink(reset: true);
        Send(new DesktopCommand("text", ActiveBlock?.Id, text));
    }

    /// <summary>
    /// Starts an input method composition, holding its text until it commits.
    /// </summary>
    internal void BeginComposition()
    {
        _composing = true;
        _discardComposition = false;
        _compositionBlock = ActiveBlock?.Id;
        _input.Opacity = 1;
        _input.Background = new SolidColorBrush(TerminalAppearance.Background);
        UpdateBlink(reset: true);
    }

    /// <summary>
    /// Ends a composition and sends its committed text once, unless a pane change cancelled it.
    /// </summary>
    internal void EndComposition()
    {
        _composing = false;
        _compositionBlock = null;
        _input.Opacity = 0;
        _input.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        UpdateBlink(reset: true);
        if (_discardComposition)
        {
            // An input method may report cancelled text just before or after it ends the composition, so
            // text is discarded until input already queued has been handled, whether or not any arrives.
            ClearInputText();
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _discardComposition = false);
            return;
        }

        OnInputTextChanged(_input, null!);
    }

    private void CancelComposition()
    {
        // Ending composition by moving focus commits by default; the committed text is discarded instead.
        _discardComposition = true;
        ClearInputText();
        _ = _canvas.Focus(FocusState.Programmatic);
        _ = _input.Focus(FocusState.Programmatic);
    }

    private void ClearInputText()
    {
        _suppressText = true;
        _input.Text = string.Empty;
        _suppressText = false;
    }

    private void ReturnToLive()
    {
        if (ActiveBlock is { } block && (block.ViewVersion != 0 || _pendingSelections.Contains(block.Id)))
        {
            Resume();
        }
    }

    private static bool IsDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }

    /// <summary>
    /// Gets the Ctrl, Alt, and Shift state for the current input thread.
    /// </summary>
    /// <returns>The modifiers.</returns>
    internal static VirtualKeyModifiers Modifiers()
    {
        return (IsDown(VirtualKey.Control) ? VirtualKeyModifiers.Control : VirtualKeyModifiers.None)
            | (IsDown(VirtualKey.Menu) ? VirtualKeyModifiers.Menu : VirtualKeyModifiers.None)
            | (IsDown(VirtualKey.Shift) ? VirtualKeyModifiers.Shift : VirtualKeyModifiers.None);
    }

    private DesktopBlockFrame? BlockAt(Point point)
    {
        return Frame?.Blocks.LastOrDefault(block => Contains(_renderer.ContentRect(block, Inset), point));
    }

    private static bool Contains(Rect rect, Point point)
    {
        return point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint current = e.GetCurrentPoint(this);
        PointerPointProperties properties = current.Properties;
        int button = properties.IsLeftButtonPressed ? 1
            : properties.IsMiddleButtonPressed ? 2
            : properties.IsRightButtonPressed ? 3
            : 0;
        e.Handled = true;
        if (PointerDown(current.Position, button, e.KeyModifiers))
        {
            _ = CapturePointer(e.Pointer);
        }
    }

    /// <summary>
    /// Starts a selection, pane resize, link activation, context menu, or reported mouse press.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="button">The button: 1 left, 2 middle, or 3 right.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    /// <returns>Whether the gesture continues and needs the pointer captured.</returns>
    internal bool PointerDown(Point point, int button, VirtualKeyModifiers modifiers)
    {
        FocusTerminal();
        if (button is 2 or 3)
        {
            if (BlockAt(point) is not { } target)
            {
                return false;
            }

            if (target.MouseTracking && !modifiers.HasFlag(VirtualKeyModifiers.Shift))
            {
                _pointerBlock = target.Id;
                _pointerButton = button;
                SendMouse(point, target, "down", button, modifiers);
                return true;
            }

            if (button == 3)
            {
                ShowContextMenu(target, point);
            }

            return false;
        }

        if (button != 1)
        {
            return false;
        }

        long now = Environment.TickCount64;
        _clicks = now - _lastClick <= 500 && Math.Abs(point.X - _lastClickPoint.X) < 4
            && Math.Abs(point.Y - _lastClickPoint.Y) < 4
            ? _clicks + 1
            : 1;
        _lastClick = now;
        _lastClickPoint = point;
        ClearSelection();
        _dragged = false;
        if (ResizeEdge(point) is { } edge)
        {
            _resizing = (edge.Block, edge.Edge, point, 0);
            return true;
        }

        if (Frame?.Blocks.FirstOrDefault(item => item.ViewVersion != 0
            && Contains(_renderer.ResumeRect(_renderer.ContentRect(item, Inset)), point)) is { } inspecting)
        {
            Resume(inspecting.Id);
            return true;
        }

        if (BlockAt(point) is not { } block)
        {
            return true;
        }

        if (!block.Active)
        {
            Send(new DesktopCommand("focus", block.Id));
        }

        int index = CellIndex(block, point);
        if (modifiers.HasFlag(VirtualKeyModifiers.Control) && block.Cells[index].Link is { } link)
        {
            OpenLink(link);
            return true;
        }

        if (block.MouseTracking && block.ScrollOffset == 0 && !modifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            _pointerBlock = block.Id;
            _pointerButton = 1;
            SendMouse(point, block, "down", 1, modifiers);
            return true;
        }

        // A single press only records an anchor; selection starts after deliberate movement.
        _dragged = _clicks > 1;
        if (_clicks == 1)
        {
            _anchor = (block, index, point);
            return true;
        }

        BeginSelection(block, index, _clicks >= 3 ? "line" : "word");
        _selectionPoint = point;
        return true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint current = e.GetCurrentPoint(this);
        PointerMove(current.Position, current.Properties.IsLeftButtonPressed, current.IsInContact, e.KeyModifiers);
    }

    /// <summary>
    /// Continues a drag or reports hover motion to an application that tracks the mouse.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="leftButton">Whether the left button is down.</param>
    /// <param name="contact">Whether any button, pen, or touch is in contact.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    internal void PointerMove(Point point, bool leftButton, bool contact, VirtualKeyModifiers modifiers)
    {
        if (_resizing is { } drag)
        {
            bool right = drag.Edge == "right";
            int cells = (int)((right ? point.X - drag.Origin.X : point.Y - drag.Origin.Y)
                / (right ? Font.CellWidth : Font.CellHeight));
            if (cells != drag.Sent)
            {
                Send(new DesktopCommand("resizePane", drag.Block, drag.Edge, X: cells - drag.Sent));
                _resizing = drag with { Sent = cells };
            }

            return;
        }

        if (_pointerBlock is not null
            && Frame?.Blocks.FirstOrDefault(block => block.Id == _pointerBlock) is { } tracked)
        {
            SendMouse(point, tracked, "drag", _pointerButton, modifiers);
            return;
        }

        if (leftButton && (_anchor is not null || _selectionBlock is not null))
        {
            ExtendSelectionGesture(point);
            return;
        }

        ProtectedCursor = ResizeEdge(point) is { } edge
            ? InputSystemCursor.Create(edge.Edge == "right"
                ? InputSystemCursorShape.SizeWestEast
                : InputSystemCursorShape.SizeNorthSouth)
            : InputSystemCursor.Create(InputSystemCursorShape.IBeam);
        if (BlockAt(point) is { MouseTracking: true } hovered && !modifiers.HasFlag(VirtualKeyModifiers.Shift)
            && !contact)
        {
            SendMouse(point, hovered, "move", 0, modifiers);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        PointerUp(e.GetCurrentPoint(this).Position, e.KeyModifiers);
        ReleasePointerCapture(e.Pointer);
    }

    /// <summary>
    /// Ends a gesture; a click without deliberate movement clears any selection.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    internal void PointerUp(Point point, VirtualKeyModifiers modifiers)
    {
        _resizing = null;
        EndGesture();
        if (_pointerBlock is not null
            && Frame?.Blocks.FirstOrDefault(block => block.Id == _pointerBlock) is { } tracked)
        {
            SendMouse(point, tracked, "up", _pointerButton, modifiers);
        }

        _pointerBlock = null;
        if (!_dragged && _clicks <= 1)
        {
            ClearSelection();
        }
    }

    private void ExtendSelectionGesture(Point point)
    {
        if (_anchor is { } anchor)
        {
            double distance = Math.Sqrt(Math.Pow(point.X - anchor.Point.X, 2) + Math.Pow(point.Y - anchor.Point.Y, 2));
            if (distance < 3)
            {
                return;
            }

            BeginSelection(anchor.Block, anchor.Index, "cell");
            _anchor = null;
        }

        if (_selectionBlock is null
            || Frame?.Blocks.FirstOrDefault(block => block.Id == _selectionBlock) is not { } block)
        {
            return;
        }

        _dragged = true;
        _selectionPoint = point;
        ExtendSelection(block, point);
        Rect content = _renderer.ContentRect(block, Inset);
        bool outside = point.Y < content.Y || point.Y >= content.Bottom;
        if (outside && !block.AlternateScreen && !_autoscroll.IsRunning)
        {
            _autoscroll.Start();
        }
        else if (!outside)
        {
            _autoscroll.Stop();
        }
    }

    private void BeginSelection(DesktopBlockFrame block, int index, string kind)
    {
        _selectionBlock = block.Id;
        _ = _pendingSelections.Add(block.Id);
        Send(new DesktopCommand("select", block.Id, kind, X: index % block.Width, Y: index / block.Width));
    }

    private void ExtendSelection(DesktopBlockFrame block, Point point)
    {
        int index = CellIndex(block, point);
        Send(new DesktopCommand("select", block.Id, "extend", X: index % block.Width, Y: index / block.Width));
    }

    /// <summary>
    /// Autoscroll runs only during an active drag and extends the selection in the same ordered queue.
    /// </summary>
    private void ScrollSelection()
    {
        if (_selectionBlock is null || _selectionPoint is not { } point
            || Frame?.Blocks.FirstOrDefault(block => block.Id == _selectionBlock)
                is not { AlternateScreen: false } block)
        {
            EndGesture();
            return;
        }

        Rect content = _renderer.ContentRect(block, Inset);
        double distance = point.Y < content.Y ? content.Y - point.Y : content.Bottom - point.Y;
        int amount = distance > 0
            ? Math.Clamp((int)(distance / Font.CellHeight), 1, 4)
            : Math.Clamp((int)(distance / Font.CellHeight), -4, -1);
        if ((amount > 0 && block.ScrollOffset < block.HistoryLines) || (amount < 0 && block.ScrollOffset > 0))
        {
            Send(new DesktopCommand("scroll", block.Id, Y: amount));
            ExtendSelection(block, point);
        }
    }

    private void EndGesture()
    {
        _autoscroll.Stop();
        _anchor = null;
        _selectionBlock = null;
        _selectionPoint = null;
    }

    private (string Block, string Edge)? ResizeEdge(Point point)
    {
        IReadOnlyList<DesktopBlockFrame> blocks = Frame?.Blocks ?? [];
        foreach (DesktopBlockFrame left in blocks)
        {
            Rect first = _renderer.ContentRect(left, Inset);
            foreach (Rect second in blocks.Where(block => block.Id != left.Id)
                .Select(block => _renderer.ContentRect(block, Inset)))
            {
                if (Math.Abs(first.Right - second.X) <= Font.CellWidth * 2.5 && first.X < second.X
                    && Math.Abs(point.X - ((first.Right + second.X) / 2)) < 5
                    && point.Y >= Math.Max(first.Y, second.Y) && point.Y <= Math.Min(first.Bottom, second.Bottom))
                {
                    return (left.Id, "right");
                }

                if (Math.Abs(first.Bottom - second.Y) <= Font.CellHeight * 2.5 && first.Y < second.Y
                    && Math.Abs(point.Y - ((first.Bottom + second.Y - Font.CellHeight) / 2)) < 5
                    && point.X >= Math.Max(first.X, second.X) && point.X <= Math.Min(first.Right, second.Right))
                {
                    return (left.Id, "down");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Converts surface points to block cells; the core encodes the negotiated mouse protocol.
    /// </summary>
    private void SendMouse(Point point, DesktopBlockFrame block, string action, int button,
        VirtualKeyModifiers modifiers)
    {
        int index = CellIndex(block, point);
        int encoded = (modifiers.HasFlag(VirtualKeyModifiers.Shift) ? 1 : 0)
            | (modifiers.HasFlag(VirtualKeyModifiers.Menu) ? 2 : 0)
            | (modifiers.HasFlag(VirtualKeyModifiers.Control) ? 4 : 0);
        Send(new DesktopCommand("mouse", block.Id, action, X: index % block.Width, Y: index / block.Width,
            Button: button, Modifiers: encoded));
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint current = e.GetCurrentPoint(this);
        if (!current.Properties.IsHorizontalMouseWheel)
        {
            e.Handled = Wheel(current.Position, current.Properties.MouseWheelDelta, e.KeyModifiers);
        }
    }

    /// <summary>
    /// Scrolls history, or reports the wheel to an application that tracks the mouse or uses the alternate screen.
    /// </summary>
    /// <param name="point">The point in surface coordinates.</param>
    /// <param name="delta">The vertical wheel delta; one detent is 120 and positive scrolls toward history.</param>
    /// <param name="modifiers">The Ctrl, Alt, and Shift state.</param>
    /// <returns>Whether a pane was under the point.</returns>
    internal bool Wheel(Point point, int delta, VirtualKeyModifiers modifiers)
    {
        if (BlockAt(point) is not { } block)
        {
            return false;
        }

        // One detent is 120 units and scrolls three rows; precision touchpads report smaller steps.
        double accumulated = _wheelRemainders.GetValueOrDefault(block.Id) + (delta / 40.0);
        double whole = Math.Truncate(accumulated);
        _wheelRemainders[block.Id] = accumulated - whole;
        int lines = (int)Math.Clamp(whole, -120, 120);
        if (lines == 0)
        {
            return true;
        }

        if (block.MouseTracking && block.ScrollOffset == 0 && !modifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            for (int step = 0; step < Math.Abs(lines); step++)
            {
                SendMouse(point, block, "down", lines > 0 ? 4 : 5, modifiers);
            }
        }
        else if (block.AlternateScreen)
        {
            for (int step = 0; step < Math.Abs(lines); step++)
            {
                Send(new DesktopCommand("key", block.Id, lines > 0 ? "Up" : "Down"));
            }
        }
        else
        {
            Send(new DesktopCommand("scroll", block.Id, Y: lines));
        }

        return true;
    }

    private void ShowContextMenu(DesktopBlockFrame block, Point point)
    {
        var menu = new MenuFlyout();
        string id = block.Id;
        void Add(string text, Action action, bool enabled = true, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            if (shortcut is not null)
            {
                item.KeyboardAcceleratorTextOverride = shortcut;
            }

            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Copy", Copy, SelectedText.Length != 0, WindowsShortcuts.Display("copy"));
        Add("Paste", () => Paste(id), shortcut: WindowsShortcuts.Display("paste"));
        Add("Find…", () => ActionRequested?.Invoke("find", id), shortcut: WindowsShortcuts.Display("find"));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Split Right", () => ActionRequested?.Invoke("splitRight", id),
            shortcut: WindowsShortcuts.Display("splitRight"));
        Add("Split Below", () => ActionRequested?.Invoke("splitBelow", id),
            shortcut: WindowsShortcuts.Display("splitBelow"));
        Add("Rename Pane…", () => ActionRequested?.Invoke("renameBlock", id));
        Add("Close Pane…", () => ActionRequested?.Invoke("closeBlock", id));
        menu.ShowAt(this, new FlyoutShowOptions { Position = point });
    }

    private static void OpenLink(string text)
    {
        // Output alone never opens anything; only an explicit Ctrl+click on an HTTP, HTTPS, or mail link does.
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }
}
