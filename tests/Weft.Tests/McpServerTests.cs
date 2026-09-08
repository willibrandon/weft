using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.IO.Pipelines;
using Weft.Client;
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

    private static readonly string[] s_expectedTools = ["list_sessions", "list_blocks", "create_block", "run", "send_keys", "capture", "wait_for", "close_block"];
    private static readonly string[] s_runCommand = ["/bin/sh", "-c", "echo mcp-$((6*7)); exit 5"];
    private static readonly string[] s_keys = ["echo keys-$((2*2))", "Enter"];

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
            Task server = WeftMcpServer.RunStreamsAsync(token => ControlClient.ConnectAsync(fixture.SocketPath, token), "test", clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), verbose: false, serverStop.Token);
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()), cancellationToken: cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                CollectionAssert.IsSubsetOf(s_expectedTools, tools.Select(tool => tool.Name).ToList());

                CallToolResult run = await client.CallToolAsync("run", new Dictionary<string, object?> { ["command"] = s_runCommand, ["target"] = "agent" }, cancellationToken: cancellationToken).ConfigureAwait(false);
                string runText = Text(run);
                Assert.Contains("exit code 5", runText);
                Assert.Contains("mcp-42", runText);

                CallToolResult blocks = await client.CallToolAsync("list_blocks", new Dictionary<string, object?> { ["target"] = "agent" }, cancellationToken: cancellationToken).ConfigureAwait(false);
                string blockId = Text(blocks).Split(' ')[0];
                Assert.StartsWith("b", blockId);

                CallToolResult prompt = await client.CallToolAsync("wait_for", new Dictionary<string, object?> { ["target"] = blockId, ["pattern"] = "\\$\\s*$", ["timeoutMs"] = 20_000 }, cancellationToken: cancellationToken).ConfigureAwait(false);
                Assert.StartsWith("pattern", Text(prompt));

                await client.CallToolAsync("send_keys", new Dictionary<string, object?> { ["target"] = blockId, ["keys"] = s_keys }, cancellationToken: cancellationToken).ConfigureAwait(false);
                CallToolResult wait = await client.CallToolAsync("wait_for", new Dictionary<string, object?> { ["target"] = blockId, ["pattern"] = "^keys-4$", ["timeoutMs"] = 20_000 }, cancellationToken: cancellationToken).ConfigureAwait(false);
                Assert.StartsWith("pattern", Text(wait));

                ReadResourceResult resource = await client.ReadResourceAsync(new Uri("weft://block/" + blockId), cancellationToken: cancellationToken).ConfigureAwait(false);
                var screen = (TextResourceContents)resource.Contents[0];
                Assert.Contains("keys-4", screen.Text);
            }

            await serverStop.CancelAsync().ConfigureAwait(false);
            try
            {
                await server.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ClientLog.Debug("The MCP server stopped on request.");
            }
        }
    }

    private static string Text(CallToolResult result) =>
        string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
