using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weft.Core;

/// <summary>
/// Source-generated serialization for the configuration file, tolerating comments and trailing commas.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(WeftConfig))]
public sealed partial class WeftConfigJsonContext : JsonSerializerContext;
