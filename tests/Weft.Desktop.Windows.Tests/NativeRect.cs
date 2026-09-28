using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// A Win32 rectangle in physical pixels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    /// <summary>
    /// The left edge.
    /// </summary>
    public int Left;

    /// <summary>
    /// The top edge.
    /// </summary>
    public int Top;

    /// <summary>
    /// The right edge.
    /// </summary>
    public int Right;

    /// <summary>
    /// The bottom edge.
    /// </summary>
    public int Bottom;
}
