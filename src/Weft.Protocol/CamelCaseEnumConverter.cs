using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weft.Protocol;

/// <summary>
/// Serializes enum values as camel-case strings on the wire.
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
