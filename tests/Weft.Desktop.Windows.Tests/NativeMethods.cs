using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Declares the Win32 functions that locate a window's client area within its captured frame.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// The window attribute for its visible frame, excluding invisible resize borders.
    /// </summary>
    internal const int ExtendedFrameBounds = 9;

    /// <summary>
    /// Converts a window's client coordinate to a screen coordinate in physical pixels.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="point">The point to convert in place.</param>
    /// <returns>Whether the conversion succeeded.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint window, ref NativePoint point);

    /// <summary>
    /// Reads a window attribute kept by the desktop window manager.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">Receives the value.</param>
    /// <param name="size">The value's size in bytes.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
}
