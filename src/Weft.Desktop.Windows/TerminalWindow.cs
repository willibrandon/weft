using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Weft.Client;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Weft.Desktop.Windows;

/// <summary>
/// Coordinates one session attachment, its terminal surface, and window-scoped dialogs.
/// </summary>
/// <remarks>
/// Commands capture their target before confirmation, so later focus changes cannot retarget them.
/// Terminal output updates the surface directly; only chrome state passes through reconciliation.
/// </remarks>
internal sealed class TerminalWindow : Component
{
    private const string TerminalGlyph = "";
    private static readonly string[] s_menuGroups = ["Session", "Tab", "Pane"];

    /// <inheritdoc />
    public override Element Render()
    {
        ElementRef<TerminalSurface> surface = this.UseElementRef<TerminalSurface>();
        Ref<TerminalSession?> session = UseRef<TerminalSession?>(null);
        Ref<Action<string, string?>> perform = UseRef<Action<string, string?>>((_, _) => { });
        Ref<Control?> previousFocus = UseRef<Control?>(null);
        (ChromeState chrome, Action<ChromeState> setChrome) = UseState(ChromeState.Connecting);
        (WindowDialog? dialog, Action<WindowDialog?> setDialog) = UseState<WindowDialog?>(null);
        (string dialogText, Action<string> setDialogText) = UseState(string.Empty);
        (int paletteIndex, Action<int> setPaletteIndex) = UseState(0);
        (string? findTarget, Action<string?> setFindTarget) = UseState<string?>(null);
        (string findText, Action<string> setFindText) = UseState(string.Empty);
        (string? notice, Action<string?> setNotice) = UseState<string?>(null);
        (ElementRef findField, Action focusFind) = this.UseElementFocus();
        (ElementRef dialogField, Action focusDialog) = this.UseElementFocus();
        (int _, Action<int> setPreferencesVersion) = UseState(0);
        ReactorWindow? window = UseWindow();
        bool active = UseIsActive();

        UseEffect(() =>
        {
            if (surface.Current is not { } view)
            {
                return () => { };
            }

            (int columns, int rows) = view.CellGrid == default ? (100, 30) : view.CellGrid;
            var created = new TerminalSession(columns, rows);
            session.Current = created;
            if (created.ConfigurationError is { } configuration)
            {
                setNotice("Your Weft configuration was not read: " + configuration);
            }

            created.FrameArrived += frame =>
            {
                view.Update(frame);
                setChrome(ChromeState.From(frame));
            };
            view.CommandRequested += command =>
            {
                if (!created.Send(command))
                {
                    setNotice("The input was not sent. The connection is closed or busy.");
                }
            };
            view.ActionRequested += (id, target) => perform.Current(id, target);
            view.GridChanged += (columns, rows) => created.Send(new DesktopCommand("resize", Width: columns, Height: rows));
            view.FocusTerminal();
            return () =>
            {
                session.Current = null;
                created.Dispose();
                view.ReleaseResources();
            };
        }, []);

        UseEffect(() =>
        {
            // Menus, shortcuts, and colors follow preference changes made in Settings.
            int version = 0;
            void Changed()
            {
                setPreferencesVersion(++version);
            }

            PreferencesStore.Changed += Changed;
            return () => PreferencesStore.Changed -= Changed;
        }, []);
        UseEffect(() => surface.Current?.SetWindowActive(active), active);
        UseEffect(() =>
        {
            if (window?.NativeWindow is { } native)
            {
                native.Title = chrome.Title + " — Weft";
            }
        }, chrome.Title);
        UseEffect(() =>
        {
            if (window?.AppWindow.TitleBar is { } titleBar)
            {
                titleBar.PreferredTheme = TitleBarTheme.Dark;
            }
        }, []);
        UseEffect(() =>
        {
            if (findTarget is not null)
            {
                focusFind();
            }
        }, findTarget);
        UseEffect(() =>
        {
            if (dialog is { Kind: "commands" or "rename" or "newSession" })
            {
                focusDialog();
            }
        }, dialog);

        TerminalSurface? View()
        {
            return surface.Current;
        }

        void Send(DesktopCommand command)
        {
            if (session.Current?.Send(command) != true)
            {
                setNotice("The input was not sent. The connection is closed or busy.");
            }
        }

        void OpenDialog(WindowDialog request)
        {
            if (dialog is not null)
            {
                return;
            }

            previousFocus.Current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(View()?.XamlRoot) as Control;
            setDialogText(request.Text);
            setPaletteIndex(0);
            setDialog(request);
        }

        void CloseDialog()
        {
            setDialog(null);
            // Dismissal restores the focus that opened the dialog, which is usually the terminal.
            if (previousFocus.Current is TerminalInput or null)
            {
                View()?.FocusTerminal();
            }
            else
            {
                _ = previousFocus.Current.Focus(FocusState.Programmatic);
            }

            previousFocus.Current = null;
        }

        void SelectTab(string id)
        {
            Send(new DesktopCommand("tab", id));
            View()?.FocusTerminal();
        }

        void Perform(string id, string? explicitTarget)
        {
            if (chrome.Commands.FirstOrDefault(action => action.Id == id) is not { } action || !chrome.CanPerform(action))
            {
                return;
            }

            string? target = explicitTarget ?? action.Scope switch
            {
                "session" => chrome.ActiveSession,
                "tab" => chrome.ActiveTab,
                "block" => chrome.ActiveBlock,
                _ => null
            };
            switch (id)
            {
                case "newSession":
                    OpenDialog(new WindowDialog("newSession"));
                    break;
                case "font":
                    DesktopWindows.ShowSettings();
                    break;
                case "commands":
                    OpenDialog(new WindowDialog("commands"));
                    break;
                case "find":
                    setFindText(string.Empty);
                    setFindTarget(target);
                    focusFind();
                    break;
                case "live":
                    View()?.Resume(target);
                    break;
                case "nextTab":
                case "previousTab":
                    int index = chrome.Tabs.ToList().FindIndex(tab => tab.Id == chrome.ActiveTab);
                    if (index >= 0)
                    {
                        int step = id == "nextTab" ? 1 : chrome.Tabs.Count - 1;
                        SelectTab(chrome.Tabs[(index + step) % chrome.Tabs.Count].Id);
                    }

                    break;
                case "renameSession":
                case "renameTab":
                case "renameBlock":
                    IReadOnlyList<ChromeItem> named = id == "renameTab" ? chrome.Tabs : chrome.Blocks;
                    string current = id == "renameSession"
                        ? chrome.Title
                        : named.FirstOrDefault(item => item.Id == target)?.Name ?? string.Empty;
                    OpenDialog(new WindowDialog("rename", id, target, current));
                    break;
                case "closeSession":
                case "closeTab":
                case "closeBlock":
                case "sync":
                    OpenDialog(new WindowDialog("confirm", id, target));
                    break;
                default:
                    Send(new DesktopCommand(id, target));
                    break;
            }
        }

        void Run(string id)
        {
            TerminalSurface? view = View();
            switch (id)
            {
                case "newWindow":
                    DesktopWindows.OpenTerminal();
                    break;
                case "settings":
                    DesktopWindows.ShowSettings();
                    break;
                case "copy":
                    view?.Copy();
                    break;
                case "paste":
                    view?.Paste();
                    break;
                case "selectAll":
                    view?.SelectAll();
                    break;
                case "largerText":
                    view?.ChangeFontSize(1);
                    break;
                case "smallerText":
                    view?.ChangeFontSize(-1);
                    break;
                case "closeWindow":
                    window?.Close();
                    break;
                case "exit":
                    DesktopWindows.Exit();
                    break;
                case "help":
                case "about":
                    OpenDialog(new WindowDialog(id));
                    break;
                default:
                    Perform(id, null);
                    break;
            }
        }

        perform.Current = Perform;

        void OnKey(object sender, KeyRoutedEventArgs e)
        {
            var chord = new ShortcutChord(ShortcutChord.Normalize(e.Key), TerminalSurface.Modifiers());
            if (WindowsShortcuts.Match(chord, chrome.Commands) is not { } id)
            {
                return;
            }

            // Clipboard and editing commands follow focus; a text field keeps its own editing keys.
            if (id is "copy" or "paste" or "selectAll" && View()?.HasInputFocus != true)
            {
                return;
            }

            e.Handled = true;
            Run(id);
        }

        void Search(string query, int direction)
        {
            View()?.ClearSelection();
            Send(new DesktopCommand("find", findTarget, query.Length > 1024 ? query[..1024] : query, Y: direction));
        }

        void CloseFind()
        {
            string? target = findTarget;
            setFindTarget(null);
            setFindText(string.Empty);
            View()?.Resume(target);
            View()?.FocusTerminal();
        }

        bool connecting = ReferenceEquals(chrome, ChromeState.Connecting);
        string? status = chrome.Error ?? notice ?? (connecting ? "Connecting…" : chrome.Connected ? null : "Disconnected");
        ChromeItem? findPane = chrome.Blocks.FirstOrDefault(block => block.Id == findTarget);
        Element? find = findTarget is null
            ? null
            : FindBar(findText, findPane, findField, value =>
                {
                    setFindText(value);
                    Search(value, 0);
                }, direction => Search(findText, direction), CloseFind)
                .Grid(row: 1);
        Element? empty = chrome.Connected && chrome.ActiveSession is null
            ? Button("New Session…", () => Perform("newSession", null))
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 2)
            : null;
        Element? statusLine = status is null
            ? null
            : TextBlock(status)
                .Foreground(Theme.SystemCritical)
                .FontSize(12)
                .ToolTip(status)
                .AutomationName("Connection status")
                .Margin(12, 3, 12, 5)
                .Grid(row: 3);
        var dialogs = new DialogContext(chrome, dialogText, setDialogText, paletteIndex, setPaletteIndex, dialogField,
            CloseDialog, Run, Send);

        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
            TitleBar(chrome, Run, Perform, SelectTab, Send).Grid(row: 0),
            find,
            TerminalSurfaceElement.TerminalSurface().Ref(surface).Margin(2).Grid(row: 2),
            empty,
            statusLine,
            Dialog(dialog, dialogs))
            .Background(TerminalAppearance.Format(TerminalAppearance.Background))
            .RequestedTheme(ElementTheme.Dark)
            .OnPreviewKeyDown(OnKey);
    }

    private static TitleBarElement TitleBar(ChromeState chrome, Action<string> run, Action<string, string?> perform,
        Action<string> selectTab, Action<DesktopCommand> send)
    {
        int selected = chrome.Tabs.ToList().FindIndex(tab => tab.Id == chrome.ActiveTab);
        MenuFlyoutItemBase[] sessionItems =
        [
            .. chrome.Sessions.Select(item => (MenuFlyoutItemBase)RadioMenuItem(item.Name, "sessions",
                item.Id == chrome.ActiveSession, () =>
                {
                    if (item.Id != chrome.ActiveSession)
                    {
                        send(new DesktopCommand("session", item.Id));
                    }
                })),
            MenuSeparator(),
            Item(chrome, "newSession", perform),
            Item(chrome, "renameSession", perform),
            Item(chrome, "closeSession", perform)
        ];
        Element sessions = Button(HStack(8,
                Icon(FontIcon("", fontSize: 14)),
                TextBlock(chrome.Title).MaxWidth(150),
                Icon(FontIcon("", fontSize: 10))))
            .SubtleButton()
            .IsEnabled(chrome.Connected)
            .ToolTip("Switch or create a session")
            .AutomationName("Sessions")
            .WithFlyout(MenuItems(sessionItems))
            .VAlign(VerticalAlignment.Center);
        TabViewItemData[] items =
        [
            .. chrome.Tabs.Select(tab => Tab(tab.Name, Empty()) with { Icon = TerminalGlyph, IsClosable = chrome.Connected })
        ];
        TabViewElement tabView = TabView((int?)selected, index =>
        {
            if (index >= 0 && index < chrome.Tabs.Count && chrome.Tabs[index].Id != chrome.ActiveTab)
            {
                selectTab(chrome.Tabs[index].Id);
            }
        }, items) with
        {
            IsAddTabButtonVisible = true,
            OnAddTabButtonClick = () => perform("newTab", null),
            OnTabCloseRequested = index =>
            {
                // Closing an inactive tab confirms against that tab without selecting it first.
                if (index >= 0 && index < chrome.Tabs.Count)
                {
                    perform("closeTab", chrome.Tabs[index].Id);
                }
            },
            CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.OnPointerOver,
            TabWidthMode = TabViewWidthMode.Equal
        };
        // Tabs do not join a content area below, so they share the title bar's centerline with the other controls.
        Element tabs = tabView.AutomationName("Tabs").VAlign(VerticalAlignment.Center).HAlign(HorizontalAlignment.Left);
        Element actions = Button(Icon(FontIcon("", fontSize: 16)))
            .SubtleButton()
            .ToolTip("Terminal actions")
            .AutomationName("Terminal actions")
            .WithFlyout(MenuItems(Menu(chrome, run, perform)))
            .VAlign(VerticalAlignment.Center)
            .Margin(0, 0, 4, 0);
        return (Factories.TitleBar(string.Empty) with
        {
            // Tabs take only the room they need; the empty strip after them keeps the window draggable.
            Content = FlexRow(sessions, tabs.Flex(shrink: 1), Border(Empty()).Flex(grow: 1, minWidth: 48)),
            RightHeader = actions,
            HeightOption = WindowTitleBarHeight.Tall
        }).AutoRefreshDragRegions();
    }

    private static MenuFlyoutItemBase[] Menu(ChromeState chrome, Action<string> run, Action<string, string?> perform)
    {
        MenuFlyoutItemBase Fixed(string text, string id)
        {
            return Shortcut(MenuItem(text, () => run(id)), id);
        }

        MenuFlyoutItemBase[] Group(string group)
        {
            return [.. chrome.Commands.Where(action => action.Group == group).Select(action => Item(chrome, action.Id, perform))];
        }

        List<MenuFlyoutItemBase> items = [Fixed("New Window", "newWindow"), MenuSeparator()];
        items.AddRange(s_menuGroups.Select(group => MenuSubItem(group, Group(group))));
        items.Add(MenuSubItem("Edit",
            [Fixed("Copy", "copy"), Fixed("Paste", "paste"), Fixed("Select All", "selectAll"), MenuSeparator(), .. Group("Edit")]));
        items.Add(MenuSubItem("View",
            [Fixed("Larger Text", "largerText"), Fixed("Smaller Text", "smallerText"), MenuSeparator(), .. Group("View")]));
        items.Add(MenuSeparator());
        items.Add(Fixed("Settings", "settings"));
        items.Add(MenuSubItem("Help", [MenuItem("Weft Help", () => run("help")), MenuItem("About Weft", () => run("about"))]));
        items.Add(MenuSeparator());
        items.Add(MenuItem("Close Window", () => run("closeWindow")) with
        {
            KeyboardAccelerators = [new KeyboardAcceleratorData(VirtualKey.F4, VirtualKeyModifiers.Menu)]
        });
        items.Add(MenuItem("Exit Weft", () => run("exit")));
        return [.. items];
    }

    private static MenuFlyoutItemBase Item(ChromeState chrome, string id, Action<string, string?> perform)
    {
        if (chrome.Commands.FirstOrDefault(item => item.Id == id) is not { } action)
        {
            return MenuSeparator();
        }

        MenuFlyoutItemData item = MenuItem(action.Label, () => perform(id, null)) with { IsEnabled = chrome.CanPerform(action) };
        return Shortcut(item, id);
    }

    private static MenuFlyoutItemData Shortcut(MenuFlyoutItemData item, string id)
    {
        // Menus show the shortcut; the window's key handler runs it once before any control sees the key.
        return ShortcutChord.Parse(WindowsShortcuts.Value(id)) is { } chord
            ? item with { KeyboardAccelerators = [new KeyboardAcceleratorData(chord.Key, chord.Modifiers)] }
            : item;
    }

    private static FlexElement FindBar(string text, ChromeItem? pane, ElementRef field, Action<string> changed, Action<int> search,
        Action close)
    {
        string count = pane is { SearchQuery.Length: > 0 } ? pane.SearchMatches + " matching lines" : string.Empty;
        return FlexRow(
            TextBox(text, changed, placeholderText: "Find in terminal")
                .AutomationName("Find in terminal")
                .OnPreviewKeyDown((_, e) =>
                {
                    if (e.Key == VirtualKey.Enter)
                    {
                        search(TerminalSurface.Modifiers().HasFlag(VirtualKeyModifiers.Shift) ? -1 : 1);
                        e.Handled = true;
                    }
                    else if (e.Key == VirtualKey.Escape)
                    {
                        close();
                        e.Handled = true;
                    }
                })
                .Width(260)
                .Ref(field),
            TextBlock(count).FontSize(12).Opacity(0.7).VAlign(VerticalAlignment.Center).Margin(8, 0, 8, 0).Flex(grow: 1),
            Button("Previous", () => search(-1)),
            Button("Next", () => search(1)).Margin(6, 0, 0, 0),
            Button("Done", close).Margin(6, 0, 0, 0))
            .FlexPadding(12, 6, 12, 6);
    }

    private static Element? Dialog(WindowDialog? dialog, DialogContext context)
    {
        return dialog?.Kind switch
        {
            "commands" => CommandPalette.Render(context),
            "newSession" => NameDialog("New Session", "Create", "Session name", context, text =>
                context.Send(new DesktopCommand("newSession", Text: text.Trim().Length == 0 ? null : text.Trim()))),
            "rename" => NameDialog(Label(context.Chrome, dialog.Action).TrimEnd('…'), "Rename", "Name", context, text =>
                context.Send(new DesktopCommand(dialog.Action!, dialog.Target, text))),
            "confirm" => ConfirmDialog(dialog, context),
            "help" => Message("Weft Help",
                "Click + for a tab, or open Commands from the … menu to find an action. Scroll to read earlier output; "
                + "Find searches it. Drag to select text, then copy with Ctrl+Shift+C. Hold Shift to select inside an app "
                + "that uses the mouse, and Ctrl+click a link to open it. Change shortcuts in Settings. Closing a window or "
                + "exiting Weft leaves your sessions running.", context),
            "about" => Message("About Weft",
                "Version " + (typeof(TerminalWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")
                + ". Durable terminal sessions that keep running when every window closes.", context),
            _ => null
        };
    }

    private static Element NameDialog(string title, string primary, string placeholder, DialogContext context, Action<string> submit)
    {
        Element field = TextBox(context.Text, context.SetText, placeholderText: placeholder)
            .AutomationName(placeholder)
            .Ref(context.Field);
        return ContentDialog(title, field, primary) with
        {
            IsOpen = true,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary)
                {
                    submit(context.Text);
                }

                context.Close();
            }
        };
    }

    private static Element ConfirmDialog(WindowDialog dialog, DialogContext context)
    {
        bool sync = dialog.Action == "sync";
        string title = sync ? "Change input broadcasting?" : Label(context.Chrome, dialog.Action).TrimEnd('…') + "?";
        string message = sync
            ? "When enabled, typing goes to every included pane in this tab."
            : "This ends the running processes inside it. Closing the window instead keeps them running.";
        return ContentDialog(title, TextBlock(message).TextWrapping(TextWrapping.Wrap), sync ? "Change Broadcasting" : "End Processes and Close") with
        {
            IsOpen = true,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary)
                {
                    context.Send(new DesktopCommand(dialog.Action!, dialog.Target));
                }

                context.Close();
            }
        };
    }

    private static Element Message(string title, string text, DialogContext context)
    {
        return ContentDialog(title, TextBlock(text).TextWrapping(TextWrapping.Wrap), "Close") with
        {
            IsOpen = true,
            OnClosed = _ => context.Close()
        };
    }

    private static string Label(ChromeState chrome, string? id)
    {
        return chrome.Commands.FirstOrDefault(action => action.Id == id)?.Label ?? string.Empty;
    }
}
