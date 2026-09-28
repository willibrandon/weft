using Weft.Client;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Weft.Desktop.Windows.Tests;

/// <summary>
/// Checks the pixels the compositor presents for text, redraws, cursors, and terminal graphics.
/// </summary>
[TestClass]
public sealed class TerminalRenderingTests
{
    private static readonly byte[] s_red = [255, 0, 0, 255];
    private static readonly byte[] s_green = [0, 255, 0, 255];

    /// <summary>
    /// Gets the test cancellation context.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Typing repaints only the prompt row, and erasing it restores the original pixels exactly.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task TypingRepaintsOnlyThePromptRow()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            // A steady cursor keeps the pixel comparisons independent of blinking.
            await window.RunAsync(@"print '\033[2J\033[H\033[2 q'").ConfigureAwait(true);
            DesktopBlockFrame block = (await window.WaitAsync(frame => frame.Blocks[0].CursorShape == 2
                && TestWindow.Text(frame).Trim() == "$").ConfigureAwait(true)).Blocks[0];
            window.Surface.FocusTerminal();
            (int left, int top, int right, int bottom) = window.SurfacePixels();
            WindowCapture before = await window.CaptureAsync().ConfigureAwait(true);
            await window.TypeAsync("typing").ConfigureAwait(true);
            _ = await window.WaitAsync(frame => frame.Blocks[0].CursorX == block.CursorX + 6
                && TestWindow.Text(frame).Contains("typing", StringComparison.Ordinal)).ConfigureAwait(true);
            WindowCapture typed = await window.CaptureAsync().ConfigureAwait(true);
            IReadOnlyList<int> changed = before.ChangedRows(typed, left, top, right, bottom);
            Rect row = window.Surface.CellRect(block, block.CursorY * block.Width);
            (_, int rowTop) = window.PixelOf(new Point(row.X, row.Y));
            (_, int rowBottom) = window.PixelOf(new Point(row.X, row.Bottom));
            Assert.IsNotEmpty(changed, "Typed text did not appear.");
            Assert.IsTrue(changed.All(y => y >= rowTop - 1 && y <= rowBottom + 1),
                "Typing repainted pixels outside the prompt row: rows " + changed[0] + " to " + changed[^1]
                + ", prompt row " + rowTop + " to " + rowBottom + ".");

            for (int index = 0; index < 6; index++)
            {
                window.Press(VirtualKey.Back);
            }

