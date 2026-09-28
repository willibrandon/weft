using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Weft.Server;

/// <summary>
/// Declares the Windows console functions the server uses before hosting pseudo-terminal children.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// The standard input handle identifier.
    /// </summary>
    internal const int StandardInput = -10;

    /// <summary>
    /// The standard output handle identifier.
    /// </summary>
    internal const int StandardOutput = -11;

    /// <summary>
    /// The standard error handle identifier.
    /// </summary>
    internal const int StandardError = -12;

    /// <summary>
    /// Requests read and write access.
    /// </summary>
    internal const uint ReadWrite = 0xC0000000;

    /// <summary>
    /// Shares a console buffer for reading and writing.
    /// </summary>
    internal const uint ShareReadWrite = 3;

    /// <summary>
    /// Opens an existing device.
    /// </summary>
    internal const uint OpenExisting = 3;

    /// <summary>
    /// Reads one of the process's standard handles.
    /// </summary>
    /// <param name="handle">The standard handle identifier.</param>
    /// <returns>The handle, or zero when none is set.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetStdHandle(int handle);

    /// <summary>
    /// Replaces one of the process's standard handles for processes created later.
    /// </summary>
    /// <param name="handle">The standard handle identifier.</param>
    /// <param name="value">The new handle.</param>
    /// <returns>Whether the handle was replaced.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetStdHandle(int handle, nint value);

    /// <summary>
    /// Reads a console handle's mode; it fails for pipes, files, and devices other than a console.
    /// </summary>
    /// <param name="handle">The handle to inspect.</param>
    /// <param name="mode">Receives the console mode.</param>
    /// <returns>Whether the handle belongs to a console.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetConsoleMode(nint handle, out uint mode);

    /// <summary>
    /// Opens a device such as the process console's input or active screen buffer.
    /// </summary>
    /// <param name="name">The device name.</param>
    /// <param name="access">The requested access.</param>
    /// <param name="share">The sharing mode.</param>
    /// <param name="security">The security attributes, or zero.</param>
    /// <param name="disposition">The creation disposition.</param>
    /// <param name="flags">The file flags.</param>
    /// <param name="template">A template handle, or zero.</param>
    /// <returns>The opened handle, which is invalid on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);
}
