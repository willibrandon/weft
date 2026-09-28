using Hex1b.Tokens;
using System.Text;
using Weft.Server;

namespace Weft.Tests;

/// <summary>
/// Checks hostile control payloads and fragmented headers through real file reads.
/// </summary>
[TestClass]
public sealed class CursorControlReaderTests
{
    /// <summary>
    /// Verifies controls embedded in strings or UTF-8 cannot masquerade as cursor commands.
    /// </summary>
    /// <param name="chunkSize">The largest file-read chunk.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(4096)]
    public void EmbeddedControlsDoNotEscapePayloads(int chunkSize)
    {
        string path = Path.Join(Path.GetTempPath(), "weft-control-" + Guid.NewGuid().ToString("N"));
        string payload = "\u001b]2;title \u001b7\u001b8\a"
            + "\u001b_Gfake;\u001b7\u001b8\u001b\\"
            + "\u001bPq\u001b7\u001b8\u001b\\"
            + "\u009dtitle \u001b7\u001b8\u009c"
            + "\u009ffake \u001b7\u001b8\u009c"
            + "\u0090q\u001b7\u001b8\u009c"
            + "💛\u001b7\u001b8";
        File.WriteAllText(path, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var observer = new CursorControlReader();
            List<AnsiToken> controls = [];
            using FileStream file = File.OpenRead(path);
            byte[] buffer = new byte[chunkSize];
            int count;
            while ((count = file.Read(buffer)) != 0)
            {
                int consumed = 0;
                while (consumed < count)
                {
                    consumed += observer.Read(buffer.AsSpan(consumed, count - consumed));
                    Assert.IsFalse(observer.Uncertain);
                    controls.AddRange(observer.Controls);
                }
            }
            Assert.HasCount(2, controls);
            _ = Assert.IsInstanceOfType<SaveCursorToken>(controls[0]);
            _ = Assert.IsInstanceOfType<RestoreCursorToken>(controls[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies oversized and eight-bit CSI headers invalidate observation without hiding later resets.
    /// </summary>
    [TestMethod]
    public void UncertainModesRequireRecovery()
    {
        string path = Path.Join(Path.GetTempPath(), "weft-control-" + Guid.NewGuid().ToString("N"));
        string payload = "\u001b[?" + new string('0', 4096) + "6h\u009b?6h\u001bc\u001b7\u001b8";
        File.WriteAllText(path, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var observer = new CursorControlReader();
            List<AnsiToken> controls = [];
            int uncertain = 0;
            using FileStream file = File.OpenRead(path);
            byte[] buffer = new byte[13];
            int count;
            while ((count = file.Read(buffer)) != 0)
            {
                int consumed = 0;
                while (consumed < count)
                {
                    consumed += observer.Read(buffer.AsSpan(consumed, count - consumed));
                    uncertain += observer.Uncertain ? 1 : 0;
                    controls.AddRange(observer.Controls);
                }
            }
            Assert.AreEqual(2, uncertain);
            Assert.HasCount(3, controls);
            _ = Assert.IsInstanceOfType<RisToken>(controls[0]);
            _ = Assert.IsInstanceOfType<SaveCursorToken>(controls[1]);
            _ = Assert.IsInstanceOfType<RestoreCursorToken>(controls[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
