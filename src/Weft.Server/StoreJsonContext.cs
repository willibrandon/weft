using System.Text.Json.Serialization;

namespace Weft.Server;

/// <summary>
/// Source-generated serialization for persisted session files.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredSession))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;
