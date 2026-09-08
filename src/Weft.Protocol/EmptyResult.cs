namespace Weft.Protocol;

/// <summary>
/// The result of methods that return nothing.
/// </summary>
public sealed class EmptyResult
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static EmptyResult Instance { get; } = new();
}
