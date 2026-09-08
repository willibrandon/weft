using Weft.Protocol;

namespace Weft.Client;

/// <summary>
/// Typed wrappers over the control protocol methods.
/// </summary>
public static class ControlClientExtensions
{
    /// <summary>Calls server.info.</summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<ServerInfoResult> ServerInfoAsync(this ControlClient client, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.ServerInfo, new TargetParams(), ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.ServerInfoResult, cancellationToken);

    /// <summary>Calls server.shutdown.</summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> ShutdownAsync(this ControlClient client, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.ServerShutdown, new TargetParams(), ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls session.list.</summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<SessionListResult> ListSessionsAsync(this ControlClient client, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionList, new TargetParams(), ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.SessionListResult, cancellationToken);

    /// <summary>Calls session.create.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<SessionInfo> CreateSessionAsync(this ControlClient client, SessionCreateParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionCreate, parameters, ProtocolJsonContext.Default.SessionCreateParams, ProtocolJsonContext.Default.SessionInfo, cancellationToken);

    /// <summary>Calls session.get.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<SessionInfo> GetSessionAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionGet, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.SessionInfo, cancellationToken);

    /// <summary>Calls session.rename.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<SessionInfo> RenameSessionAsync(this ControlClient client, SessionRenameParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionRename, parameters, ProtocolJsonContext.Default.SessionRenameParams, ProtocolJsonContext.Default.SessionInfo, cancellationToken);

    /// <summary>Calls session.close.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> CloseSessionAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionClose, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls session.attach.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<SessionAttachResult> AttachAsync(this ControlClient client, SessionAttachParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionAttach, parameters, ProtocolJsonContext.Default.SessionAttachParams, ProtocolJsonContext.Default.SessionAttachResult, cancellationToken);

    /// <summary>Calls session.detach.</summary>
    /// <param name="client">The client.</param>
    /// <param name="clientId">The attached client id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> DetachAsync(this ControlClient client, string clientId, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionDetach, new ClientParams { Client = clientId }, ProtocolJsonContext.Default.ClientParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls session.setSize.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SetSizeAsync(this ControlClient client, SessionSetSizeParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionSetSize, parameters, ProtocolJsonContext.Default.SessionSetSizeParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls session.activate.</summary>
    /// <param name="client">The client.</param>
    /// <param name="clientId">The attached client id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> ActivateAsync(this ControlClient client, string clientId, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.SessionActivate, new ClientParams { Client = clientId }, ProtocolJsonContext.Default.ClientParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls tab.sync.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabInfo> SyncTabAsync(this ControlClient client, TabSyncParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabSync, parameters, ProtocolJsonContext.Default.TabSyncParams, ProtocolJsonContext.Default.TabInfo, cancellationToken);

    /// <summary>Calls block.sync.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockInfo> SyncBlockAsync(this ControlClient client, BlockSyncParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockSync, parameters, ProtocolJsonContext.Default.BlockSyncParams, ProtocolJsonContext.Default.BlockInfo, cancellationToken);

    /// <summary>Calls tab.list.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabListResult> ListTabsAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabList, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.TabListResult, cancellationToken);

    /// <summary>Calls tab.create.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabInfo> CreateTabAsync(this ControlClient client, TabCreateParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabCreate, parameters, ProtocolJsonContext.Default.TabCreateParams, ProtocolJsonContext.Default.TabInfo, cancellationToken);

    /// <summary>Calls tab.select.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabInfo> SelectTabAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabSelect, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.TabInfo, cancellationToken);

    /// <summary>Calls tab.rename.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabInfo> RenameTabAsync(this ControlClient client, TabRenameParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabRename, parameters, ProtocolJsonContext.Default.TabRenameParams, ProtocolJsonContext.Default.TabInfo, cancellationToken);

    /// <summary>Calls tab.close.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> CloseTabAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.TabClose, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.list.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockListResult> ListBlocksAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockList, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.BlockListResult, cancellationToken);

    /// <summary>Calls block.get.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockInfo> GetBlockAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockGet, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.BlockInfo, cancellationToken);

    /// <summary>Calls block.split.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockInfo> SplitAsync(this ControlClient client, BlockSplitParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockSplit, parameters, ProtocolJsonContext.Default.BlockSplitParams, ProtocolJsonContext.Default.BlockInfo, cancellationToken);

    /// <summary>Calls block.close.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> CloseBlockAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockClose, new BlockCloseParams { Target = target }, ProtocolJsonContext.Default.BlockCloseParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.kill.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> KillBlockAsync(this ControlClient client, BlockKillParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockKill, parameters, ProtocolJsonContext.Default.BlockKillParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.rename.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockInfo> RenameBlockAsync(this ControlClient client, BlockRenameParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockRename, parameters, ProtocolJsonContext.Default.BlockRenameParams, ProtocolJsonContext.Default.BlockInfo, cancellationToken);

