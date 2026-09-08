using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Weft.Mcp;

namespace Weft.Tests;

/// <summary>
/// Drives the MCP server through the SDK client over an in-memory stream transport against a real weft server.
/// </summary>
[TestClass]
public sealed class McpServerTests
{
    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies tools are listed, a command runs to completion with its exit code, keys reach a block, and the block resource reads its screen.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task ToolsRunCommandsAndReadScreens()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(cancellationToken).ConfigureAwait(false);
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            using var serverStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task server = WeftMcpServer.RunStreamsAsync(fixture.SocketPath, "test", clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), serverStop.Token);
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                CollectionAssert.IsSubsetOf(new[] { "list_sessions", "list_blocks", "create_block", "run", "send_keys", "capture", "wait_for", "close_block" }, tools.Select(tool => tool.Name).ToList());

                CallToolResult run = await client.CallToolAsync("run", new Dictionary<string, object?> { ["command"] = new[] { "/bin/sh", "-c", "echo mcp-$((6*7)); exit 5" }, ["target"] = "agent" }, cancellationToken: cancellationToken).ConfigureAwait(false);
                string runText = Text(run);
                Assert.Contains("exit code 5", runText);
                Assert.Contains("mcp-42", runText);

                CallToolResult blocks = await client.CallToolAsync("list_blocks", new Dictionary<string, object?> { ["target"] = "agent" }, cancellationToken: cancellationToken).ConfigureAwait(false);
                string blockId = Text(blocks).Split(' ')[0];
                Assert.StartsWith("b", blockId);

                await client.CallToolAsync("send_keys", new Dictionary<string, object?> { ["target"] = blockId, ["keys"] = new[] { "echo keys-$((2*2))", "Enter" } }, cancellationToken: cancellationToken).ConfigureAwait(false);
                CallToolResult wait = await client.CallToolAsync("wait_for", new Dictionary<string, object?> { ["target"] = blockId, ["pattern"] = "^keys-4$", ["timeoutMs"] = 20_000 }, cancellationToken: cancellationToken).ConfigureAwait(false);
                Assert.StartsWith("pattern", Text(wait));

                ReadResourceResult resource = await client.ReadResourceAsync("weft://block/" + blockId, cancellationToken).ConfigureAwait(false);
                TextResourceContents screen = (TextResourceContents)resource.Contents[0];
                Assert.Contains("keys-4", screen.Text);
            }

            await serverStop.CancelAsync().ConfigureAwait(false);
            while (!server.IsCompleted)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string Text(CallToolResult result) =>
        string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
