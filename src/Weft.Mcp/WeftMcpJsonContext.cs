using System.Text.Json.Serialization;

namespace Weft.Mcp;

/// <summary>
/// Source-generated JSON metadata for the parameter and result types of weft's MCP tools.
/// </summary>
/// <remarks>
/// The SDK binds tool arguments through generated contexts of its own. Registering the exact
/// argument types weft uses keeps that binding independent of reflection under Native AOT.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(bool))]
internal sealed partial class WeftMcpJsonContext : JsonSerializerContext;
