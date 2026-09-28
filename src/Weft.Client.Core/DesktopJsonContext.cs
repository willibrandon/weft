using System.Text.Json.Serialization;

namespace Weft.Client;

/// <summary>
/// Generates the desktop bridge JSON metadata for Native AOT.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopCommand))]
[JsonSerializable(typeof(DesktopFrame))]
public sealed partial class DesktopJsonContext : JsonSerializerContext
{
}
