namespace Weft.Client;

/// <summary>
/// Reads the size of the terminal the client runs in.
/// </summary>
internal static class HostSize
{
    /// <summary>
    /// Gets the host terminal size, falling back to a sane default when unavailable.
    /// </summary>
    /// <param name="fixedSize">A fixed size that overrides the console, for headless runs.</param>
    /// <returns>The columns and rows.</returns>
    internal static (int Width, int Height) Read((int Width, int Height)? fixedSize)
    {
        if (fixedSize is { } fixedValue)
        {
            return fixedValue;
        }

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
