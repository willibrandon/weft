using System.Text.Json.Serialization;

namespace Weft.Client;

/// <summary>
/// Transfers one texture once per frame, independently of how many placements reference it.
/// </summary>
/// <param name="Key">The content digest.</param>
/// <param name="Data">The encoded or raw pixel bytes.</param>
/// <param name="Format">The PNG, RGB, or RGBA format.</param>
/// <param name="PixelWidth">The decoded width.</param>
/// <param name="PixelHeight">The decoded height.</param>
public sealed record DesktopTexture(string Key, [property: JsonIgnore] ReadOnlyMemory<byte> Data, int Format, int PixelWidth, int PixelHeight)
{
    /// <summary>
    /// Gets the length of the raw payload appended after the frame's JSON metadata.
    /// </summary>
    public int ByteLength => Data.Length;
}
