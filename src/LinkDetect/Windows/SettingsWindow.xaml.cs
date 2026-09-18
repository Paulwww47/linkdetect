using System.Windows;
using LinkDetect.Services;

namespace LinkDetect.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly Action<double> _onSave;

    public SettingsWindow(SettingsService settingsService, Action<double> onSave)
    {
        _settingsService = settingsService;
        _onSave = onSave;

        InitializeComponent();
        DefaultThresholdText.Text = $"推荐值 {BackgroundThreshold.DefaultPercent:0}%";
        ThresholdSlider.Value = BackgroundThreshold.Normalize(
            settingsService.Current.BackgroundCropThresholdPercent);
        UpdateValueText();
    }

    private void ThresholdSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThresholdSlider is not null && ThresholdValueText is not null && ResetThresholdButton is not null)
        {
            UpdateValueText();
        }
    }

    private void UpdateValueText()
    {
        ThresholdValueText.Text = $"{ThresholdSlider.Value:0}%";
        ResetThresholdButton.IsEnabled = ThresholdSlider.Value != BackgroundThreshold.DefaultPercent;
    }

    private void ResetThresholdButton_OnClick(object sender, RoutedEventArgs e)
        => ThresholdSlider.Value = BackgroundThreshold.DefaultPercent;

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settingsService.SaveThreshold(ThresholdSlider.Value);
        _onSave(ThresholdSlider.Value);
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
