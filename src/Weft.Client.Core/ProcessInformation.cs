using System.Runtime.InteropServices;

namespace Weft.Client;

/// <summary>
/// Mirrors PROCESS_INFORMATION, whose handles the caller must close.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ProcessInformation
{
    /// <summary>
    /// Gets the process handle.
    /// </summary>
    internal nint Process { get; }

    /// <summary>
    /// Gets the primary thread handle.
    /// </summary>
    internal nint Thread { get; }

    /// <summary>
    /// Gets the process identifier.
    /// </summary>
    internal int ProcessId { get; }

    /// <summary>
    /// Gets the primary thread identifier.
    /// </summary>
    internal int ThreadId { get; }
}
