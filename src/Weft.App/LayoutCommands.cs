using System.CommandLine;
using System.Globalization;
using Weft.Client;
using Weft.Core;
using Weft.Protocol;

namespace Weft.App;

/// <summary>
/// Layout commands.
/// </summary>
internal static class LayoutCommands
{
    /// <summary>Creates the layout command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateLayout()
    {
        var command = new Command("layout", "Show, apply, or preset a tab's layout.");
        Argument<string?> target = CommonOptions.OptionalTarget("Tab or session.");
        var apply = new Option<string?>("--apply") { Description = "A layout string from a previous 'layout' call." };
        var preset = new Option<string?>("--preset") { Description = "even-horizontal, even-vertical, main-vertical, main-horizontal, tiled, or next." };
        var main = new Option<int>("--main") { Description = "Main block share in percent for main presets.", DefaultValueFactory = _ => 50 };
        command.Arguments.Add(target);
        command.Options.Add(apply);
        command.Options.Add(preset);
        command.Options.Add(main);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            string? effective = CommonOptions.EffectiveTarget(parseResult.GetValue(target));
            LayoutInfo layout;
            if (parseResult.GetValue(apply) is { Length: > 0 } text)
            {
                layout = await client.ApplyLayoutAsync(new LayoutApplyParams { Target = effective, Layout = text }, cancellationToken).ConfigureAwait(false);
            }
            else if (parseResult.GetValue(preset) is { Length: > 0 } name)
            {
                LayoutPreset? chosen = name.ToUpperInvariant() switch
                {
                    "EVEN-HORIZONTAL" => LayoutPreset.EvenHorizontal,
                    "EVEN-VERTICAL" => LayoutPreset.EvenVertical,
                    "MAIN-VERTICAL" => LayoutPreset.MainVertical,
                    "MAIN-HORIZONTAL" => LayoutPreset.MainHorizontal,
                    "TILED" => LayoutPreset.Tiled,
                    "NEXT" => null,
                    _ => throw new ProtocolException(ErrorCodes.InvalidParams, "Unknown preset " + name + ".")
                };
                layout = await client.PresetAsync(new LayoutPresetParams { Target = effective, Preset = chosen, MainPercent = parseResult.GetValue(main) }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                layout = await client.GetLayoutAsync(effective, cancellationToken).ConfigureAwait(false);
            }

            context.Write(layout, ProtocolJsonContext.Default.LayoutInfo, value =>
            {
                List<string> lines = [value.Serialized];
                lines.AddRange(value.Tiled.Select(p => p.Id + "  " + p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture) + "  " + p.Width.ToString(CultureInfo.InvariantCulture) + "x" + p.Height.ToString(CultureInfo.InvariantCulture)));
                return lines;
            });
        }, cancellationToken));
        return command;
    }

    /// <summary>Creates the resize command.</summary>
    /// <returns>The command.</returns>
    internal static Command CreateResize()
    {
        var command = new Command("resize", "Move one edge of a block.");
        Argument<string?> target = CommonOptions.OptionalTarget("Block to resize.");
        Option<LayoutDirection?> direction = BlockCommands.DirectionOption();
        var amount = new Option<int>("--amount", "-n") { Description = "Cells to move.", DefaultValueFactory = _ => 1 };
        command.Arguments.Add(target);
        command.Options.Add(direction);
        command.Options.Add(amount);
        command.SetAction((parseResult, cancellationToken) => SessionCommands.InvokeAsync(parseResult, async (context, client) =>
        {
            LayoutDirection chosen = parseResult.GetValue(direction) ?? throw new ProtocolException(ErrorCodes.InvalidParams, "A direction is required.");
            LayoutInfo layout = await client.ResizeAsync(new LayoutResizeParams { Target = CommonOptions.EffectiveTarget(parseResult.GetValue(target)), Direction = chosen, Amount = parseResult.GetValue(amount) }, cancellationToken).ConfigureAwait(false);
            context.Write(layout, ProtocolJsonContext.Default.LayoutInfo, value => [value.Serialized]);
        }, cancellationToken));
        return command;
    }
}
