using System.Text.Json;

namespace Weft.Protocol;

/// <summary>
/// One line of the control protocol: a request, a response, or an event, distinguished by which properties are present.
/// </summary>
public sealed class ProtocolMessage
{
    /// <summary>
    /// Gets the request id; present on requests and responses.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// Gets the method name; present on requests.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Gets the request parameters; present on requests.
    /// </summary>
    public JsonElement? Params { get; init; }

    /// <summary>
    /// Gets the result; present on successful responses.
    /// </summary>
    public JsonElement? Result { get; init; }

    /// <summary>
    /// Gets the error; present on failed responses.
    /// </summary>
    public ProtocolError? Error { get; init; }

    /// <summary>
    /// Gets the event name; present on events.
    /// </summary>
    public string? Event { get; init; }

    /// <summary>
    /// Gets the event sequence number; present on events.
    /// </summary>
    public long? Seq { get; init; }

    /// <summary>
    /// Gets the event payload; present on events.
    /// </summary>
    public JsonElement? Data { get; init; }

    /// <summary>
    /// Gets whether this message is a request.
    /// </summary>
    public bool IsRequest => Method is not null;

    /// <summary>
    /// Gets whether this message is a response.
    /// </summary>
    public bool IsResponse => Method is null && Event is null && Id is not null;

    /// <summary>
    /// Gets whether this message is an event.
    /// </summary>
    public bool IsEvent => Event is not null;
}
