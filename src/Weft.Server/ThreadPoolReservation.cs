namespace Weft.Server;

/// <summary>
/// Keeps thread pool capacity ahead of the pool threads each running block pins.
/// </summary>
/// <remarks>
/// The Unix pseudo-terminal reader waits in a blocking select loop on a pool thread, and the exit
/// wait blocks in short slices, so every running block holds about two workers for its lifetime.
/// A pool that starts at the core count then starves once a handful of blocks are running: the
/// runtime injects replacement threads only about once a second, and until it catches up every
/// continuation in the process stalls, including socket reads, renders, and timers. Raising the
/// minimum as blocks start lets the pool grow immediately instead.
/// </remarks>
internal static class ThreadPoolReservation
{
    private const int WorkersPerBlock = 2;
    private const int Headroom = 8;
    private static readonly Lock s_gate = new();
    private static int s_blocks;

    /// <summary>
    /// Records a block that has started and grows the pool minimum to cover it.
    /// </summary>
    internal static void Acquire()
    {
        lock (s_gate)
        {
            s_blocks++;
            int wanted = Environment.ProcessorCount + Headroom + (s_blocks * WorkersPerBlock);
            ThreadPool.GetMinThreads(out int workers, out int io);
            if (workers < wanted && !ThreadPool.SetMinThreads(wanted, io))
            {
                ServerLog.Warn("Could not raise the thread pool minimum to " + wanted + " workers.");
            }
        }
    }

    /// <summary>
    /// Records a block that has exited. The minimum is left where it is; shrinking it buys nothing.
    /// </summary>
    internal static void Release()
    {
        lock (s_gate)
        {
            s_blocks--;
        }
    }
}
