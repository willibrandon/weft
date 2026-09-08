#!/usr/bin/env -S dotnet --
#:property TargetFramework=net10.0
#:property LangVersion=14.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true

using System.Diagnostics;
using System.Text.RegularExpressions;

if (args.Length == 1 && args[0] is "--help" or "-h" or "-?")
{
    await Console.Out.WriteLineAsync("Verifies weft repository policies: no personal paths, no scripts outside file-based C#, one type per file, documented members with three-line summaries.").ConfigureAwait(false);
    await Console.Out.WriteLineAsync("Usage: dotnet run --file scripts/Verify-Repository.cs").ConfigureAwait(false);
    return 0;
}

string root = FindRepositoryRoot();
IReadOnlyList<string> tracked = await ReadTrackedPathsAsync(root).ConfigureAwait(false);
var failures = new List<string>();
VerifyFilePolicy(root, tracked, failures);
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

static void VerifyFilePolicy(string root, IReadOnlyList<string> tracked, ICollection<string> failures)
{
    string[] scriptExtensions = [".sh", ".bash", ".zsh", ".ksh", ".fish", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".py", ".rb", ".pl", ".php", ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"];
    string[] scriptNames = ["Makefile", "makefile", "GNUmakefile", "justfile", "Justfile", "Taskfile.yml", "Taskfile.yaml", "Rakefile"];
    foreach (string path in tracked)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(path);
        if (scriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || scriptNames.Contains(name, StringComparer.Ordinal))
        {
            failures.Add($"Repository automation must be a file-based C# app, not a script: {path}");
        }
        else if (path.StartsWith("scripts/", StringComparison.Ordinal) && !extension.Equals(".cs", StringComparison.Ordinal) && !name.Equals("Directory.Build.props", StringComparison.Ordinal))
        {
            failures.Add($"Everything under scripts/ must be a file-based C# app or its build settings: {path}");
        }
        else if (HasForeignShebang(Path.Combine(root, path)))
        {
            failures.Add($"Repository automation must be a file-based C# app, not an interpreter script: {path}");
        }

        if (string.Equals(path, "docs/references.md", StringComparison.Ordinal))
        {
            failures.Add($"Local-only documents must not be tracked: {path}");
        }
    }
}

static bool HasForeignShebang(string full)
{
    if (!File.Exists(full))
    {
        return false;
    }

    using var reader = new StreamReader(full);
    string? first = reader.ReadLine();
    return first is not null && first.StartsWith("#!", StringComparison.Ordinal) && !first.Contains("dotnet", StringComparison.Ordinal);
}

static void VerifyTrackedText(string root, IReadOnlyList<string> tracked, ICollection<string> failures)
{
    Regex personalPath = Patterns.PersonalPath();
    string[] textExtensions = [".md", ".cs", ".csproj", ".props", ".targets", ".rsp", ".json", ".yml", ".yaml", ".slnx", ".editorconfig", ".globalconfig", ".config", ".txt", ".xml", ".resx", ".nuspec", ".toml", ".ini", ".html", ".css", ".svg", ".sarif"];
    foreach (string path in tracked)
    {
        string name = Path.GetFileName(path);
        if (!textExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && !name.StartsWith('.') && !string.Equals(name, "LICENSE", StringComparison.Ordinal))
        {
            continue;
        }

        string full = Path.Combine(root, path);
        if (!File.Exists(full))
        {
            continue;
        }

        string text = File.ReadAllText(full);
        Match match = personalPath.Match(text);
        if (match.Success)
        {
            failures.Add($"Tracked files must not contain personal paths: {path} contains '{match.Value}'.");
        }
    }
}

static void VerifySources(string root, IReadOnlyList<string> tracked, ICollection<string> failures)
{
    Regex typeDeclaration = Patterns.TypeDeclaration();
    foreach (string path in tracked)
    {
        if (!path.EndsWith(".cs", StringComparison.Ordinal) || !(path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal) || path.StartsWith("benchmarks/", StringComparison.Ordinal)))
        {
            continue;
        }

        string text = File.ReadAllText(Path.Combine(root, path));
        string code = Patterns.StringLiterals().Replace(text, string.Empty);
        int types = typeDeclaration.Count(code);
        bool assemblyAttributesOnly = types == 0 && text.Contains("[assembly:", StringComparison.Ordinal);
        if (types != 1 && !assemblyAttributesOnly)
        {
            failures.Add($"Each C# file holds exactly one type, nested types included: {path} declares {types}.");
        }

        VerifyDocumentation(path, code, failures);
    }
}

