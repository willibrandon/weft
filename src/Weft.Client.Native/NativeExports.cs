using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Weft.Core;

namespace Weft.Client.Native;

/// <summary>
/// Exposes a versioned, nonblocking C ABI with explicit frame and UTF-8 command buffer ownership.
/// </summary>
public static class NativeExports
{
    private static readonly ConcurrentDictionary<long, DesktopClient> s_clients = new();
    private static long s_nextHandle;

    /// <summary>
    /// Gets the C ABI version understood by this library.
    /// </summary>
    /// <returns>The ABI version.</returns>
    [UnmanagedCallersOnly(EntryPoint = "weft_abi_version")]
    public static int Version()
    {
        return 4;
    }

    /// <summary>
    /// Starts a desktop connection; failures during connection arrive in a frame.
    /// </summary>
    /// <param name="executable">The UTF-8 bundled CLI path.</param>
    /// <param name="length">The path byte length.</param>
    /// <param name="width">The viewport columns.</param>
    /// <param name="height">The viewport rows.</param>
    /// <returns>An opaque handle, or zero for invalid arguments.</returns>
    [UnmanagedCallersOnly(EntryPoint = "weft_open")]
    public static long Open(nint executable, int length, int width, int height)
    {
        try
        {
            string path = ReadUtf8(executable, length);
            WeftConfig config = WeftConfigLoader.LoadDefault(out string? error);
            if (error is not null)
            {
                throw new ArgumentException(error);
            }

            long handle = Interlocked.Increment(ref s_nextHandle);
            s_clients[handle] = new DesktopClient(WeftPaths.ResolveRuntimeDirectory(), path, width, height, config.Shell);
            return handle;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ClientLog.Debug(exception.Message);
            return 0;
        }
    }

    /// <summary>
    /// Copies a UTF-8 JSON command into the client's bounded queue.
    /// </summary>
    /// <param name="handle">The opaque client handle.</param>
    /// <param name="data">The command bytes.</param>
    /// <param name="length">The byte length.</param>
    /// <returns>Zero when accepted, minus one when invalid, or minus two when closed or busy.</returns>
    [UnmanagedCallersOnly(EntryPoint = "weft_send")]
    public static int Send(long handle, nint data, int length)
    {
        try
        {
            if (!s_clients.TryGetValue(handle, out DesktopClient? client))
            {
                return -1;
            }

            DesktopCommand? command = JsonSerializer.Deserialize(ReadUtf8(data, length), DesktopJsonContext.Default.DesktopCommand);
            return command is null ? -1 : client.TrySend(command) ? 0 : -2;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            ClientLog.Debug(exception.Message);
            return -1;
        }
    }

    /// <summary>
    /// Registers a worker-thread frame notification; zero clears it and drains in-flight notification calls.
    /// </summary>
    /// <param name="handle">The opaque client handle.</param>
    /// <param name="callback">The C function pointer, or zero.</param>
    /// <param name="context">The caller-owned callback context.</param>
    /// <returns>Zero on success, or minus one for an invalid handle.</returns>
    [UnmanagedCallersOnly(EntryPoint = "weft_notify")]
    public static int Notify(long handle, nint callback, nint context)
    {
        if (!s_clients.TryGetValue(handle, out DesktopClient? client))
        {
            return -1;
        }
        if (callback == 0)
        {
            client.SetFrameReady(null);
        }
        else
        {
            NativeFrameCallback notify = Marshal.GetDelegateForFunctionPointer<NativeFrameCallback>(callback);
            client.SetFrameReady(() => notify(context));
        }
        return 0;
    }

    /// <summary>
    /// Takes the latest metadata and raw texture frame into a caller-owned buffer.
    /// </summary>
    /// <param name="handle">The opaque client handle.</param>
    /// <param name="length">Receives the byte length, zero when unchanged, or minus one for failure.</param>
    /// <returns>A buffer to release with weft_free, or zero.</returns>
    [UnmanagedCallersOnly(EntryPoint = "weft_poll")]
    public static nint Poll(long handle, nint length)
    {
        if (length == 0)
        {
            return 0;
        }

        Marshal.WriteInt32(length, 0);
        try
        {
            if (!s_clients.TryGetValue(handle, out DesktopClient? client))
            {
                Marshal.WriteInt32(length, -1);
                return 0;
            }

            DesktopFrame? frame = client.TakeFrame();
            if (frame is null)
            {
                return 0;
            }

            var wire = new DesktopWireFrame(frame);
            nint buffer = CopyFrame(wire);
            Marshal.WriteInt32(length, wire.Length);
            return buffer;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or NotSupportedException)
        {
            ClientLog.Debug(exception.Message);
            Marshal.WriteInt32(length, -1);
            return 0;
        }
    }

    private static nint CopyFrame(DesktopWireFrame wire)
    {
        nint buffer = Marshal.AllocHGlobal(wire.Length);
        try
        {
            wire.CopyTo(new Span<byte>((void*)buffer, wire.Length));
            return buffer;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }

    /// <summary>
    /// Releases exactly one buffer returned by weft_poll.
    /// </summary>
    /// <param name="buffer">The owned buffer, or zero.</param>
    [UnmanagedCallersOnly(EntryPoint = "weft_free")]
    public static void Free(nint buffer)
    {
        Marshal.FreeHGlobal(buffer);
    }

    /// <summary>
    /// Invalidates the handle and releases connections asynchronously without stopping the server.
    /// </summary>
    /// <param name="handle">The opaque client handle.</param>
    [UnmanagedCallersOnly(EntryPoint = "weft_close")]
    public static void Close(long handle)
    {
        if (s_clients.TryRemove(handle, out DesktopClient? client))
        {
            client.SetFrameReady(null);
            _ = ObserveReleaseAsync(client.DisposeAsync());
        }
    }

    private static async Task ObserveReleaseAsync(ValueTask release)
    {
        try
        {
            await release.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            ClientLog.Debug(exception.ToString());
        }
    }

    private static string ReadUtf8(nint data, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 1_048_576);
        if (data == 0)
        {
            throw new ArgumentException("A UTF-8 buffer is required.", nameof(data));
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(data, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }
}
