using System.CommandLine;

namespace Weft.App;

/// <summary>
/// Options and arguments shared by many commands.
/// </summary>
internal static class CommonOptions
{
    /// <summary>
    /// Gets the option that switches output to JSON.
    /// </summary>
    internal static Option<bool> Json { get; } = new("--json") { Description = "Print results as JSON.", Recursive = true };

    /// <summary>
    /// Gets the option that overrides the runtime directory.
    /// </summary>
    internal static Option<string?> SocketDirectory { get; } = new("--socket-dir") { Description = "Runtime directory holding the server sockets.", Recursive = true };

    /// <summary>
    /// Creates an optional target argument.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <returns>The argument.</returns>
    internal static Argument<string?> OptionalTarget(string description) =>
        new("target") { Description = description, Arity = ArgumentArity.ZeroOrOne };

    /// <summary>
    /// Creates a trailing command argument that collects everything after <c>--</c>.
    /// </summary>
    /// <returns>The argument.</returns>
    internal static Argument<string[]> Command() =>
        new("command") { Description = "Command and arguments to run; defaults to the shell.", Arity = ArgumentArity.ZeroOrMore };

    /// <summary>
    /// Resolves the target to use when none was given: the block the caller runs in, if any.
    /// </summary>
    /// <param name="target">The explicit target.</param>
    /// <returns>The effective target.</returns>
    internal static string? EffectiveTarget(string? target) =>
        !string.IsNullOrEmpty(target) ? target : Environment.GetEnvironmentVariable("WEFT_BLOCK");
}