            _ = await window.WaitAsync(frame => frame.Blocks[0].CursorX == block.CursorX
                && !TestWindow.Text(frame).Contains("typing", StringComparison.Ordinal)).ConfigureAwait(true);
            WindowCapture erased = await window.CaptureAsync().ConfigureAwait(true);
            Assert.IsEmpty(before.ChangedRows(erased, left, top, right, bottom),
                "Erasing input left stale text or caret pixels.");
        });
    }

    /// <summary>
    /// True color text and wide characters reach the screen in their own cells.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task ColoredWideTextReachesThePixels()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[H\033[38;2;12;200;160m界界界\033[0m\n'").ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame => frame.Blocks[0].Cells[0].Text == "界")
                .ConfigureAwait(true);
            WindowCapture capture = await window.CaptureAsync().ConfigureAwait(true);
            DesktopBlockFrame block = shown.Blocks[0];
            Rect wide = window.Surface.CellRect(block, 2);
            wide.Width *= 2;
            var expected = Color.FromArgb(255, 12, 200, 160);
            Color nearest = Pixels(window, capture, wide).MinBy(color => TestWindow.Distance(color, expected));
            Assert.IsLessThan(90, TestWindow.Distance(nearest, expected),
                "The wide character's color did not reach the screen; the nearest pixel is " + nearest + ".");
            Rect beyond = window.Surface.CellRect(block, 7);
            Assert.DoesNotContain(color => TestWindow.Distance(color, expected) < 90, Pixels(window, capture, beyond),
                "Text drew outside its cells.");
        });
    }

    /// <summary>
    /// Kitty graphics draw, animate, crop, keep their orientation and fractional size, and release on deletion.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task KittyGraphicsDrawAndRelease()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[H\033_Ga=T,f=32,s=2,v=1,c=8,r=4,q=2;/wAA/wD/AP8=\033\\'")
                .ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame =>
                frame.Blocks[0].Images.Any(image => image.Format == 32)).ConfigureAwait(true);
            DesktopImage placement = shown.Blocks[0].Images[0];
            WindowCapture capture = await window.CaptureAsync().ConfigureAwait(true);
            Color red = Sample(window, capture, shown.Blocks[0], placement.ClipX + (placement.ClipWidth * 0.25),
                placement.ClipY + (placement.ClipHeight * 0.5));
            Color green = Sample(window, capture, shown.Blocks[0], placement.ClipX + (placement.ClipWidth * 0.75),
                placement.ClipY + (placement.ClipHeight * 0.5));
            Assert.IsTrue(red.R - red.G > 128 && green.G - green.R > 128,
                "Image pixels were not drawn where the image is placed: " + red + ", " + green + ".");

            await window.RunAsync(@"print '\033[2J\033[H\033_Ga=T,f=32,s=1,v=1,i=77,c=4,r=3,q=2;/wAA/w==\033\\"
                + @"\033_Ga=f,i=77,f=32,s=1,v=1,z=120,q=2;AP8A/w==\033\\"
                + @"\033_Ga=a,i=77,r=1,z=120,c=1,s=3,v=1,q=2\033\\'")
                .ConfigureAwait(true);
            DesktopFrame animated = await window.WaitAsync(frame => frame.Blocks[0].Images.Any(image =>
                frame.Blocks[0].Textures.Any(texture => TerminalImages.KeyOf(texture) == TerminalImages.KeyOf(image)
                    && texture.Data.Span[..4].SequenceEqual(s_green)))).ConfigureAwait(true);
            DesktopImage frameImage = animated.Blocks[0].Images[0];

            // Frames alternate every 120 ms, so the screen must show the second frame within a few captures.
            bool advanced = false;
            for (int attempt = 0; attempt < 20 && !advanced; attempt++)
            {
                Color color = Sample(window, await window.CaptureAsync().ConfigureAwait(true), window.Frame.Blocks[0],
                    frameImage.X + 0.5, frameImage.Y + 0.5);
                advanced = color.G - color.R > 128;
            }

            Assert.IsTrue(advanced, "The animation's second frame never reached the screen.");

            await DeleteImagesAsync(window).ConfigureAwait(true);
            string solid = Convert.ToBase64String(
                [.. Enumerable.Range(0, 15 * 9).SelectMany(_ => s_red)]);
            await window.RunAsync(@"print '\033[2J\033[H\033_Ga=T,f=32,s=15,v=9,X=3,Y=4,q=2;" + solid + @"\033\\'")
                .ConfigureAwait(true);
            DesktopFrame native = await window.WaitAsync(frame =>
                frame.Blocks[0].Images.Any(image => image.PixelWidth == 15)).ConfigureAwait(true);
            DesktopImage nativeImage = native.Blocks[0].Images[0];
            Assert.AreEqual(1.5, nativeImage.Width, 0.001, "Native image widths are not rounded to whole cells.");
            Assert.AreEqual(0.45, nativeImage.Height, 0.001, "Native image heights are not rounded to whole cells.");
            capture = await window.CaptureAsync().ConfigureAwait(true);
            Color inside = Sample(window, capture, native.Blocks[0], 1.05, 0.425);
            Color outside = Sample(window, capture, native.Blocks[0], 2.1, 0.425);
            Assert.IsTrue(inside.R - inside.G > 128 && outside.R - outside.G < 50,
                "Image pixels escaped their fractional placement: " + inside + ", " + outside + ".");

            await DeleteImagesAsync(window).ConfigureAwait(true);
            await window.RunAsync(@"print '\033[2J\033[H\033_Ga=T,f=32,s=2,v=1,x=1,w=1,c=4,r=2,X=3,Y=4,q=2;"
                + @"/wAA/wD/AP8=\033\\'")
                .ConfigureAwait(true);
            DesktopFrame crop = await window.WaitAsync(frame =>
                frame.Blocks[0].Images.Any(image => Math.Abs(image.ClipWidth - 3.7) < 0.001)).ConfigureAwait(true);
            Color cropped = Sample(window, await window.CaptureAsync().ConfigureAwait(true), crop.Blocks[0], 2, 1);
            Assert.IsGreaterThan(128, cropped.G - cropped.R,
                "A cropped image sampled the wrong source pixel: " + cropped + ".");

            await DeleteImagesAsync(window).ConfigureAwait(true);
            string corners = Convert.ToBase64String([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255]);
            await window.RunAsync(@"print '\033[2J\033[H\033_Ga=T,f=32,s=2,v=2,c=4,r=4,q=2;" + corners + @"\033\\'")
                .ConfigureAwait(true);
            DesktopFrame upright = await window.WaitAsync(frame =>
                frame.Blocks[0].Images.Any(image => image.PixelHeight == 2)).ConfigureAwait(true);
            capture = await window.CaptureAsync().ConfigureAwait(true);
            Color top = Sample(window, capture, upright.Blocks[0], 1, 1);
            Color bottom = Sample(window, capture, upright.Blocks[0], 1, 3);
            Assert.IsTrue(top.R - top.B > 128 && bottom.B - bottom.R > 128,
                "Image rows were drawn upside down: " + top + ", " + bottom + ".");
            await DeleteImagesAsync(window).ConfigureAwait(true);
        });
    }

    /// <summary>
    /// A Sixel image draws in its cells.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task SixelGraphicsDraw()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[H\033Pq#1;2;100;0;0#1~~~~~~~~~~\033\\'").ConfigureAwait(true);
            DesktopFrame shown = await window.WaitAsync(frame => frame.Blocks[0].Images.Count != 0)
                .ConfigureAwait(true);
            DesktopImage image = shown.Blocks[0].Images[0];
            Color color = Sample(window, await window.CaptureAsync().ConfigureAwait(true), shown.Blocks[0],
                image.ClipX + (image.ClipWidth / 2), image.ClipY + (image.ClipHeight / 2));
            Assert.IsGreaterThan(128, color.R - color.G, "The Sixel image was not drawn: " + color + ".");
        });
    }

    /// <summary>
    /// A focused blinking cursor blinks, an inactive window or steady style stops it, and blinking restarts.
    /// </summary>
    /// <returns>The test task.</returns>
    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public Task CursorBlinksOnlyWhenRequestedAndFocused()
    {
        return DesktopApp.RunWindowAsync(TestContext, async window =>
        {
            await window.RunAsync(@"print '\033[2J\033[H\033[1 q\033[?25hCURSOR\055READY\n'; read")
                .ConfigureAwait(true);
            _ = await window.WaitAsync(frame =>
                TestWindow.Text(frame).Contains("CURSOR-READY", StringComparison.Ordinal)
                && frame.Blocks[0].CursorShape == 1).ConfigureAwait(true);
            window.Surface.FocusTerminal();
            await window.UntilAsync(() => window.Surface.HasInputFocus).ConfigureAwait(true);
            window.Surface.SetWindowActive(true);
            try
            {
                Color lit = await CursorPixelAsync(window).ConfigureAwait(true);
                if (new UISettings().AnimationsEnabled)
                {
                    long deadline = Environment.TickCount64 + 2000;
                    Color current = lit;
                    while (TestWindow.Distance(lit, current) < 40 && Environment.TickCount64 < deadline)
                    {
                        current = await CursorPixelAsync(window).ConfigureAwait(true);
                    }

                    Assert.IsGreaterThanOrEqualTo(40, TestWindow.Distance(lit, current),
                        "The focused cursor did not blink.");
                }

                window.Surface.SetWindowActive(false);
                Color inactive = await CursorPixelAsync(window).ConfigureAwait(true);
                await window.DelayAsync(750).ConfigureAwait(true);
                Assert.IsLessThan(10,
                    TestWindow.Distance(inactive, await CursorPixelAsync(window).ConfigureAwait(true)),
                    "The cursor kept blinking in an inactive window.");
                Assert.IsGreaterThanOrEqualTo(40, TestWindow.Distance(inactive, TerminalAppearance.Background),
                    "An inactive window hid its cursor.");

                window.Surface.SetWindowActive(true);
                window.Press(VirtualKey.Enter);
                await window.RunAsync(@"print '\033[2 qSTEADY\055READY\n'; read").ConfigureAwait(true);
                _ = await window.WaitAsync(frame => frame.Blocks[0].CursorShape == 2
                    && TestWindow.Text(frame).Contains("STEADY-READY", StringComparison.Ordinal)).ConfigureAwait(true);
                Color steady = await CursorPixelAsync(window).ConfigureAwait(true);
                await window.DelayAsync(750).ConfigureAwait(true);
                Assert.IsLessThan(10, TestWindow.Distance(steady, await CursorPixelAsync(window).ConfigureAwait(true)),
                    "A steady cursor style blinked.");
                window.Press(VirtualKey.Enter);
                await window.RunAsync(@"print '\033[0 q'").ConfigureAwait(true);
                _ = await window.WaitAsync(frame => frame.Blocks[0].CursorShape == 0).ConfigureAwait(true);
            }
            finally
            {
                window.Surface.SetWindowActive(false);
            }
        });
    }

    private static async Task DeleteImagesAsync(TestWindow window)
    {
        await window.RunAsync(@"print '\033_Ga=d,d=a,q=2\033\\'").ConfigureAwait(true);
        _ = await window.WaitAsync(frame => frame.Blocks[0].Images.Count == 0).ConfigureAwait(true);
        await window.UntilAsync(() => window.Surface.RasterCacheCount == 0 && window.Surface.RasterCacheBytes == 0)
            .ConfigureAwait(true);
    }

    private static Color Sample(TestWindow window, WindowCapture capture, DesktopBlockFrame block, double x, double y)
    {
        Rect origin = window.Surface.CellRect(block, 0);
        return window.PixelAt(capture, new Point(origin.X + (x * origin.Width), origin.Y + (y * origin.Height)));
    }

    private static IEnumerable<Color> Pixels(TestWindow window, WindowCapture capture, Rect rect)
    {
        (int left, int top) = window.PixelOf(new Point(rect.X, rect.Y));
        (int right, int bottom) = window.PixelOf(new Point(rect.Right, rect.Bottom));
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                yield return capture.At(x, y);
            }
        }
    }

    private static async Task<Color> CursorPixelAsync(TestWindow window)
    {
        DesktopBlockFrame block = window.Active;
        WindowCapture capture = await window.CaptureAsync().ConfigureAwait(true);
        return window.PixelAt(capture, window.CellCenter(block, (block.CursorY * block.Width) + block.CursorX));
    }
}
