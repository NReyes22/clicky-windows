using System.Windows;
using System.Windows.Controls;
using ClickyWindows.ViewModels;

namespace ClickyWindows.Views;

public partial class TrayPanelWindow : Window
{
    private readonly CompanionManagerViewModel companionManager;
    private bool isInitialized;

    public TrayPanelWindow(CompanionManagerViewModel companionManager)
    {
        this.companionManager = companionManager;
        InitializeComponent();
        isInitialized = true;

        // Auto-hide when the window loses focus
        Deactivated += (_, _) =>
        {
            // Small delay to avoid dismissing when interacting with the panel's own controls
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!IsActive)
                {
                    Hide();
                }
            };
            timer.Start();
        };

        // Update status text when voice state changes
        companionManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CompanionManagerViewModel.VoiceState))
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = companionManager.VoiceState switch
                    {
                        Models.CompanionVoiceState.Idle => "ready \u2014 hold ctrl+alt to talk",
                        Models.CompanionVoiceState.Listening => "listening...",
                        Models.CompanionVoiceState.Processing => "thinking...",
                        Models.CompanionVoiceState.Responding => "speaking...",
                        _ => "ready"
                    };
                });
            }
        };

        // Set initial model selection
        for (int i = 0; i < ModelPicker.Items.Count; i++)
        {
            if (ModelPicker.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == companionManager.SelectedModel)
            {
                ModelPicker.SelectedIndex = i;
                break;
            }
        }
    }

    private void ModelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard against event firing during XAML initialization before constructor completes
        if (!isInitialized) return;

        if (ModelPicker.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string modelId)
        {
            companionManager.SelectedModel = modelId;
        }
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
