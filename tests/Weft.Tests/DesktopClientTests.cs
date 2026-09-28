using System.Text.Json;
using Weft.Client;
using Weft.Protocol;

namespace Weft.Tests;

/// <summary>
/// Exercises the native desktop core through real server sockets and shell processes.
/// </summary>
[TestClass]
public sealed class DesktopClientTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies terminal cells, resizing, splits, tabs, and detach followed by reattach.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopRendersAndLeavesProcessesAlive()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "desktop" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                string runtime = Path.GetDirectoryName(fixture.SocketPath)!;
                var desktop = new DesktopClient(runtime, "/unused-existing-server", 100, 30, "/bin/bash");
                string blockId;
                int? processId;
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    blockId = ready.Blocks[0].Id;
                    processId = (await control.GetBlockAsync(blockId, token).ConfigureAwait(false)).Pid;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", blockId, "printf '\\033[2J\\033[H\\033[1;38;2;12;34;56m界é\\033[0m\\n\\033]8;;https://example.com\\007link\\033]8;;\\007\\n'\r")));
                    DesktopFrame rendered = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.Cells.Any(cell => cell.Text == "界" && cell.Foreground == 0x0c2238)
                        && block.Cells.Any(cell => cell.Link == "https://example.com")), token).ConfigureAwait(false);
                    DesktopBlockFrame block = rendered.Blocks.Single();
                    Assert.Contains(cell => cell.Text == "é", block.Cells);
                    Assert.Contains(cell => cell.Text.Length == 0, block.Cells, "Wide glyph continuation must survive the bridge.");
                    string json = JsonSerializer.Serialize(rendered, DesktopJsonContext.Default.DesktopFrame);
                    DesktopFrame roundTrip = JsonSerializer.Deserialize(json, DesktopJsonContext.Default.DesktopFrame)!;
                    Assert.AreSequenceEqual(block.Cells, roundTrip.Blocks.Single().Cells);
                    Assert.IsLessThan(block.Cells.Count * 16, System.Text.Encoding.UTF8.GetByteCount(json), "Ordinary cells must not repeat style metadata in each frame.");

                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("resize", Width: 120, Height: 40)));
                    _ = await WaitAsync(desktop, frame => frame.Blocks.Any(item => item.Width == 118 && item.Height == 38), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("splitRight", blockId)));
                    DesktopFrame split = await WaitAsync(desktop, frame => frame.Blocks.Count == 2 && frame.Blocks.Any(item => item.Active && item.Id != blockId), token).ConfigureAwait(false);
                    string other = split.Blocks.Single(item => item.Id != blockId).Id;
                    Assert.AreEqual(other, split.Blocks.Single(item => item.Active).Id);
                    foreach (string focus in new[] { blockId, other, blockId })
                    {
                        Assert.IsTrue(desktop.TrySend(new DesktopCommand("focus", focus)));
                        DesktopFrame focused = await WaitAsync(desktop, frame => frame.Blocks.Any(item => item.Id == focus && item.Active), token).ConfigureAwait(false);
                        Assert.AreEqual(focus, focused.Blocks.Single(item => item.Active).Id);
                    }
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("newTab")));
                    _ = await WaitAsync(desktop, frame => frame.Tabs.Count == 2 && frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("tab", ready.ActiveTab)));
                    _ = await WaitAsync(desktop, frame => frame.Blocks.Count == 2 && frame.Blocks.Any(item => item.Id == blockId), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("newSession", Text: "second")));
                    DesktopFrame second = await WaitAsync(desktop, frame => frame.Title == "second" && frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    Assert.AreEqual("/bin/bash", (await control.GetBlockAsync(second.Blocks[0].Id, token).ConfigureAwait(false)).Command);
                    Assert.HasCount(2, second.Sessions);
                    Assert.AreNotEqual(session.Id, second.ActiveSession);
                    Assert.AreEqual(0, (await control.GetSessionAsync(session.Id, token).ConfigureAwait(false)).Clients);
                    Assert.AreEqual(1, (await control.GetSessionAsync(second.ActiveSession, token).ConfigureAwait(false)).Clients);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("session", session.Id)));
                    _ = await WaitAsync(desktop, frame => frame.ActiveSession == session.Id && frame.Blocks.Any(item => item.Id == blockId), token).ConfigureAwait(false);
                    Assert.AreEqual(0, (await control.GetSessionAsync(second.ActiveSession, token).ConfigureAwait(false)).Clients);
                    Assert.AreEqual(processId, (await control.GetBlockAsync(blockId, token).ConfigureAwait(false)).Pid);
                }

                Assert.AreEqual(processId, (await control.GetBlockAsync(blockId, token).ConfigureAwait(false)).Pid);
                var reopened = new DesktopClient(runtime, "/unused-existing-server", 100, 30);
                await using (reopened.ConfigureAwait(false))
                {
                    DesktopFrame restored = await WaitAsync(reopened, frame => frame.Blocks.Any(block => block.Id == blockId), token).ConfigureAwait(false);
                    Assert.AreEqual("desktop", restored.Title);
                }
            }
        }
    }

    /// <summary>
    /// Verifies terminal key bytes and cursor-style requests survive the desktop connection.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopPreservesTerminalKeys()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "input" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                var desktop = new DesktopClient(Path.GetDirectoryName(fixture.SocketPath)!, "/unused-existing-server", 80, 24);
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    string id = ready.Blocks[0].Id;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "printf '\\033[1 qCURSOR\\055CHECK\\n'; IFS= read -r reply\r")));
                    DesktopFrame cursor = await WaitAsync(desktop, frame => Text(frame).Contains("CURSOR-CHECK", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.AreEqual(1, cursor.Blocks[0].CursorShape);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "\r")));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "stty raw -echo; printf '\\033[2J\\033[HREADY'; dd bs=1 count=5 of=keys.bin 2>/dev/null; stty sane; printf DONE\r")));
                    _ = await WaitAsync(desktop, frame => Text(frame).Contains("READY", StringComparison.Ordinal) && !Text(frame).Contains("stty", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("key", id, "Escape")));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("key", id, "C-b")));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("key", id, "F1")));
                    _ = await WaitAsync(desktop, frame => Text(frame).Contains("DONE", StringComparison.Ordinal), token).ConfigureAwait(false);
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Join(fixture.Root, "keys.bin"), token).ConfigureAwait(false);
                    Assert.AreSequenceEqual(new byte[] { 27, 2, 27, 79, 80 }, bytes);
                }
            }
        }
    }

    /// <summary>
    /// Verifies history predating attachment, stable inspection under output, and terminal mouse encoding.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopRetainsHistoryAndSendsMouseInput()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "history" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                _ = await control.TypeAsync(new BlockTextParams
                {
                    Target = session.Id,
                    Text = "for i in $(seq 1 120); do printf 'HISTORY-%03d\\n' \"$i\"; done\r"
                }, token).ConfigureAwait(false);
                _ = await control.WaitAsync(new BlockWaitParams { Target = session.Id, Pattern = "HISTORY-120" }, token).ConfigureAwait(false);
                var desktop = new DesktopClient(Path.GetDirectoryName(fixture.SocketPath)!, "/unused-existing-server", 80, 24);
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.HistoryLines > 50), token).ConfigureAwait(false);
                    string id = ready.Blocks[0].Id;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("scrollTo", id, Y: int.MaxValue)));
                    DesktopFrame history = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.ScrollOffset > 50), token).ConfigureAwait(false);
                    Assert.Contains("HISTORY-001", Text(history));
                    Assert.IsFalse(history.Blocks[0].CursorVisible);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "printf 'LIVE-OUTPUT\\n'\r")));
                    _ = await control.WaitAsync(new BlockWaitParams { Target = id, Pattern = "LIVE-OUTPUT" }, token).ConfigureAwait(false);
                    DesktopFrame stable = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.ScrollOffset > 50), token).ConfigureAwait(false);
                    Assert.AreEqual(Text(history), Text(stable));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("live", id)));
                    _ = await WaitAsync(desktop, frame => Text(frame).Contains("LIVE-OUTPUT", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "stty raw -echo; printf '\\033[2J\\033[H\\033[?1000h\\033[?1006hMOUSE-READY'; dd bs=1 count=9 of=mouse.bin 2>/dev/null; printf '\\033[?1000l\\033[?1006lMOUSE-DONE'; stty sane\r")));
                    _ = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.MouseTracking)
                        && Text(frame).Contains("MOUSE-READY", StringComparison.Ordinal) && !Text(frame).Contains("stty", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("mouse", id, "down", X: 3, Y: 4, Button: 1)));
                    _ = await WaitAsync(desktop, frame => Text(frame).Contains("MOUSE-DONE", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.AreEqual("\u001b[<0;4;5M", await File.ReadAllTextAsync(Path.Join(fixture.Root, "mouse.bin"), token).ConfigureAwait(false));
                }
            }
        }
    }

    /// <summary>
    /// Verifies reconnect retains content, drops uncertain commands, and reattaches to the same processes.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopReconnectsWithoutReplayingInput()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "reconnect" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                var desktop = new DesktopClient(Path.GetDirectoryName(fixture.SocketPath)!, "/unused-existing-server", 80, 24);
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Count == 1 && Text(frame).Contains('$', StringComparison.Ordinal), token).ConfigureAwait(false);
                    string id = ready.Blocks[0].Id;
                    int? pid = (await control.GetBlockAsync(id, token).ConfigureAwait(false)).Pid;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("reconnect")));
                    DesktopFrame stale = await WaitAsync(desktop, frame => !frame.Connected, token, allowDisconnected: true).ConfigureAwait(false);
                    Assert.AreEqual(Text(ready), Text(stale));
                    Assert.IsFalse(desktop.TrySend(new DesktopCommand("text", id, "touch should-not-exist\r")));
                    DesktopFrame restored = await WaitAsync(desktop, frame => frame.Connected && frame.Blocks.Count == 1, token, allowDisconnected: true).ConfigureAwait(false);
                    Assert.AreEqual(session.Id, restored.ActiveSession);
                    Assert.AreEqual(pid, (await control.GetBlockAsync(id, token).ConfigureAwait(false)).Pid);
                    Assert.IsFalse(File.Exists(Path.Join(fixture.Root, "should-not-exist")));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("renameSession", session.Id, "renamed")));
                    _ = await WaitAsync(desktop, frame => frame.Title == "renamed", token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("closeSession", session.Id)));
                    _ = await WaitAsync(desktop, frame => frame.Connected && frame.ActiveSession is null && frame.Blocks.Count == 0, token).ConfigureAwait(false);
                    for (int cycle = 0; cycle < 5; cycle++)
                    {
                        Assert.IsTrue(desktop.TrySend(new DesktopCommand("newSession", Text: "close-cycle")));
                        _ = await WaitAsync(desktop, frame => frame.Title == "close-cycle" && frame.Blocks.Count == 1, token).ConfigureAwait(false);
                        Assert.IsTrue(desktop.TrySend(new DesktopCommand("closeSession")));
                        _ = await WaitAsync(desktop, frame => frame.Connected && frame.ActiveSession is null && frame.Blocks.Count == 0, token).ConfigureAwait(false);
                    }
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("newSession", Text: "fresh")));
                    DesktopFrame fresh = await WaitAsync(desktop, frame => frame.Title == "fresh" && frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("reconnect")));
                    _ = await WaitAsync(desktop, frame => !frame.Connected, token, allowDisconnected: true).ConfigureAwait(false);
                    _ = await control.CloseSessionAsync(fresh.ActiveSession, token).ConfigureAwait(false);
                    _ = await WaitAsync(desktop, frame => frame.Connected && frame.ActiveSession is null && frame.Blocks.Count == 0,
                        token, allowDisconnected: true).ConfigureAwait(false);
                    Assert.IsEmpty((await control.ListSessionsAsync(token).ConfigureAwait(false)).Sessions);
                }
            }
        }
    }

    /// <summary>
    /// Verifies real terminal graphics retain complete placements and shared pixels across the native boundary.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopPreservesRasterPlacements()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "images" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                var desktop = new DesktopClient(Path.GetDirectoryName(fixture.SocketPath)!, "/unused-existing-server", 80, 24);
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    string id = ready.Blocks[0].Id;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[2J\\033[H\\033_Ga=T,f=32,s=2,v=1,c=8,r=4,q=2;/wAA/wD/AP8=\\033\\\\'\r")));
                    DesktopFrame graphic = await WaitAsync(desktop, frame => frame.Blocks.Any(block => block.Images.Count > 0), token).ConfigureAwait(false);
                    DesktopImage image = graphic.Blocks[0].Images[0];
                    Assert.AreEqual(2, image.PixelWidth);
                    Assert.AreEqual(1, image.PixelHeight);
                    Assert.HasCount(8, image.Data.ToArray());
                    Assert.AreEqual(8d, image.ClipWidth);
                    string json = JsonSerializer.Serialize(graphic, DesktopJsonContext.Default.DesktopFrame);
                    DesktopFrame decoded = JsonSerializer.Deserialize(json, DesktopJsonContext.Default.DesktopFrame)!;
                    Assert.HasCount(1, decoded.Blocks[0].Textures);
                    byte[] wire = DesktopWireFrame.Serialize(graphic);
                    Assert.IsTrue(image.Data.Span.SequenceEqual(wire.AsSpan(wire.Length - image.Data.Length)));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033_Ga=d,d=a,q=2\\033\\\\\\033[2J\\033[H\\033_Ga=T,f=32,s=2,v=1,X=3,Y=4,q=2;/wAA/wD/AP8=\\033\\\\'\r")));
                    DesktopFrame native = await WaitAsync(desktop, frame => frame.Blocks[0].Images.Any(raster => raster.ClipWidth < 1), token).ConfigureAwait(false);
                    DesktopImage nativeImage = native.Blocks[0].Images[0];
                    Assert.AreEqual(0.2d, nativeImage.ClipWidth, 0.0001);
                    Assert.AreEqual(0.05d, nativeImage.ClipHeight, 0.0001);
                    Assert.AreEqual(0.3d, nativeImage.ClipX, 0.0001);
                    Assert.AreEqual(0.2d, nativeImage.ClipY, 0.0001);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033_Ga=d,d=a,q=2\\033\\\\\\033[2J\\033[H\\033_Ga=t,i=99,f=32,s=2,v=1,q=2;/wAA/wD/AP8=\\033\\\\'; i=1; while [ \"$i\" -le 800 ]; do printf '\\033_Ga=p,i=99,p=%d,c=1,r=1,q=2\\033\\\\' \"$i\"; i=$((i+1)); done\r")));
                    DesktopFrame sprites = await WaitAsync(desktop, frame => frame.Blocks[0].Images.Count == 800, token).ConfigureAwait(false);
                    Assert.HasCount(1, sprites.Blocks[0].Textures);
                    DesktopFrame spriteCopy = JsonSerializer.Deserialize(JsonSerializer.Serialize(sprites,
                        DesktopJsonContext.Default.DesktopFrame), DesktopJsonContext.Default.DesktopFrame)!;
                    Assert.HasCount(800, spriteCopy.Blocks[0].Images);
                    Assert.HasCount(1, spriteCopy.Blocks[0].Textures);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[?2026h\\033_Ga=d,d=a,q=2\\033\\\\'; sleep .1; printf '\\033_Ga=p,i=99,p=1,c=1,r=1,q=2\\033\\\\'; sleep .1; printf '\\033_Ga=p,i=99,p=2,c=1,r=1,q=2\\033\\\\\\033[?2026l'\r")));
                    _ = await WaitAsync(desktop, frame =>
                    {
                        Assert.IsTrue(frame.Blocks[0].Images.Count is 800 or 2, "A synchronized sprite update was presented before completion.");
                        return frame.Blocks[0].Images.Count == 2;
                    }, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[?2026h\\033_Ga=d,d=a,q=2\\033\\\\\\033_Ga=p,i=99,p=1,c=1,r=1,q=2\\033\\\\'\r")));
                    _ = await WaitAsync(desktop, frame => frame.Blocks[0].Images.Count == 1, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "printf '\\033[?2026l'\r")));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033_Ga=d,d=a,q=2\\033\\\\\\033[2J\\033[H\\033Pq#1;2;100;0;0#1!20~\\033\\\\'\r")));
                    DesktopFrame sixel = await WaitAsync(desktop, frame => frame.Blocks.SelectMany(block => block.Images)
                        .Any(raster => raster.PixelWidth == 20 && raster.PixelHeight == 6), token).ConfigureAwait(false);
                    Assert.AreEqual(0d, sixel.Blocks[0].Images[0].ClipY);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[1;2H\\033Pq#2;2;0;0;100#2!10~\\033\\\\'\r")));
                    DesktopFrame layered = await WaitAsync(desktop, frame => frame.Blocks[0].Images.Any(raster =>
                        raster.PixelWidth == 20 && raster.PixelHeight == 6 && raster.Data.Span[42] == 255), token).ConfigureAwait(false);
                    ReadOnlyMemory<byte> plane = layered.Blocks[0].Images[0].Data;
                    Assert.IsTrue(plane.Span[..4].SequenceEqual(new byte[] { 255, 0, 0, 255 }));
                    Assert.IsTrue(plane.Span.Slice(40, 4).SequenceEqual(new byte[] { 0, 0, 255, 255 }));
                    Assert.AreEqual(0d, layered.Blocks[0].Images[0].ClipY);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[2J\\033[H\\033Pq#1;2;100;0;0#1!20~\\033\\\\\\033[H\\033P0;1q#2;2;0;0;100#2!20@\\033\\\\'\r")));
                    DesktopFrame transparent = await WaitAsync(desktop, frame => frame.Blocks[0].Images.Any(raster =>
                        raster.PixelWidth == 20 && raster.PixelHeight == 6 && raster.Data.Span[2] == 255), token).ConfigureAwait(false);
                    ReadOnlyMemory<byte> transparentPlane = transparent.Blocks[0].Images[0].Data;
                    Assert.IsTrue(transparentPlane.Span[..4].SequenceEqual(new byte[] { 0, 0, 255, 255 }));
                    Assert.IsTrue(transparentPlane.Span.Slice(80, 4).SequenceEqual(new byte[] { 255, 0, 0, 255 }));
                }
            }
        }
    }

    /// <summary>
    /// Verifies selection spans retained history and stays anchored while the process writes more output.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(90_000, CooperativeCancellation = true)]
    public async Task DesktopSelectionCrossesViewports()
    {
        CancellationToken token = TestContext.CancellationToken;
        var fixture = ServerFixture.Start();
        await using (fixture.ConfigureAwait(false))
        {
            await fixture.WaitReadyAsync(token).ConfigureAwait(false);
            ControlClient control = await fixture.ConnectAsync(token).ConfigureAwait(false);
            await using (control.ConfigureAwait(false))
            {
                SessionInfo session = await control.CreateSessionAsync(new SessionCreateParams { Name = "selection" }, token).ConfigureAwait(false);
                await ServerFixture.WaitForPromptAsync(control, session.Id, token).ConfigureAwait(false);
                var desktop = new DesktopClient(Path.GetDirectoryName(fixture.SocketPath)!, "/unused-existing-server", 100, 30);
                await using (desktop.ConfigureAwait(false))
                {
                    DesktopFrame ready = await WaitAsync(desktop, frame => frame.Blocks.Count == 1, token).ConfigureAwait(false);
                    string id = ready.Blocks[0].Id;
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id,
                        "printf '\\033[2J\\033[H'; for i in $(seq 1 100); do printf 'SEL-%03d\\n' \"$i\"; done\r")));
                    _ = await WaitAsync(desktop, frame => frame.Blocks[0].HistoryLines > 50 && Text(frame).Contains("SEL-100", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("scrollTo", id, Y: int.MaxValue)));
                    DesktopFrame first = await WaitAsync(desktop, frame => frame.Blocks[0].ScrollOffset == frame.Blocks[0].HistoryLines, token).ConfigureAwait(false);
                    int start = Text(first).IndexOf("SEL-001", StringComparison.Ordinal);
                    Assert.IsGreaterThanOrEqualTo(0, start);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("select", id, "cell", X: start % first.Blocks[0].Width, Y: start / first.Blocks[0].Width)));
                    _ = await WaitAsync(desktop, frame => frame.Blocks[0].Selection is not null, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("scrollTo", id, Y: 0)));
                    DesktopFrame last = await WaitAsync(desktop, frame => frame.Blocks[0].ScrollOffset == 0, token).ConfigureAwait(false);
                    Assert.AreEqual(first.Blocks[0].ViewVersion, last.Blocks[0].ViewVersion);
                    int end = Text(last).IndexOf("SEL-100", StringComparison.Ordinal) + 6;
                    Assert.IsGreaterThanOrEqualTo(6, end);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("select", id, "extend", X: end % last.Blocks[0].Width, Y: end / last.Blocks[0].Width)));
                    DesktopFrame selected = await WaitAsync(desktop, frame => frame.Blocks[0].Selection?.Text.Contains("SEL-100", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    string expected = string.Join('\n', Enumerable.Range(1, 100).Select(number => $"SEL-{number:000}"));
                    Assert.AreEqual(expected, selected.Blocks[0].Selection!.Text);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "printf 'AFTER-SELECTION\\n'\r")));
                    _ = await control.WaitAsync(new BlockWaitParams { Target = id, Pattern = "AFTER-SELECTION" }, token).ConfigureAwait(false);
                    DesktopFrame stable = await WaitAsync(desktop, frame => frame.Blocks[0].Selection is not null, token).ConfigureAwait(false);
                    Assert.AreEqual(expected, stable.Blocks[0].Selection!.Text);
                    Assert.AreEqual(Text(selected), Text(stable));
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("live", id)));
                    _ = await WaitAsync(desktop, frame => frame.Blocks[0].Selection is null && Text(frame).Contains("AFTER-SELECTION", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("text", id, "printf '\\033[2J\\033[Hab界écd\\n'\r")));
                    _ = await WaitAsync(desktop, frame => Text(frame).StartsWith("ab界écd", StringComparison.Ordinal), token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("select", id, "word", X: 6, Y: 0)));
                    DesktopFrame word = await WaitAsync(desktop, frame => frame.Blocks[0].Selection is not null, token).ConfigureAwait(false);
                    Assert.AreEqual("ab界écd", word.Blocks[0].Selection!.Text);
                    Assert.AreEqual(0, word.Blocks[0].Selection!.Start);
                    Assert.AreEqual(6, word.Blocks[0].Selection!.End);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("find", id, "ab界écd")));
                    _ = await WaitAsync(desktop, frame => frame.Blocks[0].SearchMatches == 1, token).ConfigureAwait(false);
                    Assert.IsTrue(desktop.TrySend(new DesktopCommand("resize", Width: 140, Height: 40)));
                    DesktopFrame resized = await WaitAsync(desktop, frame => frame.Blocks[0].Width == 138 && frame.Blocks[0].SearchMatches == 1, token).ConfigureAwait(false);
                    Assert.AreEqual("ab界écd", resized.Blocks[0].SearchQuery);
                }
            }
        }
    }

    private static string Text(DesktopFrame frame)
    {
        return string.Concat(frame.Blocks.SelectMany(block => block.Cells).Select(cell => cell.Text));
    }

    private static async Task<DesktopFrame> WaitAsync(DesktopClient desktop, Func<DesktopFrame, bool> predicate, CancellationToken token, bool allowDisconnected = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            DesktopFrame? frame = desktop.TakeFrame();
            if (frame is not null)
            {
                if (!allowDisconnected || frame.Connected)
                {
                    Assert.IsNull(frame.Error, frame.Error);
                }
                if (predicate(frame))
                {
                    return frame;
                }
            }

            await Task.Delay(20, timeout.Token).ConfigureAwait(false);
        }
    }
}
