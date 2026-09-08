namespace Weft.Protocol;

/// <summary>
/// How a capture renders cells.
/// </summary>
public enum CaptureFormat
{
    /// <summary>
    /// Plain text with trailing spaces trimmed.
    /// </summary>
    Text,

    /// <summary>
    /// Text with ANSI styling sequences.
    /// </summary>
    Ansi
}
