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
    /// Verifies a lock chord a single-stroke leader can never register is dropped with a warning, so lock mode stays refused.
    /// </summary>
    [TestMethod]
    public void RejectsChordTheLeaderCannotRegister()
    {
        var config = new WeftConfig { Leader = "ctrl+b", Bindings = { ["leader g"] = "none", ["leader g h"] = "lock" } };

        var table = BindingTable.Build(config);

        Assert.AreEqual(string.Empty, table.ChordFor(ClientActions.Lock));
        Assert.Contains(warning => warning.Contains("leader g h", StringComparison.Ordinal), table.Warnings);
    }

    /// <summary>
    /// Verifies one key after the leader is accepted as a rebinding.
    /// </summary>
    [TestMethod]
    public void AcceptsOneKeyAfterTheLeader()
    {
        var config = new WeftConfig { Leader = "ctrl+b", Bindings = { ["leader g"] = "none", ["leader h"] = "lock" } };

        var table = BindingTable.Build(config);

        Assert.AreEqual("Ctrl+B H", table.ChordFor(ClientActions.Lock));
        Assert.IsEmpty(table.Warnings);
    }
}
