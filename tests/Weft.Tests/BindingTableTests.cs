using Weft.Client;
using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies which configured bindings the client accepts.
/// </summary>
[TestClass]
public sealed class BindingTableTests
{
    /// <summary>
    /// Verifies key sequences are rejected rather than reserving a prefix in the terminal.
    /// </summary>
    [TestMethod]
    public void RejectsKeySequences()
    {
        var config = new WeftConfig { Bindings = { ["f12"] = "none", ["ctrl+b g"] = "lock" } };

        var table = BindingTable.Build(config);

        Assert.AreEqual(string.Empty, table.ChordFor(ClientActions.Lock));
        Assert.Contains(warning => warning.Contains("ctrl+b g", StringComparison.Ordinal), table.Warnings);
    }

    /// <summary>
    /// Verifies direct shortcuts can replace the default function keys.
    /// </summary>
    [TestMethod]
    public void AcceptsDirectShortcut()
    {
        var config = new WeftConfig { Bindings = { ["f12"] = "none", ["alt+g"] = "lock" } };

        var table = BindingTable.Build(config);

        Assert.AreEqual("Alt+G", table.ChordFor(ClientActions.Lock));
        Assert.IsEmpty(table.Warnings);
    }

    /// <summary>
    /// Verifies configuration cannot turn Esc into an exit shortcut.
    /// </summary>
    [TestMethod]
    public void EscapeCannotExitWeft()
    {
        var config = new WeftConfig { Bindings = { ["esc"] = "detach" } };
        var table = BindingTable.Build(config);
        Assert.DoesNotContain(binding => binding.Chord.Steps[0].Key == "escape", table.Bindings);
        Assert.HasCount(1, table.Warnings);
    }
}
