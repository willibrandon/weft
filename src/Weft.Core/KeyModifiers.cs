namespace Weft.Core;

/// <summary>
/// Modifier keys held during a key stroke.
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>
    /// No modifier.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Control key.
    /// </summary>
    Control = 1,

    /// <summary>
    /// The Alt or Meta key.
    /// </summary>
    Alt = 2,

    /// <summary>
    /// The Shift key.
    /// </summary>
    Shift = 4
}
