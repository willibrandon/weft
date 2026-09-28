using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Wrappers;

namespace Weft.Desktop.Windows;

/// <summary>
/// Mounts the terminal surface as a stable native control that frames update imperatively.
/// </summary>
[GenerateReactorWrapper(typeof(TerminalSurface))]
internal sealed partial record TerminalSurfaceElement
{
    /// <summary>
    /// Gets content the wrapper never assigns; the surface builds its own visual tree.
    /// </summary>
    public Element? Content { get; init; }
}
