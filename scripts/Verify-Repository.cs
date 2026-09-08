#!/usr/bin/env -S dotnet --
#:property TargetFramework=net10.0
#:property LangVersion=14.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.Text.RegularExpressions;

if (args.Length == 1 && args[0] is "--help" or "-h" or "-?")
{
    await Console.Out.WriteLineAsync("Verifies weft repository policies: no personal paths, no shell scripts, one type per file, documented members.").ConfigureAwait(false);
    await Console.Out.WriteLineAsync("Usage: dotnet run --file scripts/Verify-Repository.cs").ConfigureAwait(false);
    return 0;
}

string root = FindRepositoryRoot();
IReadOnlyList<string> tracked = await ReadTrackedPathsAsync(root).ConfigureAwait(false);
var failures = new List<string>();
VerifyFilePolicy(tracked, failures);
VerifyTrackedText(root, tracked, failures);
VerifySources(root, tracked, failures);
if (failures.Count != 0)
{
    foreach (string failure in failures.Order(StringComparer.Ordinal))
    {
        await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
    }

    return 1;
}

await Console.Out.WriteLineAsync($"Verified {tracked.Count} tracked files.").ConfigureAwait(false);
return 0;

static void VerifyFilePolicy(IReadOnlyList<string> tracked, ICollection<string> failures)
{
    string[] forbiddenExtensions = [".sh", ".ps1", ".psm1", ".bat", ".cmd"];
    foreach (string path in tracked)
    {
        string extension = Path.GetExtension(path);
        if (forbiddenExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"Repository automation must be a file-based C# app, not a script: {path}");
        }

        if (path.StartsWith("docs/references.md", StringComparison.Ordinal) || string.Equals(Path.GetFileName(path), "progress.local.md", StringComparison.Ordinal))
        {
            failures.Add($"Local-only documents must not be tracked: {path}");
        }
    }
}

static void VerifyTrackedText(string root, IReadOnlyList<string> tracked, ICollection<string> failures)
{
    Regex personalPath = Patterns.PersonalPath();
    string[] textExtensions = [".md", ".cs", ".csproj", ".props", ".targets", ".json", ".yml", ".yaml", ".slnx", ".editorconfig", ".globalconfig"];
    foreach (string path in tracked)
    {
        if (!textExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && !path.StartsWith('.'))
        {
            continue;
        }

        string full = Path.Combine(root, path);
        if (!File.Exists(full))
        {
            continue;
        }

        string text = File.ReadAllText(full);
        if (personalPath.IsMatch(text))
        {
            failures.Add($"Tracked files must not contain personal paths: {path}");
        }
    }
}

static void VerifySources(string root, IReadOnlyList<string> tracked, ICollection<string> failures)
{
    Regex typeDeclaration = Patterns.TypeDeclaration();
    foreach (string path in tracked)
    {
        if (!path.EndsWith(".cs", StringComparison.Ordinal) || !(path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal)))
        {
            continue;
        }

        string text = File.ReadAllText(Path.Combine(root, path));
        int topLevelTypes = 0;
        foreach (Match match in typeDeclaration.Matches(text))
        {
            string line = text[..match.Index];
            int depth = line.Count(c => c == '{') - line.Count(c => c == '}');
            if (depth == 0)
            {
                topLevelTypes++;
            }
        }

        if (topLevelTypes > 1)
        {
            failures.Add($"Each C# file holds exactly one type: {path} declares {topLevelTypes}.");
        }

        if (path.StartsWith("src/", StringComparison.Ordinal) && Patterns.VisibleType().IsMatch(text) && !text.Contains("/// <summary>", StringComparison.Ordinal))
        {
            failures.Add($"Public and internal types need XML documentation: {path}");
        }
    }
}

static string FindRepositoryRoot()
{
    string? directory = Directory.GetCurrentDirectory();
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory, "Weft.slnx")))
        {
            return directory;
        }

        directory = Path.GetDirectoryName(directory);
    }

    throw new InvalidOperationException("Run this from inside the weft repository.");
}

static async Task<IReadOnlyList<string>> ReadTrackedPathsAsync(string root)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false };
    start.ArgumentList.Add("ls-files");
    start.ArgumentList.Add("-z");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
    string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
    await process.WaitForExitAsync().ConfigureAwait(false);
    return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// Compile-time regular expressions used by the policy checks.
/// </summary>
internal static partial class Patterns
{
    /// <summary>
    /// Matches personal home directory paths.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"(/home/[a-z][a-z0-9_-]*|/Users/[A-Za-z][A-Za-z0-9_-]*|C:\\Users\\)", RegexOptions.CultureInvariant)]
    internal static partial Regex PersonalPath();

    /// <summary>
    /// Matches a type declaration at any nesting depth.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*(public|internal|private|protected|file)?\s*(static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*(class|interface|enum|record\s+struct|record|struct|delegate)\s+[A-Z]", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    internal static partial Regex TypeDeclaration();

    /// <summary>
    /// Matches a public or internal type declaration that needs documentation.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*(public|internal)\s+(static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+)*(class|interface|enum|record|struct)\s", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    internal static partial Regex VisibleType();
}
