using System.Windows;

namespace ClickyWindows.Models;

public record PointingParseResult(
    string SpokenText,
    Point? Coordinate,
    string? ElementLabel,
    int? ScreenNumber
);
