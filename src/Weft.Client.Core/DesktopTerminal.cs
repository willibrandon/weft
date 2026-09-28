using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;
using Hex1b.Theming;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// Maintains a visible block's terminal state through the public HMP1 client API.
/// </summary>
internal sealed class DesktopTerminal : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Hex1bTerminal _terminal;
    private readonly TerminalWidgetHandle _handle;
    private readonly Task _run;
    private readonly DesktopPresentation _presentation;
    private readonly DesktopSixel _sixel = new();
    private Hex1bTerminalSnapshot? _history;
    private int _scrollOffset;
    private long _viewVersion;
    private string _searchQuery = string.Empty;
    private int _searchMatches;
    private int _searchRow = -1;
    private DesktopSelection? _selection;
    private int _anchorStart;
    private int _anchorEnd;
    private string _selectionKind = "cell";
    private int _viewportWidth;
    private int _viewportHeight;

    /// <summary>
    /// Connects to a real block and starts observing terminal output.
    /// </summary>
    /// <param name="block">The server block.</param>
    /// <param name="invalidate">Marks the desktop frame as changed.</param>
    internal DesktopTerminal(BlockInfo block, Action invalidate)
    {
        _viewportWidth = block.Width;
        _viewportHeight = block.Height;
        Hex1bTerminalBuilder builder = Hex1bTerminal.CreateBuilder()
            .WithDimensions(block.Width, block.Height)
            .WithScrollback(Math.Min(10_000, 2_000_000 / Math.Max(1, block.Width)))
            .WithGraphics(options =>
            {
                options.MaximumRetainedBytesPerScreen = 64L * 1024 * 1024;
                // Reserve room for both raster tiles and dense pixels before lazy materialization.
                options.MaximumRetainedLogicalPixelsPerScreen = 8L * 1024 * 1024;
            })
            .WithHmp1UdsClient(block.SocketPath, options =>
            {
                options.DisplayName = "Weft desktop";
                options.DefaultRole = Hmp1Role.Secondary;
                options.OnRemoteResized = (args, _) =>
                {
                    _terminal?.Resize(args.Width, args.Height);
                    invalidate();
                    return Task.CompletedTask;
                };
            })
            .WithTerminalWidget(out _handle);
        _presentation = new DesktopPresentation(_handle, invalidate);
        _terminal = builder.WithPresentation(_presentation).Build();
        _presentation.Initialize();
        _run = RunAsync(invalidate);
    }

    /// <summary>
    /// Gets a transport failure, if the block connection ended unexpectedly.
    /// </summary>
    internal string? Error { get; private set; }

    /// <summary>
    /// Captures cells without interpreting terminal escape sequences in the frontend.
    /// </summary>
    /// <param name="block">The current block metadata.</param>
    /// <param name="placement">The block geometry.</param>
    /// <param name="frameSize">The server frame thickness.</param>
    /// <param name="active">Whether the tab's current focus names this block.</param>
    /// <returns>The immutable frame.</returns>
    internal DesktopBlockFrame Capture(BlockInfo block, BlockPlacement placement, int frameSize, bool active)
    {
        return _presentation.ReadSnapshot(live => Capture(block, placement, frameSize, active, live));
    }

    private DesktopBlockFrame Capture(BlockInfo block, BlockPlacement placement, int frameSize, bool active, Hex1bTerminalSnapshot live)
    {
        _viewportWidth = live.Width;
        _viewportHeight = live.Height;
        if (_history is not null && (_history.InAlternateScreen != live.InAlternateScreen
            || _history.Width != live.Width || _history.Height - _history.ScrollbackLineCount != live.Height))
        {
            string query = _searchQuery;
            Resume();
            if (query.Length != 0)
            {
                Find(query, 0);
            }
        }

        Hex1bTerminalSnapshot snapshot = _history ?? live;
        int historyLines = _history?.ScrollbackLineCount ?? _terminal.ScrollbackCount;
        int top = _history is null ? 0 : Math.Max(0, snapshot.ScrollbackLineCount - _scrollOffset);
        var cells = new DesktopCell[live.Width * live.Height];
        for (int y = 0; y < live.Height; y++)
        {
            for (int x = 0; x < live.Width; x++)
            {
                TerminalCell cell = snapshot.GetCell(x, top + y);
                cells[(y * live.Width) + x] = new DesktopCell(cell.Character, Color(cell.Foreground), Color(cell.Background),
                    (int)cell.Attributes, cell.HyperlinkData?.Uri);
            }
        }

        IReadOnlyList<DesktopImage> images = DesktopGraphics.Capture(snapshot, top, live.Height, _sixel);
        return new DesktopBlockFrame(block.Id, block.Title, active, placement.X + frameSize, placement.Y + frameSize,
            live.Width, live.Height, live.CursorX, live.CursorY, live.CursorVisible && _history is null, _presentation.CursorShape, cells,
            live.InAlternateScreen ? 0 : historyLines, _scrollOffset, live.InAlternateScreen, _handle.MouseTrackingEnabled,
            _history is null ? 0 : _viewVersion, _searchQuery, _searchMatches)
        {
            Images = images,
            Textures = [.. images.DistinctBy(image => (image.Key, image.Format, image.PixelWidth, image.PixelHeight))
                .Select(image => new DesktopTexture(image.Key, image.Data, image.Format, image.PixelWidth, image.PixelHeight))],
            Selection = _selection is null ? null : _selection with { Start = _selection.Start - (top * snapshot.Width), End = _selection.End - (top * snapshot.Width) }
        };
    }

    /// <summary>
    /// Moves through a bounded, stable history snapshot while output continues in the terminal.
    /// </summary>
    /// <param name="offset">The number of rows above the live screen, or a relative movement.</param>
    /// <param name="relative">Whether the offset is relative to the current position.</param>
    internal void Scroll(int offset, bool relative)
    {
        if (_handle.InAlternateScreen)
        {
            return;
        }

        RetainHistory();

        _scrollOffset = (int)Math.Clamp(relative ? (long)_scrollOffset + offset : offset, 0, _history.ScrollbackLineCount);
        if (_scrollOffset == 0 && _selection is null)
        {
            Resume();
        }
    }

    /// <summary>
    /// Releases history inspection and follows current output again.
    /// </summary>
    internal void Resume()
    {
        _history?.Dispose();
        _history = null;
        _scrollOffset = 0;
        _searchQuery = string.Empty;
        _searchMatches = 0;
        _searchRow = -1;
        _selection = null;
    }

    /// <summary>
    /// Finds literal text in retained history without executing patterns supplied by terminal output.
    /// </summary>
    /// <param name="query">The case-insensitive search text.</param>
    /// <param name="direction">Negative selects the previous row; positive selects the next.</param>
    internal void Find(string query, int direction)
    {
        if (query.Length == 0)
        {
            Resume();
            return;
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, 1024);
        RetainHistory();
        _selection = null;

        if (!string.Equals(query, _searchQuery, StringComparison.Ordinal))
        {
            _searchRow = -1;
        }
        _searchQuery = query;
        List<int> matches = [];
        for (int row = 0; row < _history.Height; row++)
        {
            if (_history.GetLineTrimmed(row).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(row);
            }
        }
        _searchMatches = matches.Count;
        if (matches.Count == 0)
        {
            return;
        }
        int index = matches.IndexOf(_searchRow);
        index = index < 0 ? (direction < 0 ? matches.Count - 1 : 0)
            : (index + (direction < 0 ? matches.Count - 1 : Math.Min(direction, 1))) % matches.Count;
        _searchRow = matches[index];
        _scrollOffset = Math.Clamp(_history.ScrollbackLineCount - _searchRow + ((_history.Height - _history.ScrollbackLineCount) / 2), 0, _history.ScrollbackLineCount);
    }

    /// <summary>
    /// Starts or extends selection within a retained snapshot, independent of later process output.
    /// </summary>
    /// <param name="kind">Cell, word, line, all, extend, or clear.</param>
    /// <param name="x">The pointer column in the viewport.</param>
    /// <param name="y">The pointer row in the viewport.</param>
    internal void Select(string kind, int x, int y)
    {
        if (kind == "clear")
        {
            _selection = null;
            if (_scrollOffset == 0 && _searchQuery.Length == 0)
            {
                Resume();
            }
            return;
        }
        if (kind is not ("cell" or "word" or "line" or "all" or "extend"))
        {
            throw new ArgumentException("Invalid selection operation.", nameof(kind));
        }
        if (kind == "extend" && _selection is null)
        {
            return;
        }

        RetainHistory();
        Hex1bTerminalSnapshot snapshot = _history;
        int viewportHeight = snapshot.Height - snapshot.ScrollbackLineCount;
        int row = Math.Clamp(snapshot.ScrollbackLineCount - _scrollOffset + Math.Clamp(y, 0, viewportHeight - 1), 0, snapshot.Height - 1);
        int column = Math.Clamp(x, 0, snapshot.Width - 1);
        int index = (row * snapshot.Width) + column;
        if (kind != "extend")
        {
            _selectionKind = kind;
            (_anchorStart, _anchorEnd) = SelectionUnit(snapshot, index, kind);
        }
        (int start, int end) = SelectionUnit(snapshot, index, _selectionKind);
        start = Math.Min(start, _anchorStart);
        end = Math.Max(end, _anchorEnd);
        if (_selection?.Start == start && _selection.End == end)
        {
            return;
        }
        var text = new StringBuilder();
        for (int line = start / snapshot.Width; line <= end / snapshot.Width; line++)
        {
            int first = line == start / snapshot.Width ? start % snapshot.Width : 0;
            int last = line == end / snapshot.Width ? end % snapshot.Width : snapshot.Width - 1;
            var content = new StringBuilder();
            for (int cell = first; cell <= last; cell++)
            {
                _ = content.Append(snapshot.GetCell(cell, line).Character);
            }
            bool hardBreak = last == snapshot.Width - 1 && !snapshot.GetCell(last, line).IsSoftWrap;
            _ = text.Append(hardBreak ? content.ToString().TrimEnd(' ') : content.ToString());
            if (hardBreak && line < end / snapshot.Width)
            {
                _ = text.Append('\n');
            }
        }
        _selection = new DesktopSelection(start, end, text.ToString());
    }

    [MemberNotNull(nameof(_history))]
    private void RetainHistory()
    {
        if (_history is null)
        {
            _history = _terminal.CreateSnapshot(Math.Min(10_000, 2_000_000 / Math.Max(1, _viewportWidth)));
            _viewVersion++;
        }
    }

    private static (int Start, int End) SelectionUnit(Hex1bTerminalSnapshot snapshot, int index, string kind)
    {
        int row = index / snapshot.Width;
        int first = index % snapshot.Width;
        int last = first;
        if (kind == "all")
        {
            return (0, (snapshot.Height * snapshot.Width) - 1);
        }
        if (kind == "line")
        {
            return (row * snapshot.Width, ((row + 1) * snapshot.Width) - 1);
        }
        while (first > 0 && snapshot.GetCell(first, row).Character.Length == 0)
        {
            first--;
        }
        while (last + 1 < snapshot.Width && snapshot.GetCell(last + 1, row).Character.Length == 0)
        {
            last++;
        }
        if (kind == "word")
        {
            bool spaces = string.IsNullOrWhiteSpace(snapshot.GetCell(first, row).Character);
            while (first > 0 && (snapshot.GetCell(first - 1, row).Character.Length == 0
                || string.IsNullOrWhiteSpace(snapshot.GetCell(first - 1, row).Character) == spaces))
            {
                first--;
            }
            while (last + 1 < snapshot.Width && (snapshot.GetCell(last + 1, row).Character.Length == 0
                || string.IsNullOrWhiteSpace(snapshot.GetCell(last + 1, row).Character) == spaces))
            {
                last++;
            }
        }
        return ((row * snapshot.Width) + first, (row * snapshot.Width) + last);
    }

    /// <summary>
    /// Encodes pointer input using the terminal's negotiated application modes.
    /// </summary>
    /// <param name="command">The pointer request.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The input write task.</returns>
    internal Task MouseAsync(DesktopCommand command, CancellationToken token)
    {
        return !Enum.TryParse(command.Text, ignoreCase: true, out MouseAction action) || !Enum.IsDefined(action)
            || !Enum.IsDefined((MouseButton)command.Button) || (command.Modifiers & ~7) != 0
            ? throw new ArgumentException("Invalid terminal mouse event.", nameof(command))
            : _history is null ? _handle.SendEventAsync(new Hex1bMouseEvent((MouseButton)command.Button, action,
            Math.Clamp(command.X, 0, _viewportWidth - 1), Math.Clamp(command.Y, 0, _viewportHeight - 1),
            (Hex1bModifiers)command.Modifiers), token) : Task.CompletedTask;
    }

    /// <summary>
    /// Ends this secondary connection without terminating the server block.
    /// </summary>
    /// <returns>A task that completes once the terminal is released.</returns>
    public async ValueTask DisposeAsync()
    {
        Resume();
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _run.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        await _terminal.DisposeAsync().ConfigureAwait(false);
        await _presentation.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task RunAsync(Action invalidate)
    {
        try
        {
            _ = await _terminal.RunAsync(_stopping.Token).ConfigureAwait(false);
            Error = "The terminal connection closed. Reopen the window to reconnect.";
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            ClientLog.Debug("Desktop terminal detached.");
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Error = exception.Message;
        }
        finally
        {
            if (!_stopping.IsCancellationRequested)
            {
                invalidate();
            }
        }
    }

    private static int? Color(Hex1bColor? color)
    {
        return color is { IsDefault: false } value ? (value.R << 16) | (value.G << 8) | value.B : null;
    }
}
