using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// A Win32 point in physical pixels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    /// <summary>
    /// The horizontal coordinate.
    /// </summary>
    public int X;

    /// <summary>
    /// The vertical coordinate.
    /// </summary>
    public int Y;
}
