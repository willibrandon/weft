using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weft.Client;

/// <summary>
/// Encodes cells as compact arrays, omitting default style fields from ordinary text.
/// </summary>
internal sealed class DesktopCellJsonConverter : JsonConverter<DesktopCell>
{
    /// <inheritdoc />
    public override DesktopCell Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray || !reader.Read())
        {
            throw new JsonException("Expected a cell array.");
        }
        string text = reader.GetString() ?? throw new JsonException("Expected cell text.");
        if (!reader.Read())
        {
            throw new JsonException("Incomplete cell.");
        }
        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return new DesktopCell(text, null, null, 0);
        }
        int? foreground = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
        if (!reader.Read())
        {
            throw new JsonException("Missing cell background.");
        }
        int? background = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
        if (!reader.Read())
        {
            throw new JsonException("Missing cell attributes.");
        }
        int attributes = reader.GetInt32();
        if (!reader.Read())
        {
            throw new JsonException("Incomplete cell attributes.");
        }
        string? link = null;
        if (reader.TokenType != JsonTokenType.EndArray)
        {
            link = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Incomplete cell link.");
            }
        }
        return reader.TokenType == JsonTokenType.EndArray
            ? new DesktopCell(text, foreground, background, attributes, link)
            : throw new JsonException("Unexpected cell field.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DesktopCell value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(value.Text);
        if (value.Foreground is not null || value.Background is not null || value.Attributes != 0 || value.Link is not null)
        {
            if (value.Foreground is int foreground)
            {
                writer.WriteNumberValue(foreground);
            }
            else
            {
                writer.WriteNullValue();
            }
            if (value.Background is int background)
            {
                writer.WriteNumberValue(background);
            }
            else
            {
                writer.WriteNullValue();
            }
            writer.WriteNumberValue(value.Attributes);
            if (value.Link is not null)
            {
                writer.WriteStringValue(value.Link);
            }
        }
        writer.WriteEndArray();
    }
}
