namespace Weft.Client;

/// <summary>
/// Retains materialized pixel dimensions without keeping an additional raster buffer alive.
/// </summary>
/// <param name="Width">The materialized pixel width.</param>
/// <param name="Height">The materialized pixel height.</param>
internal sealed record DesktopRasterSize(int Width, int Height);
