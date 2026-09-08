namespace Weft.Server;

/// <summary>
/// An exclusive lock file that keeps a second server from starting on the same runtime directory.
/// </summary>
internal sealed class ServerLock : IDisposable
{
    private readonly FileStream _stream;

    private ServerLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Tries to take the lock.
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <returns>The lock, or null when another process holds it.</returns>
    internal static ServerLock? TryAcquire(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                stream.SetLength(0);
                byte[] pid = System.Text.Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                stream.Write(pid);
                stream.Flush();
                return new ServerLock(stream);
            }
            catch
            {
                // A failure after the file opened must not leave the handle open, or the lock looks taken.
                stream.Dispose();
                throw;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Releases the lock.
    /// </summary>
    public void Dispose() => _stream.Dispose();
}
