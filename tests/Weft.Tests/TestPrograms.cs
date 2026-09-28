namespace Weft.Tests;

/// <summary>
/// Locates the file-based helper programs built beside the tests.
/// </summary>
internal static class TestPrograms
{
    /// <summary>
    /// Gets the test shell, which behaves the same in every operating system's pseudo-terminal.
    /// </summary>
    internal static string Shell { get; } = Path.Join(AppContext.BaseDirectory, "programs",
        OperatingSystem.IsWindows() ? "TestShell.exe" : "TestShell");
}
