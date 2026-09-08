using System.Text.Json;

namespace Weft.Server;

/// <summary>
/// Persists session structure as one JSON file per session for resurrection.
/// </summary>
/// <param name="directory">The directory that holds session files.</param>
internal sealed class SessionStore(string directory)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Writes a session file atomically.
    /// </summary>
    /// <param name="session">The session to persist.</param>
    internal void Save(StoredSession session)
    {
        string path = PathFor(session.Name);
        string temporary = path + ".tmp";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(session, StoreJsonContext.Default.StoredSession);
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
    }

    /// <summary>
    /// Deletes a session file.
    /// </summary>
    /// <param name="name">The session name.</param>
    internal void Delete(string name)
    {
        lock (_gate)
        {
            File.Delete(PathFor(name));
        }
    }

    /// <summary>
    /// Loads a session file by name.
    /// </summary>
    /// <param name="name">The session name.</param>
    /// <returns>The stored session, or null when there is no readable file.</returns>
    internal StoredSession? Load(string name)
    {
        string path = PathFor(name);
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(File.ReadAllBytes(path), StoreJsonContext.Default.StoredSession);
            }
            catch (JsonException exception)
            {
                ServerLog.Warn("Ignoring unreadable session file " + path + ": " + exception.Message);
                return null;
            }
            catch (IOException exception)
            {
                ServerLog.Warn("Ignoring unreadable session file " + path + ": " + exception.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// Lists the names of stored sessions.
    /// </summary>
    /// <returns>The names.</returns>
    internal IReadOnlyList<string> ListNames()
    {
        lock (_gate)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            List<string> names = [];
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    private string PathFor(string name)
    {
        string safe = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(directory, safe + ".json");
    }
}
