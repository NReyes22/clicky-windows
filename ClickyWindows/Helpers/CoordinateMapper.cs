using System.Windows;
using ClickyWindows.Models;

namespace ClickyWindows.Helpers;

/// <summary>
/// Maps coordinates from Claude's screenshot pixel space to actual display pixels.
/// Simpler than the macOS version because Windows uses top-left origin everywhere
/// (no Y-axis flip needed between Core Graphics and AppKit coordinate systems).
/// </summary>
public static class CoordinateMapper
{
    /// <summary>
    /// Converts a point from screenshot pixel coordinates to global screen coordinates.
    /// </summary>
    /// <param name="screenshotCoordinate">The x,y from Claude's [POINT:x,y:...] tag</param>
    /// <param name="capture">The screen capture metadata for the target screen</param>
    /// <returns>The global screen coordinate (physical pixels)</returns>
    public static Point MapScreenshotToDisplay(Point screenshotCoordinate, CompanionScreenCapture capture)
    {
        // Clamp to screenshot bounds
        var clampedX = Math.Max(0, Math.Min(screenshotCoordinate.X, capture.ScreenshotWidthInPixels));
        var clampedY = Math.Max(0, Math.Min(screenshotCoordinate.Y, capture.ScreenshotHeightInPixels));

        // Scale from screenshot pixels to display pixels
        var scaleX = (double)capture.DisplayWidthInPixels / capture.ScreenshotWidthInPixels;
        var scaleY = (double)capture.DisplayHeightInPixels / capture.ScreenshotHeightInPixels;

        var displayLocalX = clampedX * scaleX;
        var displayLocalY = clampedY * scaleY;

        // Add the display's global offset to get absolute screen coordinates
        return new Point(
            displayLocalX + capture.DisplayBounds.Left,
            displayLocalY + capture.DisplayBounds.Top);
    }
}
