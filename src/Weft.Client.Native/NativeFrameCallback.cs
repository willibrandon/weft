using System.Runtime.InteropServices;

namespace Weft.Client.Native;

/// <summary>
/// Schedules a native frame read without transferring ownership of the callback context.
/// </summary>
/// <param name="context">The caller-owned context retained until notifications are cleared.</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void NativeFrameCallback(nint context);
