using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows;

/// <summary>
/// Declares the Win32 functions the app calls where WinUI has no equivalent.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// Converts a window's client coordinate to a screen coordinate in physical pixels.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="point">The point to convert in place.</param>
    /// <returns>Whether the conversion succeeded.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint window, ref NativePoint point);
}
