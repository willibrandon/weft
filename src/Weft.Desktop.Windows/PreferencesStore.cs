using System.Text.Json;
using Weft.Client;

namespace Weft.Desktop.Windows;

/// <summary>
/// Loads and saves per-user desktop preferences and notifies open windows when they change.
/// </summary>
internal static class PreferencesStore
{
    private static readonly Lock s_gate = new();
    private static DesktopPreferences? s_current;

    /// <summary>
    /// Raised on the UI thread after preferences are saved.
    /// </summary>
    internal static event Action? Changed;

    /// <summary>
    /// Gets the preferences file, which lives beside other per-user Weft data.
    /// </summary>
    internal static string FilePath { get; } = ResolveFilePath();

    /// <summary>
    /// Gets the current preferences, reading the file once.
    /// </summary>
    internal static DesktopPreferences Current
    {
        get
        {
            lock (s_gate)
            {
                return s_current ??= Load();
            }
        }
    }

    /// <summary>
    /// Replaces the preferences, writes them atomically, and notifies listeners.
    /// </summary>
    /// <param name="update">Produces the new preferences from the current ones.</param>
    internal static void Update(Func<DesktopPreferences, DesktopPreferences> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        DesktopPreferences next;
        lock (s_gate)
        {
            next = update(s_current ??= Load());
            s_current = next;
        }

        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + ".tmp";
            string json = JsonSerializer.Serialize(next, PreferencesJsonContext.Default.DesktopPreferences);
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preferences still apply for this session when the file cannot be written.
            ClientLog.Debug("Preferences were not saved: " + exception.Message);
        }

        Changed?.Invoke();
    }

    private static string ResolveFilePath()
    {
        // Tests use a private file so they never read or change the user's preferences.
        if (Environment.GetEnvironmentVariable("WEFT_DESKTOP_PREFERENCES") is { Length: > 0 } explicitPath)
        {
            return Path.GetFullPath(explicitPath);
        }

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Join(localData, "weft", "desktop.json");
    }

    private static DesktopPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new DesktopPreferences();
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, PreferencesJsonContext.Default.DesktopPreferences)
                ?? new DesktopPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ClientLog.Debug("Preferences were not read: " + exception.Message);
            return new DesktopPreferences();
        }
    }
}
