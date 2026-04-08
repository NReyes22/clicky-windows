using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyWindows.Helpers;
using ClickyWindows.Services;
using ClickyWindows.ViewModels;
using ClickyWindows.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace ClickyWindows;

public partial class App : Application
{
    private TaskbarIcon? taskbarIcon;
    private CompanionManagerViewModel? companionManager;
    private TrayPanelWindow? trayPanelWindow;
    private readonly List<OverlayWindow> overlayWindows = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log debug output to a file so we can diagnose issues
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clicky-debug.log");
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(logPath));
        System.Diagnostics.Trace.AutoFlush = true;
        System.Diagnostics.Debug.WriteLine("[Clicky] App starting");

        var config = LoadConfiguration();
        System.Diagnostics.Debug.WriteLine($"[Clicky] Worker URL: {config.WorkerBaseUrl}");

        companionManager = new CompanionManagerViewModel(
            config.WorkerBaseUrl,
            config.DefaultModel);

        trayPanelWindow = new TrayPanelWindow(companionManager);

        CreateOverlayWindows();

        SetupTaskbarIcon();

        companionManager.Start();
    }

    private AppConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
        }
        return new AppConfiguration();
    }

    private void SetupTaskbarIcon()
    {
        taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Clicky",
            Icon = CreateTrayIcon()
        };

        taskbarIcon.TrayLeftMouseUp += (_, _) => ToggleTrayPanel();
    }

    /// <summary>
    /// Creates a small blue triangle icon for the system tray.
    /// </summary>
    private static System.Drawing.Icon CreateTrayIcon()
    {
        const int iconSize = 32;
        using var bitmap = new System.Drawing.Bitmap(iconSize, iconSize);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);

        // Draw a blue equilateral triangle rotated ~35 degrees, matching the macOS icon
        var centerX = iconSize / 2f;
        var centerY = iconSize / 2f;
        var triangleSize = iconSize * 0.65f;
        var rotationRadians = -35.0 * Math.PI / 180.0;

        // Equilateral triangle vertices (pointing right, then rotated)
        var vertices = new System.Drawing.PointF[3];
        for (int i = 0; i < 3; i++)
        {
            var angle = rotationRadians + (i * 2.0 * Math.PI / 3.0) - Math.PI / 2.0;
            vertices[i] = new System.Drawing.PointF(
                centerX + (float)(triangleSize / 2.0 * Math.Cos(angle)),
                centerY + (float)(triangleSize / 2.0 * Math.Sin(angle)));
        }

        using var brush = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(59, 130, 246)); // Blue-500
        graphics.FillPolygon(brush, vertices);

        var handle = bitmap.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }

    private void ToggleTrayPanel()
    {
        if (trayPanelWindow == null) return;

        if (trayPanelWindow.IsVisible)
        {
            trayPanelWindow.Hide();
        }
        else
        {
            PositionTrayPanelAboveTaskbar();
            trayPanelWindow.Show();
        }
    }

    private void PositionTrayPanelAboveTaskbar()
    {
        if (trayPanelWindow == null) return;

        // Position the panel above the system tray area (bottom-right of primary screen)
        var workArea = SystemParameters.WorkArea;
        var panelWidth = trayPanelWindow.Width;
        var panelHeight = trayPanelWindow.Height;

        trayPanelWindow.Left = workArea.Right - panelWidth - 8;
        trayPanelWindow.Top = workArea.Bottom - panelHeight - 8;
    }

    private void CreateOverlayWindows()
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var overlayWindow = new OverlayWindow(companionManager!, screen);
            overlayWindow.Show();
            overlayWindows.Add(overlayWindow);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        companionManager?.Stop();
        taskbarIcon?.Dispose();

        foreach (var overlayWindow in overlayWindows)
        {
            overlayWindow.Close();
        }

        base.OnExit(e);
    }
}

internal class AppConfiguration
{
    public string WorkerBaseUrl { get; set; } = "https://your-worker.your-subdomain.workers.dev";
    public string DefaultModel { get; set; } = "claude-sonnet-4-6";
}
