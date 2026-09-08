using System.Text.Json;

namespace Weft.Core;

/// <summary>
/// Reads the configuration file, falling back to defaults when it is absent.
/// </summary>
public static class WeftConfigLoader
{
    /// <summary>
    /// Loads a configuration file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="config">The configuration, defaults when the file is absent.</param>
    /// <param name="error">A description of a malformed file, or null.</param>
    /// <returns>Whether the file was absent or valid.</returns>
    public static bool TryLoad(string path, out WeftConfig config, out string? error)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        error = null;
        if (!File.Exists(path))
        {
            config = new WeftConfig();
            return true;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            config = JsonSerializer.Deserialize(bytes, WeftConfigJsonContext.Default.WeftConfig) ?? new WeftConfig();
            if (!KeyChord.TryParse(config.Leader, null, out _))
            {
                error = "The leader chord '" + config.Leader + "' is not valid.";
                config = new WeftConfig();
                return false;
            }

            return true;
        }
        catch (JsonException exception)
        {
            error = "Could not parse " + path + ": " + exception.Message;
            config = new WeftConfig();
            return false;
        }
        catch (IOException exception)
        {
            error = "Could not read " + path + ": " + exception.Message;
            config = new WeftConfig();
            return false;
        }
    }

    /// <summary>
    /// Loads the configuration from the resolved default path.
    /// </summary>
    /// <param name="error">A description of a malformed file, or null.</param>
    /// <returns>The configuration.</returns>
    public static WeftConfig LoadDefault(out string? error)
    {
        TryLoad(WeftPaths.ResolveConfigPath(), out WeftConfig config, out error);
        return config;
    }
}
