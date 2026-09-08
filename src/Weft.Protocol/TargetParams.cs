namespace Weft.Protocol;

/// <summary>
/// Parameters for methods that only need a target.
/// </summary>
public sealed class TargetParams
{
    /// <summary>
    /// Gets the target in <c>session:tab.block</c> form; null means the caller's current target.
    /// </summary>
    public string? Target { get; init; }
}
