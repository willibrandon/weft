namespace Weft.Client;

/// <summary>
/// Holds one resolved image frame independently of its placement geometry.
/// </summary>
/// <param name="Key">The pixel digest used by native caches.</param>
/// <param name="Data">The current raster bytes.</param>
/// <param name="Format">The raster encoding.</param>
internal sealed record DesktopRaster(string Key, byte[] Data, int Format);
