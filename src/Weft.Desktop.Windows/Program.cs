using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using System.Globalization;
using System.Runtime.InteropServices;
using Weft.Desktop.Windows;

// A self-contained app activates WinRT types through the runtime beside it, which starts
// redirecting activation when it loads. Loading it by full path involves no search order.
// The process id stamp keeps a child, such as the server, from adopting this directory.
Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
Environment.SetEnvironmentVariable(
    "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID",
    Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
_ = NativeLibrary.Load(Path.Join(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"));

// Packaging renders the product's vector icon without constructing an application or a window.
if (args is ["--render-icon", string directory])
{
    await AppIcon.RenderAsync(Path.GetFullPath(directory)).ConfigureAwait(false);
    return 0;
}

// The app must never act as the server bootstrap executable. Reject arguments before any window exists.
if (args.Length != 0)
{
    await Console.Error.WriteLineAsync("Weft is the desktop app, not the server executable.").ConfigureAwait(false);
    return 64;
}

// The .NET 11 RC1 ILCompiler's scanner does not predict that code generation constructs this array
// type and stops the Native AOT publish. Constructing it here keeps both analyses in agreement.
GC.KeepAlive(new KeyframeEntry[1]);
ReactorApp.Run<TerminalWindow>(DesktopWindows.TerminalSpec());
return 0;
