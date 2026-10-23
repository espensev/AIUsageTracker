// <copyright file="ScreenshotComparisonTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageTracker.Tests.UI;

public sealed class ScreenshotComparisonTests
{
    [Theory]
    [InlineData(0, 0.0, true)]
    [InlineData(1, 0.5, true)]
    [InlineData(2, 1.0, false)]
    [InlineData(3, 1.5, false)]
    public void Compare_ChangedPixels_PreservesStrictOnePercentBoundary(int changedPixels, double percentage, bool accepted)
    {
        WithDirectory(directory =>
        {
            var expected = new byte[200 * 4];
            var actual = new byte[200 * 4];
            for (var i = 0; i < changedPixels; i++)
            {
                actual[i * 4] = 1;
            }

            WritePng(Path.Combine(directory, "expected.png"), 20, 10, expected, PixelFormats.Bgra32);
            WritePng(Path.Combine(directory, "actual.png"), 20, 10, actual, PixelFormats.Bgra32);
            var result = ScreenshotComparison.Compare(Path.Combine(directory, "actual.png"), Path.Combine(directory, "expected.png"));
            Assert.Equal(changedPixels, result.PixelErrorCount);
            Assert.Equal(percentage, result.PixelErrorPercentage);
            Assert.Equal(accepted, result.PixelErrorPercentage < ScreenshotBaselineTests.PixelErrorTolerancePercent);
        });
    }

    [Fact]
    public void Compare_AlphaAndPixelFormats_IgnoresAlphaWithoutBlendingRgb()
    {
        WithDirectory(directory =>
        {
            WritePng(Path.Combine(directory, "rgb.png"), 1, 1, [10, 20, 30], PixelFormats.Bgr24);
            WritePng(Path.Combine(directory, "rgba.png"), 1, 1, [10, 20, 30, 0], PixelFormats.Bgra32);
            var result = ScreenshotComparison.Compare(Path.Combine(directory, "rgba.png"), Path.Combine(directory, "rgb.png"));
            Assert.Equal(0, result.PixelErrorCount);
            Assert.Equal(0.0, result.PixelErrorPercentage);
        });
    }

    [Fact]
    public void Compare_DifferentDimensions_RejectsInsteadOfResizing()
    {
        WithDirectory(directory =>
        {
            WritePng(Path.Combine(directory, "wide.png"), 2, 1, new byte[8], PixelFormats.Bgra32);
            WritePng(Path.Combine(directory, "narrow.png"), 1, 1, new byte[4], PixelFormats.Bgra32);
            Assert.Throws<InvalidOperationException>(() => ScreenshotComparison.Compare(Path.Combine(directory, "wide.png"), Path.Combine(directory, "narrow.png")));
        });
    }

    [Fact]
    public void Compare_ColorDifference_ProducesReopenablePerChannelPngMask()
    {
        WithDirectory(directory =>
        {
            WritePng(Path.Combine(directory, "expected.png"), 1, 1, [10, 20, 30, 255], PixelFormats.Bgra32);
            WritePng(Path.Combine(directory, "actual.png"), 1, 1, [40, 10, 35, 255], PixelFormats.Bgra32);
            var result = ScreenshotComparison.Compare(Path.Combine(directory, "actual.png"), Path.Combine(directory, "expected.png"));
            Assert.Equal(1, result.PixelErrorCount);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result.DiffMask));
            using var output = new MemoryStream();
            encoder.Save(output);
            output.Position = 0;
            var mask = BitmapDecoder.Create(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var pixels = new byte[3];
            mask.CopyPixels(pixels, 3, 0);
            Assert.Equal(new byte[] { 30, 10, 5 }, pixels);
        });
    }

    internal static void WritePng(string path, int width, int height, byte[] pixels, PixelFormat format)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * format.BitsPerPixel / 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal static void WithDirectory(Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory("tracker-pixel-comparison-");
        try
        {
            action(directory.FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
