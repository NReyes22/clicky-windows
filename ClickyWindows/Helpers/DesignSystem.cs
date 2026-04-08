using System.Windows.Media;

namespace ClickyWindows.Helpers;

/// <summary>
/// Centralized design system tokens ported from the macOS DesignSystem.swift.
/// All UI references DS.Colors.*, DS.CornerRadius, etc.
/// </summary>
public static class DS
{
    public static class Colors
    {
        // ── Backgrounds ─────────────────────────────────────────
        public static readonly Color Background = ColorFromHex("#101211");
        public static readonly Color Surface1 = ColorFromHex("#171918");
        public static readonly Color Surface2 = ColorFromHex("#202221");
        public static readonly Color Surface3 = ColorFromHex("#272A29");
        public static readonly Color Surface4 = ColorFromHex("#2E3130");

        // ── Borders ─────────────────────────────────────────────
        public static readonly Color BorderSubtle = ColorFromHex("#373B39");
        public static readonly Color BorderStrong = ColorFromHex("#444947");

        // ── Text ────────────────────────────────────────────────
        public static readonly Color TextPrimary = ColorFromHex("#ECEEED");
        public static readonly Color TextSecondary = ColorFromHex("#ADB5B2");
        public static readonly Color TextTertiary = ColorFromHex("#6B736F");
        public static readonly Color TextOnAccent = System.Windows.Media.Colors.White;

        // ── Blue Accent Scale ───────────────────────────────────
        public static readonly Color Blue50 = ColorFromHex("#EFF6FF");
        public static readonly Color Blue100 = ColorFromHex("#DBEAFE");
        public static readonly Color Blue200 = ColorFromHex("#BFDBFE");
        public static readonly Color Blue300 = ColorFromHex("#93C5FD");
        public static readonly Color Blue400 = ColorFromHex("#60A5FA");
        public static readonly Color Blue500 = ColorFromHex("#3B82F6");
        public static readonly Color Blue600 = ColorFromHex("#2563EB");
        public static readonly Color Blue700 = ColorFromHex("#1D4ED8");
        public static readonly Color Blue800 = ColorFromHex("#1E40AF");
        public static readonly Color Blue900 = ColorFromHex("#1E3A8A");

        // ── Convenience Brushes ─────────────────────────────────
        public static readonly SolidColorBrush BackgroundBrush = new(Background);
        public static readonly SolidColorBrush Surface1Brush = new(Surface1);
        public static readonly SolidColorBrush Surface2Brush = new(Surface2);
        public static readonly SolidColorBrush Surface3Brush = new(Surface3);
        public static readonly SolidColorBrush TextPrimaryBrush = new(TextPrimary);
        public static readonly SolidColorBrush TextSecondaryBrush = new(TextSecondary);
        public static readonly SolidColorBrush TextTertiaryBrush = new(TextTertiary);
        public static readonly SolidColorBrush BorderSubtleBrush = new(BorderSubtle);
        public static readonly SolidColorBrush Blue500Brush = new(Blue500);
        public static readonly SolidColorBrush Blue600Brush = new(Blue600);

        /// The main overlay cursor blue color and brush.
        public static readonly Color OverlayCursorBlue = Blue500;
        public static readonly SolidColorBrush OverlayCursorBlueBrush = Blue500Brush;

        private static Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            return Color.FromRgb(r, g, b);
        }
    }

    public static class CornerRadius
    {
        public static readonly System.Windows.CornerRadius Small = new(4);
        public static readonly System.Windows.CornerRadius Medium = new(8);
        public static readonly System.Windows.CornerRadius Large = new(12);
        public static readonly System.Windows.CornerRadius XLarge = new(16);
    }
}
