using System.Buffers.Binary;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Renders the woven W into the Windows icon and package logo sizes.
/// </summary>
[TestClass]
public sealed class AppIconTests
{
    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The icon file holds every shell size, and each package logo has its declared pixel size.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public Task RendersEveryIconSize()
    {
        return DesktopApp.RunAsync(async () =>
        {
            string directory = Path.Join(DesktopApp.Root, "icon-" + Guid.NewGuid().ToString("N")[..8]);
            await AppIcon.RenderAsync(directory).ConfigureAwait(true);
            byte[] icon = await File.ReadAllBytesAsync(Path.Join(directory, "AppIcon.ico"),
                TestContext.CancellationToken).ConfigureAwait(true);
            Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2)), "The file is not an icon.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4));
            // A width byte of zero means 256 pixels.
            int[] sizes = [.. Enumerable.Range(0, count).Select(index => icon[6 + (index * 16)] == 0 ? 256
                : icon[6 + (index * 16)])];
            Assert.AreSequenceEqual([16, 20, 24, 32, 40, 48, 64, 256], sizes);

            foreach ((string name, int width, int height) in new[]
            {
                ("Square44x44Logo.targetsize-24.png", 24, 24),
                ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
                ("Square44x44Logo.scale-200.png", 88, 88),
                ("Square150x150Logo.scale-100.png", 150, 150),
                ("Wide310x150Logo.scale-200.png", 620, 300),
                ("StoreLogo.scale-100.png", 50, 50)
            })
            {
                byte[] png = await File.ReadAllBytesAsync(Path.Join(directory, name), TestContext.CancellationToken)
                    .ConfigureAwait(true);
                Assert.AreEqual(width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), name);
                Assert.AreEqual(height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)), name);
            }
        });
    }
}
