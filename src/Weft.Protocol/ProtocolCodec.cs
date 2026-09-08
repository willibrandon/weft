using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Weft.Protocol;

/// <summary>
/// Encodes and decodes newline-delimited control protocol messages.
/// </summary>
public static class ProtocolCodec
{
    /// <summary>
    /// The maximum accepted line length in bytes, which bounds memory per connection.
    /// </summary>
    public const int MaximumLineBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Serializes a message to one UTF-8 line ending in a newline.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The encoded line.</returns>
    public static byte[] EncodeLine(ProtocolMessage message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.ProtocolMessage);
        byte[] line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    /// <summary>
    /// Parses one line into a message.
    /// </summary>
    /// <param name="line">The UTF-8 line without its newline.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ProtocolException">The line was not a message.</exception>
    public static ProtocolMessage DecodeLine(ReadOnlySpan<byte> line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.ProtocolMessage)
                ?? throw new ProtocolException(ErrorCodes.InvalidRequest, "The line was not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ProtocolException(ErrorCodes.InvalidRequest, "The line was not valid JSON: " + exception.Message);
        }
    }

    /// <summary>
    /// Converts a typed value to a JSON element for embedding in a message.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The source-generated type information.</param>
    /// <returns>The element.</returns>
    public static JsonElement ToElement<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToElement(value, typeInfo);

    /// <summary>
    /// Converts a message's parameters, result, or data into a typed value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="element">The element; null yields a default instance when the type allows it.</param>
    /// <param name="typeInfo">The source-generated type information.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ProtocolException">The element did not match the type.</exception>
    public static T FromElement<T>(JsonElement? element, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return JsonSerializer.Deserialize("{}", typeInfo)
                    ?? throw new ProtocolException(ErrorCodes.InvalidParams, "Parameters are required.");
            }

            return element.Value.Deserialize(typeInfo)
                ?? throw new ProtocolException(ErrorCodes.InvalidParams, "Parameters are required.");
        }
        catch (JsonException exception)
        {
            throw new ProtocolException(ErrorCodes.InvalidParams, "Invalid parameters: " + exception.Message);
        }
    }

    /// <summary>
    /// Builds a request message.
    /// </summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="id">The request id.</param>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="typeInfo">The source-generated type information.</param>
    /// <returns>The message.</returns>
    public static ProtocolMessage Request<T>(long id, string method, T parameters, JsonTypeInfo<T> typeInfo) =>
        new() { Id = id, Method = method, Params = ToElement(parameters, typeInfo) };

    /// <summary>
    /// Builds a success response.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="id">The request id.</param>
    /// <param name="result">The result.</param>
    /// <param name="typeInfo">The source-generated type information.</param>
    /// <returns>The message.</returns>
    public static ProtocolMessage Success<T>(long id, T result, JsonTypeInfo<T> typeInfo) =>
        new() { Id = id, Result = ToElement(result, typeInfo) };

    /// <summary>
    /// Builds an error response.
    /// </summary>
    /// <param name="id">The request id, or null when the request had none.</param>
    /// <param name="code">The error code.</param>
    /// <param name="message">The message.</param>
    /// <returns>The message.</returns>
    public static ProtocolMessage Failure(long? id, string code, string message) =>
        new() { Id = id ?? 0, Error = new ProtocolError { Code = code, Message = message } };

    /// <summary>
    /// Builds an event message.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="name">The event name.</param>
    /// <param name="seq">The sequence number.</param>
    /// <param name="data">The payload.</param>
    /// <param name="typeInfo">The source-generated type information.</param>
    /// <returns>The message.</returns>
    public static ProtocolMessage Event<T>(string name, long seq, T data, JsonTypeInfo<T> typeInfo) =>
        new() { Event = name, Seq = seq, Data = ToElement(data, typeInfo) };

    /// <summary>
    /// Renders a message as a string line for logs and tests.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The JSON text without a trailing newline.</returns>
    public static string ToJson(ProtocolMessage message) =>
        Encoding.UTF8.GetString(EncodeLine(message).AsSpan()[..^1]);
}
