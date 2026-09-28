using Weft.Core;

namespace Weft.Tests;

/// <summary>
/// Verifies configuration files load with comments and trailing commas, and fall back on errors.
/// </summary>
[TestClass]
public sealed class WeftConfigTests
{
    /// <summary>
    /// Verifies a commented file with trailing commas loads every documented setting.
    /// </summary>
    [TestMethod]
    public void LoadsCommentedFile()
    {
        string path = Path.Join(Path.GetTempPath(), "weft-config-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(path, /*lang=json*/ """
            {
              // appearance
              "frames": false,
              "sizePolicy": "smallest",
              "scrollback": 500,
              "shell": "/bin/sh",
              "theme": "ocean",
              "bindings": { "alt+left": "focus.left", "alt+enter": "block.zoom", },
              "hooks": { "block.exited": "true", },
            }
            """);
        try
        {
            Assert.IsTrue(WeftConfigLoader.TryLoad(path, out WeftConfig config, out string? error), error);
            Assert.IsFalse(config.Frames);
            Assert.AreEqual(SizePolicy.Smallest, config.SizePolicy);
            Assert.AreEqual(500, config.Scrollback);
            Assert.AreEqual("/bin/sh", config.Shell);
            Assert.AreEqual("ocean", config.Theme);
            Assert.AreEqual("focus.left", config.Bindings["alt+left"]);
            Assert.AreEqual("true", config.Hooks["block.exited"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies a missing file yields defaults and a malformed file reports an error with defaults.
    /// </summary>
    [TestMethod]
    public void MissingAndMalformedFiles()
    {
        string missing = Path.Join(Path.GetTempPath(), "weft-missing-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        Assert.IsTrue(WeftConfigLoader.TryLoad(missing, out WeftConfig defaults, out string? none));
        Assert.IsNull(none);
        Assert.IsTrue(defaults.Frames);

        string malformed = missing + ".bad";
        File.WriteAllText(malformed, "{ \"theme\": ");
        try
        {
            Assert.IsFalse(WeftConfigLoader.TryLoad(malformed, out WeftConfig fallback, out string? error));
            Assert.IsNotNull(error);
            Assert.AreEqual("default", fallback.Theme);
        }
        finally
        {
            File.Delete(malformed);
        }

        // Valid JSON that nulls a member the code relies on must fall back rather than throw later.
        string nulled = missing + ".null";
        File.WriteAllText(nulled, /*lang=json,strict*/ "{ \"bindings\": null }");
        try
        {
            Assert.IsFalse(WeftConfigLoader.TryLoad(nulled, out WeftConfig fallback, out string? error));
            Assert.IsNotNull(error);
            Assert.Contains("null", error);
            Assert.IsNotNull(fallback.Bindings);
        }
        finally
        {
            File.Delete(nulled);
        }
    }
}
