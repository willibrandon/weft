namespace Weft.Client;

/// <summary>
/// Reads the size of the terminal the client runs in.
/// </summary>
internal static class HostSize
{
    /// <summary>
    /// Gets the host terminal size, falling back to a sane default when unavailable.
    /// </summary>
    /// <returns>The columns and rows.</returns>
    internal static (int Width, int Height) Read()
    {
        try
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            return (width > 0 ? width : 80, height > 0 ? height : 24);
        }
        catch (IOException)
        {
            return (80, 24);
        }
        catch (PlatformNotSupportedException)
        {
            return (80, 24);
        }
    }
}
