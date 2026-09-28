using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;

namespace Weft.Server;

/// <summary>
/// Gives pseudo-console children the server's console rather than redirected standard handles.
/// </summary>
/// <remarks>
/// Windows passes a new process its creator's standard handles unless they are console handles.
/// A launcher or test host that redirected the server's output would otherwise receive a shell's
/// output, and a closed input pipe would end the shell at once. Console handles are replaced by
/// the pseudo console, so each shell reads and writes its own terminal.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsConsole
{
    private static readonly Lock s_gate = new();
    private static SafeFileHandle? s_input;
    private static SafeFileHandle? s_output;

    /// <summary>
    /// Points each redirected standard handle at this process's console, leaving console handles alone.
    /// </summary>
    internal static void UseConsoleForChildren()
    {
        lock (s_gate)
        {
            if (!IsConsole(NativeMethods.StandardInput))
            {
                s_input ??= Open("CONIN$");
                Replace(NativeMethods.StandardInput, s_input);
            }

            if (!IsConsole(NativeMethods.StandardOutput) || !IsConsole(NativeMethods.StandardError))
            {
                s_output ??= Open("CONOUT$");
                Replace(NativeMethods.StandardOutput, s_output);
                Replace(NativeMethods.StandardError, s_output);
            }
        }
    }

    private static bool IsConsole(int standardHandle)
    {
        return NativeMethods.GetConsoleMode(NativeMethods.GetStdHandle(standardHandle), out _);
    }

    private static SafeFileHandle? Open(string device)
    {
        SafeFileHandle handle = NativeMethods.CreateFile(device, NativeMethods.ReadWrite, NativeMethods.ShareReadWrite,
            0, NativeMethods.OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            ServerLog.Warn("This process has no console; block processes inherit its standard handles.");
            return null;
        }

        return handle;
    }

    private static void Replace(int standardHandle, SafeFileHandle? console)
    {
        if (console is not null && !NativeMethods.SetStdHandle(standardHandle, console.DangerousGetHandle()))
        {
            ServerLog.Warn("A standard handle could not be pointed at the console.");
        }
    }
}
