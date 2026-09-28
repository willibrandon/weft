using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Hosts the Reactor app on one UI thread for the whole run, the way the desktop process does.
/// </summary>
/// <remarks>
/// Test windows use private preferences and servers and never take the foreground, so a run leaves the user's
/// sessions, settings, keyboard, and focus alone. Each test body runs on the UI thread, where awaits resume.
/// </remarks>
[TestClass]
public static class DesktopApp
{
    private static DispatcherQueue? s_dispatcher;
    private static Thread? s_thread;

    /// <summary>
    /// Gets the directory that holds this run's preferences and private servers.
    /// </summary>
    internal static string Root { get; } = Path.Join(Path.GetTempPath(), "wd-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// Gets the server that windows start on demand.
    /// </summary>
    internal static string Server { get; } =
        Environment.GetEnvironmentVariable("WEFT_DESKTOP_SERVER") is { Length: > 0 } configured
            ? configured
            : typeof(DesktopApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "WeftServer").Value!;

    /// <summary>
    /// Gets the deterministic shell that every test terminal runs.
    /// </summary>
    internal static string Shell { get; } = Path.Join(AppContext.BaseDirectory, "programs", "TestShell.exe");

    /// <summary>
    /// Starts WinUI and Reactor before any test opens a window.
    /// </summary>
    /// <param name="context">The test context.</param>
    /// <returns>A task that completes when the UI thread is ready.</returns>
    [AssemblyInitialize]
    public static async Task StartAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("WEFT_DESKTOP_PREFERENCES", Path.Join(Root, "desktop.json"));
        Environment.SetEnvironmentVariable("WEFT_DESKTOP_SERVER", Server);
        // The same self-contained runtime initialization as the app's Program.cs.
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = NativeLibrary.Load(Path.Join(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"));

        var started = new TaskCompletionSource<DispatcherQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_thread = new Thread(() =>
        {
            // Tests open and close many windows; only the cleanup below ends the app.
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorApp.Run(_ => started.SetResult(DispatcherQueue.GetForCurrentThread()));
        })
        {
            IsBackground = true,
            Name = "Weft UI"
        };
        s_thread.SetApartmentState(ApartmentState.STA);
        s_thread.Start();
        s_dispatcher = await started.Task.WaitAsync(TimeSpan.FromMinutes(1), context.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Closes every window and ends the UI thread.
    /// </summary>
    /// <param name="context">The test context.</param>
    /// <returns>A task that completes when the app has exited.</returns>
    [AssemblyCleanup]
    public static async Task StopAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (s_dispatcher is not null)
        {
            await RunAsync(() =>
            {
                ReactorApp.Exit();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            _ = s_thread?.Join(TimeSpan.FromSeconds(30));
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException exception)
        {
            context.WriteLine("Test files remain in " + Root + ": " + exception.Message);
        }
    }

    /// <summary>
    /// Runs a test against a new window on its own server, once the shell's prompt appears.
    /// </summary>
    /// <param name="context">The test context.</param>
    /// <param name="test">The test body.</param>
    /// <returns>A task that completes with the body's outcome after the window and server are gone.</returns>
    internal static Task RunWindowAsync(TestContext context, Func<TestWindow, PrivateServer, Task> test)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(test);
        return RunAsync(async () =>
        {
            PrivateServer server = await PrivateServer.CreateAsync().ConfigureAwait(true);
            await using (server.ConfigureAwait(true))
            {
                TestWindow window = await TestWindow.OpenReadyAsync(context.CancellationToken).ConfigureAwait(true);
                await using (window.ConfigureAwait(true))
                {
                    await test(window, server).ConfigureAwait(true);
                }
            }
        });
    }

    /// <summary>
    /// Runs a test against a new window on its own server, once the shell's prompt appears.
    /// </summary>
    /// <param name="context">The test context.</param>
    /// <param name="test">The test body.</param>
    /// <returns>A task that completes with the body's outcome after the window and server are gone.</returns>
    internal static Task RunWindowAsync(TestContext context, Func<TestWindow, Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return RunWindowAsync(context, (window, _) => test(window));
    }

    /// <summary>
    /// Runs a test body on the UI thread, where windows are created and their events arrive.
    /// </summary>
    /// <param name="test">The test body.</param>
    /// <returns>A task that completes with the body's outcome.</returns>
    internal static Task RunAsync(Func<Task> test)
    {
        DispatcherQueue dispatcher = s_dispatcher ?? throw new InvalidOperationException("The app has not started.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = dispatcher.TryEnqueue(() => _ = test().ContinueWith(
            completion.SetFromTask,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default));
        return queued ? completion.Task : throw new InvalidOperationException("The UI thread has stopped.");
    }
}
