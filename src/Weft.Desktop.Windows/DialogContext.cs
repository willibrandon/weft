using Microsoft.UI.Reactor.Input;
using Weft.Client;

namespace Weft.Desktop.Windows;

/// <summary>
/// The window state and callbacks a modal dialog reads and invokes.
/// </summary>
/// <param name="Chrome">The window's chrome state.</param>
/// <param name="Text">The dialog's text field value.</param>
/// <param name="SetText">Updates the text field value.</param>
/// <param name="Selected">The selected command in the command panel.</param>
/// <param name="SetSelected">Updates the selected command.</param>
/// <param name="Field">The field that receives focus when the dialog opens.</param>
/// <param name="Close">Dismisses the dialog and restores the previous focus.</param>
/// <param name="Run">Runs an app command or catalog action.</param>
/// <param name="Send">Sends a command to the session.</param>
internal sealed record DialogContext(ChromeState Chrome, string Text, Action<string> SetText, int Selected,
    Action<int> SetSelected, ElementRef Field, Action Close, Action<string> Run, Action<DesktopCommand> Send);
