namespace ClickyWindows.Models;

public class CompanionScreenCapture
{
    public required byte[] ImageData { get; init; }
    public required string Label { get; init; }
    public required bool IsCursorScreen { get; init; }
    public required int DisplayWidthInPixels { get; init; }
    public required int DisplayHeightInPixels { get; init; }
    public required System.Drawing.Rectangle DisplayBounds { get; init; }
    public required int ScreenshotWidthInPixels { get; init; }
    public required int ScreenshotHeightInPixels { get; init; }
}
