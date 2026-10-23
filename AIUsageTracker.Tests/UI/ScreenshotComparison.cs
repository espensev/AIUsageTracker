// <copyright file="ScreenshotComparison.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageTracker.Tests.UI;

internal static class ScreenshotComparison
{
    internal static (int PixelErrorCount, double PixelErrorPercentage, BitmapSource DiffMask) Compare(string actualPath, string expectedPath)
    {
        var actual = ReadPng(actualPath);
        var expected = ReadPng(expectedPath);
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            throw new InvalidOperationException("Size of images differ.");
        }

        var quantity = checked(actual.Width * actual.Height);
        var difference = new byte[checked(quantity * 3)];
        var pixelErrors = 0;
        for (var pixel = 0; pixel < quantity; pixel++)
        {
            var differs = false;
            for (var channel = 0; channel < 3; channel++)
            {
                var delta = (byte)Math.Abs(actual.Pixels[(pixel * 4) + channel] - expected.Pixels[(pixel * 4) + channel]);
                difference[(pixel * 3) + channel] = delta;
                differs |= delta != 0;
            }

            pixelErrors += differs ? 1 : 0;
        }

        var mask = BitmapSource.Create(actual.Width, actual.Height, 96, 96, PixelFormats.Bgr24, null, difference, checked(actual.Width * 3));
        mask.Freeze();
        return (pixelErrors, (double)pixelErrors / quantity * 100, mask);
    }

    private static (int Width, int Height, byte[] Pixels) ReadPng(string path)
    {
        using var input = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

        // Preserve the previous comparison contract: exact RGB differences, with
        // alpha ignored rather than composited and no resizing or color correction.
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        return (bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