    /// <summary>Calls block.focus.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockInfo> FocusAsync(this ControlClient client, BlockFocusParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockFocus, parameters, ProtocolJsonContext.Default.BlockFocusParams, ProtocolJsonContext.Default.BlockInfo, cancellationToken);

    /// <summary>Calls block.zoom.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<TabInfo> ZoomAsync(this ControlClient client, BlockZoomParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockZoom, parameters, ProtocolJsonContext.Default.BlockZoomParams, ProtocolJsonContext.Default.TabInfo, cancellationToken);

    /// <summary>Calls block.swap.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SwapAsync(this ControlClient client, BlockSwapParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockSwap, parameters, ProtocolJsonContext.Default.BlockSwapParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.float.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> FloatAsync(this ControlClient client, BlockFloatParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockFloat, parameters, ProtocolJsonContext.Default.BlockFloatParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls block.tile.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> TileAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockTile, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls block.move.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> MoveAsync(this ControlClient client, BlockMoveParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockMove, parameters, ProtocolJsonContext.Default.BlockMoveParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls block.sendKeys.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SendKeysAsync(this ControlClient client, BlockSendKeysParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockSendKeys, parameters, ProtocolJsonContext.Default.BlockSendKeysParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.type.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> TypeAsync(this ControlClient client, BlockTextParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockType, parameters, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.paste.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> PasteAsync(this ControlClient client, BlockTextParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockPaste, parameters, ProtocolJsonContext.Default.BlockTextParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls block.capture.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockCaptureResult> CaptureAsync(this ControlClient client, BlockCaptureParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockCapture, parameters, ProtocolJsonContext.Default.BlockCaptureParams, ProtocolJsonContext.Default.BlockCaptureResult, cancellationToken);

    /// <summary>Calls block.wait.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockWaitResult> WaitAsync(this ControlClient client, BlockWaitParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockWait, parameters, ProtocolJsonContext.Default.BlockWaitParams, ProtocolJsonContext.Default.BlockWaitResult, cancellationToken);

    /// <summary>Calls block.run.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<BlockRunResult> RunAsync(this ControlClient client, BlockRunParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.BlockRun, parameters, ProtocolJsonContext.Default.BlockRunParams, ProtocolJsonContext.Default.BlockRunResult, cancellationToken);

    /// <summary>Calls layout.get.</summary>
    /// <param name="client">The client.</param>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> GetLayoutAsync(this ControlClient client, string? target, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.LayoutGet, new TargetParams { Target = target }, ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls layout.apply.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> ApplyLayoutAsync(this ControlClient client, LayoutApplyParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.LayoutApply, parameters, ProtocolJsonContext.Default.LayoutApplyParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls layout.preset.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> PresetAsync(this ControlClient client, LayoutPresetParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.LayoutPreset, parameters, ProtocolJsonContext.Default.LayoutPresetParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls layout.resize.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<LayoutInfo> ResizeAsync(this ControlClient client, LayoutResizeParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.LayoutResize, parameters, ProtocolJsonContext.Default.LayoutResizeParams, ProtocolJsonContext.Default.LayoutInfo, cancellationToken);

    /// <summary>Calls events.subscribe; events then arrive on <see cref="ControlClient.Events"/>.</summary>
    /// <param name="client">The client.</param>
    /// <param name="since">The last seen sequence number, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SubscribeAsync(this ControlClient client, long? since, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.EventsSubscribe, new EventsSubscribeParams { Since = since }, ProtocolJsonContext.Default.EventsSubscribeParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls paste.get.</summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<PasteBuffer> GetPasteAsync(this ControlClient client, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.PasteGet, new TargetParams(), ProtocolJsonContext.Default.TargetParams, ProtocolJsonContext.Default.PasteBuffer, cancellationToken);

    /// <summary>Calls paste.set.</summary>
    /// <param name="client">The client.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SetPasteAsync(this ControlClient client, string text, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.PasteSet, new PasteBuffer { Text = text }, ProtocolJsonContext.Default.PasteBuffer, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls wait.for.</summary>
    /// <param name="client">The client.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> WaitForAsync(this ControlClient client, WaitChannelParams parameters, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.WaitFor, parameters, ProtocolJsonContext.Default.WaitChannelParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    /// <summary>Calls wait.signal.</summary>
    /// <param name="client">The client.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static Task<EmptyResult> SignalAsync(this ControlClient client, string channel, CancellationToken cancellationToken) =>
        InvokeAsync(client, ProtocolMethods.WaitSignal, new WaitChannelParams { Channel = channel }, ProtocolJsonContext.Default.WaitChannelParams, ProtocolJsonContext.Default.EmptyResult, cancellationToken);

    private static Task<TResult> InvokeAsync<TParams, TResult>(
        ControlClient client,
        string method,
        TParams parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TParams> parameterInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.InvokeAsync(method, parameters, parameterInfo, resultInfo, cancellationToken);
    }
}
