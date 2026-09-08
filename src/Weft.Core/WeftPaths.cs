namespace Weft.Core;

/// <summary>
/// Resolves the directories and socket paths weft uses on this machine.
/// </summary>
public static class WeftPaths
{
    /// <summary>
    /// The environment variable that overrides the runtime directory.
    /// </summary>
    public const string SocketDirectoryVariable = "WEFT_SOCKET_DIR";

    /// <summary>
    /// The environment variable that overrides the state directory.
    /// </summary>
    public const string StateDirectoryVariable = "WEFT_STATE_DIR";

    /// <summary>
    /// The environment variable that overrides the configuration file path.
    /// </summary>
    public const string ConfigPathVariable = "WEFT_CONFIG";

    /// <summary>
    /// Resolves the runtime directory that holds sockets and the server lock.
    /// </summary>
    /// <returns>The directory path, which may not exist yet.</returns>
    public static string ResolveRuntimeDirectory()
    {
        string? explicitDirectory = Environment.GetEnvironmentVariable(SocketDirectoryVariable);
        if (!string.IsNullOrEmpty(explicitDirectory))
        {
            return Path.GetFullPath(explicitDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "weft", "run");
        }

        string? xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntime))
        {
            return Path.Join(xdgRuntime, "weft");
        }

        string tmp = Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } tmpdir ? tmpdir : "/tmp";
        return Path.Join(tmp, "weft-" + Environment.UserName);
    }

    /// <summary>
    /// Resolves the directory that holds persisted session state.
    /// </summary>
    /// <returns>The directory path, which may not exist yet.</returns>
    public static string ResolveStateDirectory()
    {
        string? explicitDirectory = Environment.GetEnvironmentVariable(StateDirectoryVariable);
        if (!string.IsNullOrEmpty(explicitDirectory))
        {
            return Path.GetFullPath(explicitDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "weft", "state");
        }

        string? xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        string stateHome = !string.IsNullOrEmpty(xdgState)
            ? xdgState
            : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Join(stateHome, "weft");
    }

    /// <summary>
    /// Resolves the configuration file path.
    /// </summary>
    /// <returns>The file path, which may not exist.</returns>
    public static string ResolveConfigPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable(ConfigPathVariable);
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "weft", "config.json");
        }

        string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string configHome = !string.IsNullOrEmpty(xdgConfig)
            ? xdgConfig
            : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Join(configHome, "weft", "config.json");
    }

    /// <summary>
    /// Gets the control socket path inside a runtime directory.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <returns>The socket path.</returns>
    public static string ControlSocketPath(string runtimeDirectory) => Path.Join(runtimeDirectory, "weft.sock");

    /// <summary>
    /// Gets the server lock file path inside a runtime directory.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <returns>The lock file path.</returns>
    public static string LockFilePath(string runtimeDirectory) => Path.Join(runtimeDirectory, "server.lock");

    /// <summary>
    /// Gets the directory that holds block sockets inside a runtime directory.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <returns>The directory path.</returns>
    public static string BlockSocketDirectory(string runtimeDirectory) => Path.Join(runtimeDirectory, "blocks");

    /// <summary>
    /// Gets a block's HMP1 socket path inside a runtime directory.
    /// </summary>
    /// <param name="runtimeDirectory">The runtime directory.</param>
    /// <param name="block">The block.</param>
    /// <returns>The socket path.</returns>
    public static string BlockSocketPath(string runtimeDirectory, BlockId block) =>
        Path.Join(BlockSocketDirectory(runtimeDirectory), $"{block}.sock");
}
