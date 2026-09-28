using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows;

/// <summary>
/// Declares the Win32 functions the app calls where WinUI has no equivalent.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// The error returned for a process that does not run from a package.
    /// </summary>
    internal const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// Gets the calling process's package full name.
    /// </summary>
    /// <param name="length">The buffer length in characters, including the terminator; receives the length needed.</param>
    /// <param name="name">The buffer, or null to query the length.</param>
    /// <returns>A Win32 error code, or zero.</returns>
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetCurrentPackageFullName(ref int length, char* name);

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
