using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Weft.Client;

/// <summary>
/// Starts the Windows server independently of the caller's handles, console, and job.
/// </summary>
/// <remarks>
/// A redirected <see cref="System.Diagnostics.Process"/> start passes every inheritable handle to the child,
/// so a detached server would keep a caller's output pipe open and its reader would never see the end.
/// The server instead receives no handles and a console without a window. That console is what gives each
/// shell its own pseudo console, and closing the caller's terminal cannot end the server.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsServerProcess
{
    /// <summary>
    /// Starts the executable and returns without waiting for it.
    /// </summary>
    /// <param name="executable">The server executable.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The process identifier.</returns>
    /// <exception cref="Win32Exception">The process could not be created.</exception>
    internal static int Start(string executable, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder();
        Append(line, executable);
        foreach (string argument in arguments)
        {
            _ = line.Append(' ');
            Append(line, argument);
        }

        nint commandLine = Marshal.StringToHGlobalUni(line.ToString());
        try
        {
            var startup = new StartupInformation(Marshal.SizeOf<StartupInformation>());
            const int Flags = NativeMethods.CreateNoWindow | NativeMethods.CreateNewProcessGroup;
            if (!NativeMethods.CreateProcess(executable, commandLine, 0, 0, false, Flags | NativeMethods.CreateBreakawayFromJob, 0, null, startup, out ProcessInformation created))
            {
                int error = Marshal.GetLastPInvokeError();
                // A job that forbids breakaway still lets the server outlive its caller unless it kills on close.
                if (error != NativeMethods.ErrorAccessDenied
                    || !NativeMethods.CreateProcess(executable, commandLine, 0, 0, false, Flags, 0, null, startup, out created))
                {
                    throw new Win32Exception(error == NativeMethods.ErrorAccessDenied ? Marshal.GetLastPInvokeError() : error);
                }
            }

            _ = NativeMethods.CloseHandle(created.Thread);
            _ = NativeMethods.CloseHandle(created.Process);
            return created.ProcessId;
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
        }
    }

    /// <summary>
    /// Quotes one argument using the rules that CommandLineToArgvW and the C runtime reverse.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="argument">The argument.</param>
    internal static void Append(StringBuilder line, string argument)
    {
        if (argument.Length != 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            _ = line.Append(argument);
            return;
        }

        _ = line.Append('"');
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes are literal unless they precede a quote, which they must then escape.
            _ = line.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes).Append(character);
            backslashes = 0;
        }

        _ = line.Append('\\', backslashes * 2).Append('"');
    }
}
