using System.Runtime.InteropServices;

namespace Weft.Client;

/// <summary>
/// Mirrors STARTUPINFOW with no standard handles, so a new console supplies them.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct StartupInformation
{
    private readonly int _size;
    private readonly nint _reserved;
    private readonly nint _desktop;
    private readonly nint _title;
    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly int _columns;
    private readonly int _rows;
    private readonly int _fillAttribute;
    private readonly int _flags;
    private readonly short _showWindow;
    private readonly short _reservedSize;
    private readonly nint _reservedData;
    private readonly nint _standardInput;
    private readonly nint _standardOutput;
    private readonly nint _standardError;

    /// <summary>
    /// Initializes an empty structure with its required size.
    /// </summary>
    /// <param name="size">The structure size in bytes.</param>
    internal StartupInformation(int size)
    {
        _size = size;
    }
}
