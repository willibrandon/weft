using System.Net.Sockets;
using System.Text.Json;
using Weft.Core;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Verifies protocol messages survive a round trip over a real socket pair.
/// </summary>
[TestClass]
public sealed class ProtocolCodecTests
{
    /// <summary>
    /// Verifies a request that omits a defaulted field still carries the documented default after decoding.
    /// </summary>
    [TestMethod]
    public void OmittedFieldsKeepTheirDefaults()
    {
        BlockWaitParams wait = ProtocolCodec.FromElement(JsonDocument.Parse("{}").RootElement, ProtocolJsonContext.Default.BlockWaitParams);
        Assert.AreEqual(30_000, wait.TimeoutMs);

        BlockKillParams kill = ProtocolCodec.FromElement(JsonDocument.Parse("{}").RootElement, ProtocolJsonContext.Default.BlockKillParams);
        Assert.AreEqual(15, kill.Signal);
    }

    /// <summary>
    /// Verifies a request, a response, and an event written by one side are read intact by the other.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MessagesRoundTripOverSocketPair()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        string path = Path.Join(Path.GetTempPath(), "weft-codec-" + Guid.NewGuid().ToString("N")[..8] + ".sock");
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        try
        {
            using var clientSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await clientSocket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
            using Socket serverSocket = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            using var clientStream = new NetworkStream(clientSocket, ownsSocket: false);
            using var serverStream = new NetworkStream(serverSocket, ownsSocket: false);
            using var writer = new ProtocolWriter(clientStream);
            var reader = new ProtocolReader(serverStream);
            await using (reader.ConfigureAwait(false))
            {

                var split = new BlockSplitParams { Target = "main:1.2", Orientation = SplitOrientation.TopBottom, Size = 10 };
                await writer.WriteAsync(ProtocolCodec.Request(7, ProtocolMethods.BlockSplit, split, ProtocolJsonContext.Default.BlockSplitParams), cancellationToken).ConfigureAwait(false);
                await writer.WriteAsync(ProtocolCodec.Failure(8, ErrorCodes.NotFound, "gone"), cancellationToken).ConfigureAwait(false);
                await writer.WriteAsync(ProtocolCodec.Event(ProtocolEvents.SubscriberPaused, 42, new SubscriberPausedData { Dropped = 3 }, ProtocolJsonContext.Default.SubscriberPausedData), cancellationToken).ConfigureAwait(false);

                ProtocolMessage? request = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Assert.IsNotNull(request);
                Assert.IsTrue(request.IsRequest);
                Assert.AreEqual(7, request.Id);
                Assert.AreEqual(ProtocolMethods.BlockSplit, request.Method);
                BlockSplitParams decoded = ProtocolCodec.FromElement(request.Params, ProtocolJsonContext.Default.BlockSplitParams);
                Assert.AreEqual("main:1.2", decoded.Target);
                Assert.AreEqual(SplitOrientation.TopBottom, decoded.Orientation);
                Assert.AreEqual(10, decoded.Size);
                Assert.IsTrue(decoded.Focus);

                ProtocolMessage? failure = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Assert.IsNotNull(failure);
                Assert.IsTrue(failure.IsResponse);
                Assert.AreEqual(ErrorCodes.NotFound, failure.Error!.Code);

                ProtocolMessage? paused = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Assert.IsNotNull(paused);
                Assert.IsTrue(paused.IsEvent);
                Assert.AreEqual(42, paused.Seq);
                Assert.AreEqual(3, ProtocolCodec.FromElement(paused.Data, ProtocolJsonContext.Default.SubscriberPausedData).Dropped);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies enums serialize as camel-case strings and nulls are omitted.
    /// </summary>
    [TestMethod]
    public void EnumsAreCamelCaseAndNullsOmitted()
    {
        var split = new BlockSplitParams { Orientation = SplitOrientation.LeftRight };
        string json = ProtocolCodec.ToJson(ProtocolCodec.Request(1, ProtocolMethods.BlockSplit, split, ProtocolJsonContext.Default.BlockSplitParams));

        Assert.Contains("\"orientation\":\"leftRight\"", json);
        Assert.DoesNotContain("target", json);
        Assert.DoesNotContain("\"size\"", json);
    }

    /// <summary>
    /// Verifies malformed lines produce an invalidRequest error rather than an unhandled exception.
    /// </summary>
    [TestMethod]
    public void MalformedLineIsInvalidRequest()
    {
        ProtocolException exception = Assert.ThrowsExactly<ProtocolException>(() => ProtocolCodec.DecodeLine("{not json"u8));

        Assert.AreEqual(ErrorCodes.InvalidRequest, exception.Code);
    }

    /// <summary>
    /// Verifies missing required parameters are reported as invalidParams.
    /// </summary>
    [TestMethod]
    public void MissingRequiredParameterIsInvalidParams()
    {
        JsonElement element = JsonDocument.Parse("{\"target\":\"x\"}").RootElement.Clone();

        ProtocolException exception = Assert.ThrowsExactly<ProtocolException>(() => ProtocolCodec.FromElement(element, ProtocolJsonContext.Default.BlockSplitParams));

        Assert.AreEqual(ErrorCodes.InvalidParams, exception.Code);
    }

    /// <summary>
    /// Gets the test context supplied by the runner.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;
}
