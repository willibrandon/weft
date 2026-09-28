using Weft.Client;

namespace Weft.Tests;

/// <summary>
/// Verifies server startup cannot recursively launch its desktop caller.
/// </summary>
[TestClass]
public sealed class ServerLauncherTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Rejects the current executable as an explicit server, including case-only differences.
    /// </summary>
    /// <param name="changeCase">Whether to vary the path casing.</param>
    /// <returns>The test task.</returns>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ExplicitServerCannotBeTheCallingExecutable(bool changeCase)
    {
        string root = Path.Join(Path.GetTempPath(), "weft-launch-" + Guid.NewGuid().ToString("N")[..10]);
        _ = Directory.CreateDirectory(root);
        try
        {
            string executable = Environment.ProcessPath!;
            if (changeCase)
            {
                executable = executable.ToUpperInvariant();
            }

            InvalidOperationException failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => ConnectAndDisposeAsync(root, executable, TestContext.CancellationToken)).ConfigureAwait(false);
            Assert.Contains("cannot start itself", failure.Message);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ConnectAndDisposeAsync(string root, string executable, CancellationToken token)
    {
        ControlClient client = await ServerLauncher.ConnectOrStartAsync(root, executable, token).ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }
}
