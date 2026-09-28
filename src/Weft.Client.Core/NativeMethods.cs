using System.Runtime.InteropServices;

namespace Weft.Client;

/// <summary>
/// Declares the Windows process functions used to start a server that outlives its caller.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// Creates a console for the process without showing a window.
    /// </summary>
    internal const int CreateNoWindow = 0x08000000;

    /// <summary>
    /// Places the process in its own group so a caller's Ctrl+C does not reach it.
    /// </summary>
    internal const int CreateNewProcessGroup = 0x00000200;

    /// <summary>
    /// Leaves the caller's job object when that job permits it.
    /// </summary>
    internal const int CreateBreakawayFromJob = 0x01000000;

    /// <summary>
    /// The error returned when a job does not permit breakaway.
    /// </summary>
    internal const int ErrorAccessDenied = 5;

    /// <summary>
    /// Creates a process and its primary thread.
    /// </summary>
    /// <param name="applicationName">The executable path.</param>
    /// <param name="commandLine">A writable UTF-16 command line.</param>
    /// <param name="processAttributes">The process security attributes, or zero.</param>
    /// <param name="threadAttributes">The thread security attributes, or zero.</param>
    /// <param name="inheritHandles">Whether inheritable handles pass to the new process.</param>
    /// <param name="creationFlags">The creation flags.</param>
    /// <param name="environment">The environment block, or zero to inherit it.</param>
    /// <param name="currentDirectory">The working directory, or null to inherit it.</param>
    /// <param name="startupInfo">The startup information.</param>
    /// <param name="processInformation">Receives the process and thread handles.</param>
    /// <returns>Whether the process was created.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(string applicationName, nint commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, int creationFlags, nint environment, string? currentDirectory,
        in StartupInformation startupInfo, out ProcessInformation processInformation);

    /// <summary>
    /// Closes a kernel handle.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns>Whether the handle was closed.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
