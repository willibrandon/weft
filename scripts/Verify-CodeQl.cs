#!/usr/bin/env -S dotnet --
#:property TargetFramework=net10.0
#:property LangVersion=14.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Text.Json;

if (args.Length == 1 && args[0] is "--help" or "-h" or "-?")
{
    await Console.Out.WriteLineAsync("Fails when a CodeQL SARIF result contains any finding.").ConfigureAwait(false);
    await Console.Out.WriteLineAsync("Usage: dotnet run --file scripts/Verify-CodeQl.cs -- <sarif-directory>").ConfigureAwait(false);
    return 0;
}

if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("Usage: dotnet run --file scripts/Verify-CodeQl.cs -- <sarif-directory>").ConfigureAwait(false);
    return 2;
}

string sarifDirectory = Path.GetFullPath(args[0]);
if (!Directory.Exists(sarifDirectory))
{
    await Console.Error.WriteLineAsync($"CodeQL SARIF directory does not exist: {sarifDirectory}").ConfigureAwait(false);
    return 1;
}

string[] sarifPaths = [.. Directory.EnumerateFiles(sarifDirectory, "*.sarif", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
if (sarifPaths.Length == 0)
{
    await Console.Error.WriteLineAsync($"CodeQL produced no SARIF files under {sarifDirectory}.").ConfigureAwait(false);
    return 1;
}

var findings = new List<string>();
foreach (string sarifPath in sarifPaths)
{
    using FileStream stream = File.OpenRead(sarifPath);
    using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
    if (!document.RootElement.TryGetProperty("runs", out JsonElement runs) || runs.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidDataException($"CodeQL SARIF has no runs array: {sarifPath}");
    }

    foreach (JsonElement run in runs.EnumerateArray())
    {
        if (!run.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            string ruleId = SarifRead.ReadString(result, "ruleId", "unknown-rule");
            string level = SarifRead.ReadString(result, "level", "warning");
            string message = result.TryGetProperty("message", out JsonElement messageElement)
                ? SarifRead.ReadString(messageElement, "text", "CodeQL finding")
                : "CodeQL finding";
            findings.Add($"{level}: {ruleId}: {SarifRead.ReadLocation(result)}: {message}");
        }
    }
}

if (findings.Count != 0)
{
    foreach (string finding in findings.Order(StringComparer.Ordinal))
    {
        await Console.Error.WriteLineAsync(finding).ConfigureAwait(false);
    }

    await Console.Error.WriteLineAsync($"CodeQL reported {findings.Count} finding(s).").ConfigureAwait(false);
    return 1;
}

await Console.Out.WriteLineAsync($"Verified {sarifPaths.Length} CodeQL SARIF file(s) with no findings.").ConfigureAwait(false);
return 0;

/// <summary>
/// Reads the SARIF fields used by the CodeQL repository check.
/// </summary>
internal static class SarifRead
{
    /// <summary>
    /// Reads a string property or returns its fallback value.
    /// </summary>
    /// <param name="element">The object containing the property.</param>
    /// <param name="name">The property name.</param>
    /// <param name="fallback">The value to use when the property is absent.</param>
    /// <returns>The property value or the fallback.</returns>
    internal static string ReadString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    /// <summary>
    /// Reads the first physical location of a SARIF result.
    /// </summary>
    /// <param name="result">The SARIF result.</param>
    /// <returns>The location, or a placeholder when it is absent.</returns>
    internal static string ReadLocation(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out JsonElement locations) || locations.ValueKind != JsonValueKind.Array || locations.GetArrayLength() == 0)
        {
            return "unknown-location";
        }

        JsonElement first = locations[0];
        if (!first.TryGetProperty("physicalLocation", out JsonElement physical))
        {
            return "unknown-location";
        }

        string uri = physical.TryGetProperty("artifactLocation", out JsonElement artifact) ? ReadString(artifact, "uri", "unknown-file") : "unknown-file";
        int line = physical.TryGetProperty("region", out JsonElement region) && region.TryGetProperty("startLine", out JsonElement start) && start.ValueKind == JsonValueKind.Number
            ? start.GetInt32()
            : 0;
        return line > 0 ? $"{uri}:{line}" : uri;
    }
}
