namespace Weft.Client;

/// <summary>
/// Describes a discoverable desktop operation independently of platform shortcuts.
/// </summary>
/// <param name="Id">The command identifier.</param>
/// <param name="Label">The user-facing name.</param>
/// <param name="Group">The menu group.</param>
/// <param name="Scope">The required session, tab, block, or window scope.</param>
/// <param name="Destructive">Whether the command ends running processes.</param>
public sealed record DesktopAction(string Id, string Label, string Group, string Scope, bool Destructive = false);
