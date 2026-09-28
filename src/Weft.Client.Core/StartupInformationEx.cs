using System.Runtime.InteropServices;

namespace Weft.Client;

/// <summary>
/// Mirrors STARTUPINFOEXW, which adds a process attribute list to the startup information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct StartupInformationEx
{
    private readonly StartupInformation _startup;
    private readonly nint _attributeList;

    /// <summary>
    /// Initializes the structure with its required size and an attribute list.
    /// </summary>
    /// <param name="attributeList">The initialized attribute list.</param>
    internal StartupInformationEx(nint attributeList)
    {
        _startup = new StartupInformation(Marshal.SizeOf<StartupInformationEx>());
        _attributeList = attributeList;
    }
}
