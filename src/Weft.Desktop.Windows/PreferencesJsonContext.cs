using System.Text.Json.Serialization;

namespace Weft.Desktop.Windows;

/// <summary>
/// Serializes desktop preferences without reflection so the app stays Native AOT compatible.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DesktopPreferences))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;
