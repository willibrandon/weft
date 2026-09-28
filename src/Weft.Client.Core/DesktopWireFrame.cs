using System.Buffers.Binary;
using System.Text.Json;

namespace Weft.Client;

/// <summary>
/// Encodes metadata and shared raw textures without expanding pixel buffers into JSON strings.
/// </summary>
public sealed class DesktopWireFrame
{
    private readonly DesktopFrame _frame;
    private readonly byte[] _metadata;

    /// <summary>
    /// Prepares frame metadata while retaining existing texture storage for a direct copy to the bridge.
    /// </summary>
    /// <param name="frame">The immutable display frame.</param>
    public DesktopWireFrame(DesktopFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _metadata = JsonSerializer.SerializeToUtf8Bytes(frame, DesktopJsonContext.Default.DesktopFrame);
        Length = checked(4 + _metadata.Length + frame.Blocks.Sum(block => block.Textures.Sum(texture => texture.Data.Length)));
    }

    /// <summary>
    /// Gets the byte count required for metadata and raw textures.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Copies the prepared frame into caller-owned storage without allocating another pixel buffer.
    /// </summary>
    /// <param name="destination">Storage large enough for the complete frame.</param>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("The frame destination is too small.", nameof(destination));
        }
        BinaryPrimitives.WriteInt32LittleEndian(destination, _metadata.Length);
        _metadata.CopyTo(destination[4..]);
        int offset = 4 + _metadata.Length;
        foreach (DesktopTexture texture in _frame.Blocks.SelectMany(block => block.Textures))
        {
            texture.Data.Span.CopyTo(destination[offset..]);
            offset += texture.Data.Length;
        }
    }

    /// <summary>
    /// Writes a little-endian JSON length, UTF-8 metadata, then each block's textures in order.
    /// </summary>
    /// <param name="frame">The immutable display frame.</param>
    /// <returns>The complete owned wire buffer.</returns>
    public static byte[] Serialize(DesktopFrame frame)
    {
        var wire = new DesktopWireFrame(frame);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(wire.Length);
        wire.CopyTo(bytes);
        return bytes;
    }
}
