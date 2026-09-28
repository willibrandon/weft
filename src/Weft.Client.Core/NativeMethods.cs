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
    /// Tells process creation that the startup information carries an attribute list.
    /// </summary>
    internal const int ExtendedStartupInfoPresent = 0x00080000;

    /// <summary>
    /// The attribute that sets whether a packaged app's new processes stay in its package.
    /// </summary>
    internal const nint DesktopAppPolicyAttribute = 0x00020012;

    /// <summary>
    /// The desktop app policy that creates processes outside the caller's package.
    /// </summary>
    internal const int DesktopAppBreakawayEnableProcessTree = 0x1;

    /// <summary>
    /// The error returned for a process that does not run from a package.
    /// </summary>
    internal const int AppModelErrorNoPackage = 15700;

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
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(string applicationName, nint commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, int creationFlags, nint environment,
        string? currentDirectory, in StartupInformation startupInfo, out ProcessInformation processInformation);

    /// <summary>
    /// Creates a process and its primary thread from startup information with an attribute list.
    /// </summary>
    /// <param name="applicationName">The executable path.</param>
    /// <param name="commandLine">A writable UTF-16 command line.</param>
    /// <param name="processAttributes">The process security attributes, or zero.</param>
    /// <param name="threadAttributes">The thread security attributes, or zero.</param>
    /// <param name="inheritHandles">Whether inheritable handles pass to the new process.</param>
    /// <param name="creationFlags">The creation flags, including <see cref="ExtendedStartupInfoPresent"/>.</param>
    /// <param name="environment">The environment block, or zero to inherit it.</param>
    /// <param name="currentDirectory">The working directory, or null to inherit it.</param>
    /// <param name="startupInfo">The extended startup information.</param>
    /// <param name="processInformation">Receives the process and thread handles.</param>
    /// <returns>Whether the process was created.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcessExtended(string applicationName, nint commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, int creationFlags, nint environment,
        string? currentDirectory, in StartupInformationEx startupInfo, out ProcessInformation processInformation);

    /// <summary>
    /// Initializes a process attribute list, or reports the size one needs when the list is zero.
    /// </summary>
    /// <param name="attributeList">The list memory, or zero to query its size.</param>
    /// <param name="attributeCount">The number of attributes.</param>
    /// <param name="flags">Reserved; zero.</param>
    /// <param name="size">The list size in bytes.</param>
    /// <returns>Whether the list was initialized.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags,
        ref nint size);

    /// <summary>
    /// Sets one attribute in a process attribute list.
    /// </summary>
    /// <param name="attributeList">The list.</param>
    /// <param name="flags">Reserved; zero.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value, which must remain valid until the process is created.</param>
    /// <param name="size">The value size in bytes.</param>
    /// <param name="previousValue">Reserved; zero.</param>
    /// <param name="returnSize">Reserved; zero.</param>
    /// <returns>Whether the attribute was set.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(nint attributeList, int flags, nint attribute, nint value,
        nint size, nint previousValue, nint returnSize);

    /// <summary>
    /// Releases a process attribute list's contents.
    /// </summary>
    /// <param name="attributeList">The list.</param>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void DeleteProcThreadAttributeList(nint attributeList);

    /// <summary>
    /// Gets the calling process's package name, failing with <see cref="AppModelErrorNoPackage"/> when unpackaged.
    /// </summary>
    /// <param name="length">The buffer length in characters; receives the length needed.</param>
    /// <param name="name">The buffer, or zero to query the length.</param>
    /// <returns>A Win32 error code, or zero.</returns>
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetCurrentPackageFullName(ref int length, nint name);

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
