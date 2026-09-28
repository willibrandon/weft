using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows;

/// <summary>
/// Mirrors a Win32 POINT in physical pixels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    /// <summary>
    /// Gets or sets the horizontal coordinate.
    /// </summary>
    internal int X { get; set; }

    /// <summary>
    /// Gets or sets the vertical coordinate.
    /// </summary>
    internal int Y { get; set; }
}
