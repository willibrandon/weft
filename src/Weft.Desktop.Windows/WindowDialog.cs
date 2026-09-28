namespace Weft.Desktop.Windows;

/// <summary>
/// One modal request in a terminal window; commands capture their target before confirmation.
/// </summary>
/// <param name="Kind">The dialog: commands, newSession, rename, confirm, help, or about.</param>
/// <param name="Action">The catalog action being renamed or confirmed.</param>
/// <param name="Target">The session, tab, or pane the action applies to.</param>
/// <param name="Text">The initial name for a rename.</param>
internal sealed record WindowDialog(string Kind, string? Action = null, string? Target = null, string Text = "");
