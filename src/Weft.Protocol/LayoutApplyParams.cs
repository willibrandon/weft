namespace Weft.Protocol;

/// <summary>
/// Parameters for layout.apply.
/// </summary>
public sealed class LayoutApplyParams
{
    /// <summary>
    /// Gets the target tab.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Gets the serialized layout.
    /// </summary>
    public required string Layout { get; init; }
}