static void VerifyDocumentation(string path, string text, ICollection<string> failures)
{
    string[] lines = text.Split('\n');
    Regex visibleMember = Patterns.VisibleMember();
    for (int i = 0; i < lines.Length; i++)
    {
        string trimmed = Patterns.DocLine().Replace(lines[i].Trim(), "/// ");
        if (trimmed.StartsWith("/// <summary>", StringComparison.Ordinal))
        {
            // Exactly three lines: the opening tag, one line of text, the closing tag.
            bool wellFormed = trimmed.Equals("/// <summary>", StringComparison.Ordinal)
                && i + 2 < lines.Length
                && lines[i + 1].Trim() is { Length: > 4 } textLine && textLine.StartsWith("/// ", StringComparison.Ordinal) && !textLine.StartsWith("/// <", StringComparison.Ordinal)
                && lines[i + 2].Trim().Equals("/// </summary>", StringComparison.Ordinal);
            if (!wellFormed)
            {
                failures.Add($"XML summaries are exactly three lines, opening tag, text, closing tag: {path}:{i + 1}.");
            }
        }

        if (!visibleMember.IsMatch(lines[i]) && !IsInterfaceMember(lines, i))
        {
            continue;
        }

        // Walk up over attributes, which may span lines, to the line that must be documentation.
        int above = i - 1;
        while (above >= 0 && lines[above].Trim() is { Length: > 0 } candidate && !candidate.StartsWith("///", StringComparison.Ordinal) && !candidate.EndsWith(';') && !candidate.EndsWith('{') && !candidate.EndsWith('}'))
        {
            above--;
        }

        if (above < 0 || !lines[above].Trim().StartsWith("///", StringComparison.Ordinal))
        {
            failures.Add($"Public and internal members need XML documentation: {path}:{i + 1}.");
        }
    }
}

static bool IsInterfaceMember(string[] lines, int index)
{
    // Walk up to the enclosing type declaration at one less brace depth; members of an interface need no modifier.
    string trimmed = lines[index].Trim();
    if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('[') ||
        trimmed.StartsWith('{') || trimmed.StartsWith('}') || trimmed.StartsWith('#') ||
        !(trimmed.EndsWith(';') || trimmed.EndsWith('{') || trimmed.Contains('(', StringComparison.Ordinal) || trimmed.Contains("=>", StringComparison.Ordinal)))
    {
        return false;
    }

    int depth = 0;
    for (int above = index - 1; above >= 0; above--)
    {
        string line = lines[above];
        depth += line.Count(character => character == '}') - line.Count(character => character == '{');
        if (depth < 0)
        {
            return Patterns.InterfaceDeclaration().IsMatch(line);
        }
    }

    return false;
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
    var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.ArgumentList.Add("ls-files");
    start.ArgumentList.Add("-z");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
    string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
    string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
    await process.WaitForExitAsync().ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"git ls-files failed with exit code {process.ExitCode}: {error.Trim()}");
    }

    string[] paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    if (paths.Length == 0)
    {
        throw new InvalidOperationException("git ls-files listed no tracked files; refusing to verify an empty tree.");
    }

    return paths;
}

/// <summary>
/// Compile-time regular expressions used by the policy checks.
/// </summary>
internal static partial class Patterns
{
    /// <summary>
    /// Matches personal or machine-specific home directory paths on any platform.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"(/home/[a-z][a-z0-9_-]*|/ro[o]t(/|\b)|/Users/[A-Za-z][A-Za-z0-9_-]*|[A-Za-z]:[\\/]Users[\\/])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    internal static partial Regex PersonalPath();

    /// <summary>
    /// Matches a type declaration at any nesting depth.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*((public|internal|private|protected|file)\s+)*(static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+|new\s+|unsafe\s+)*(class|interface|enum|record\s+struct|record\s+class|record|struct|delegate)\s+\w", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    internal static partial Regex TypeDeclaration();

    /// <summary>
    /// Matches a public or internal member or type declaration that needs documentation.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*(public|internal|protected\s+internal)\s+[^=]*\S", RegexOptions.CultureInvariant)]
    internal static partial Regex VisibleMember();

    /// <summary>
    /// Matches string literals of every kind, so fixtures embedded in tests are not mistaken for code.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(""""
        """[\s\S]*?"""|@"(?:[^"]|"")*"|"(?:[^"\\\n]|\\.)*"
        """", RegexOptions.CultureInvariant)]
    internal static partial Regex StringLiterals();

    /// <summary>
    /// Matches the start of a documentation comment line with any spacing after the slashes.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^///\s*", RegexOptions.CultureInvariant)]
    internal static partial Regex DocLine();

    /// <summary>
    /// Matches an interface declaration line.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^\s*((public|internal|private|protected|file)\s+)*(partial\s+)?interface\s+\w", RegexOptions.CultureInvariant)]
    internal static partial Regex InterfaceDeclaration();
}
