using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ClickyWindows.Models;

namespace ClickyWindows.Services;

/// <summary>
/// Captures screenshots of all connected monitors using GDI+.
/// Port of CompanionScreenCaptureUtility.swift — captures, scales to max 1280px,
/// JPEG-encodes, labels screens, and sorts cursor screen first.
/// </summary>
public class ScreenCaptureService
{
    private const int MaxDimension = 1280;
    private const long JpegQuality = 80;

    /// <summary>
    /// Captures all connected screens and returns labeled screenshot data.
    /// The cursor screen is always first in the returned list.
    /// </summary>
    public List<CompanionScreenCapture> CaptureAllScreens()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var cursorPosition = System.Windows.Forms.Cursor.Position;
        var captures = new List<CompanionScreenCapture>();
        int totalScreens = screens.Length;

        // Determine which screen the cursor is on
        var cursorScreenIndex = 0;
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i].Bounds.Contains(cursorPosition))
            {
                cursorScreenIndex = i;
                break;
            }
        }

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var bounds = screen.Bounds;
            bool isCursorScreen = (i == cursorScreenIndex);

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }

            // Scale to max 1280px while preserving aspect ratio
            var (scaledWidth, scaledHeight) = ComputeScaledDimensions(bounds.Width, bounds.Height);

            using var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaledBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(bitmap, 0, 0, scaledWidth, scaledHeight);
            }

            // Encode as JPEG
            var jpegData = EncodeAsJpeg(scaledBitmap);

            // Build label matching macOS format
            var screenNumber = i + 1;
            var label = totalScreens == 1
                ? $"screen {screenNumber} of {totalScreens} ({scaledWidth}x{scaledHeight} pixels)"
                : isCursorScreen
                    ? $"screen {screenNumber} of {totalScreens} \u2014 cursor is on this screen ({scaledWidth}x{scaledHeight} pixels)"
                    : $"screen {screenNumber} of {totalScreens} ({scaledWidth}x{scaledHeight} pixels)";

            captures.Add(new CompanionScreenCapture
            {
                ImageData = jpegData,
                Label = label,
                IsCursorScreen = isCursorScreen,
                DisplayWidthInPixels = bounds.Width,
                DisplayHeightInPixels = bounds.Height,
                DisplayBounds = bounds,
                ScreenshotWidthInPixels = scaledWidth,
                ScreenshotHeightInPixels = scaledHeight
            });
        }

        // Sort so cursor screen is first
        captures.Sort((a, b) => b.IsCursorScreen.CompareTo(a.IsCursorScreen));

        return captures;
    }

    private static (int width, int height) ComputeScaledDimensions(int originalWidth, int originalHeight)
    {
        if (originalWidth <= MaxDimension && originalHeight <= MaxDimension)
        {
            return (originalWidth, originalHeight);
        }

        double aspectRatio = (double)originalWidth / originalHeight;

        if (originalWidth >= originalHeight)
        {
            return (MaxDimension, (int)(MaxDimension / aspectRatio));
        }
        else
        {
            return ((int)(MaxDimension * aspectRatio), MaxDimension);
        }
    }

    private static byte[] EncodeAsJpeg(Bitmap bitmap)
    {
        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(e => e.FormatID == ImageFormat.Jpeg.Guid);

        var encoderParams = new EncoderParameters(1)
        {
            Param = { [0] = new EncoderParameter(Encoder.Quality, JpegQuality) }
        };

        using var stream = new MemoryStream();
        bitmap.Save(stream, jpegEncoder, encoderParams);
        return stream.ToArray();
    }
}
