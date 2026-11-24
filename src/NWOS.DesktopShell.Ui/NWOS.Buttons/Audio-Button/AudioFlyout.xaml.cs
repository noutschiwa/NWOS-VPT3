using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NWOS.DesktopShell.Ui.Button
{
    public partial class AudioFlyout : UserControl
    {
        private bool _isSyncing;

        public AudioFlyout()
        {
            InitializeComponent();
            SyncFromWindows();
        }

        private void AudioPopup_Opened(object? sender, EventArgs e)
        {
            SyncFromWindows();
            LoadAppAudioSessions();
        }

        private void LoadAppAudioSessions()
        {
            AppAudioListPanel.Children.Clear();
            var sessions = SystemAudioHelper.GetActiveAppSessions();

            if (sessions.Count == 0)
            {
                AppAudioListPanel.Children.Add(new TextBlock
                {
                    Text = "Keine aktiven App-Audio-Streams",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
                    FontSize = 11,
                    Margin = new Thickness(2, 4, 2, 4)
                });
                return;
            }

            foreach (var session in sessions)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262932")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var stack = new StackPanel();

                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleBlock = new TextBlock
                {
                    Text = session.DisplayName,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6")),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(titleBlock, 0);

                var percentBlock = new TextBlock
                {
                    Text = $"{(int)Math.Round(session.VolumePercent)}%",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
                    FontSize = 11,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(percentBlock, 1);

                headerGrid.Children.Add(titleBlock);
                headerGrid.Children.Add(percentBlock);

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = session.VolumePercent,
                    Margin = new Thickness(0, 4, 0, 0),
                    IsMoveToPointEnabled = true
                };

                uint pid = session.ProcessId;
                slider.ValueChanged += (_, args) =>
                {
                    float val = (float)args.NewValue;
                    percentBlock.Text = $"{(int)Math.Round(val)}%";
                    SystemAudioHelper.SetSessionVolume(pid, val);
                };

                stack.Children.Add(headerGrid);
                stack.Children.Add(slider);
                card.Child = stack;

                AppAudioListPanel.Children.Add(card);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncing) return;

            float newVolume = (float)e.NewValue;
            SystemAudioHelper.SetMasterVolume(newVolume);

            // Falls der Regler hochgezogen wird, Stummschaltung automatisch aufheben
            bool isMuted = SystemAudioHelper.GetMute();
            if (isMuted && newVolume > 0)
            {
                SystemAudioHelper.SetMute(false);
                isMuted = false;
            }

            UpdateVisualIndicators((int)Math.Round(newVolume), isMuted);
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            bool newMuteState = !SystemAudioHelper.GetMute();
            SystemAudioHelper.SetMute(newMuteState);

            int currentPercent = (int)Math.Round(VolumeSlider.Value);
            UpdateVisualIndicators(currentPercent, newMuteState);
        }

        private void UpdateVisualIndicators(int percent, bool isMuted)
        {
            VolumePercentText.Text = $"{percent}%";

            string icon = (isMuted || percent == 0) ? "🔇"
                        : percent < 40 ? "🔉"
                        : "🔊";

            MuteButton.Content = icon;
            AudioToggleButton.Content = icon;
        }

        private bool _micMuted;
                private bool _camMuted;

                public void SyncFromWindows()
                {
                    _isSyncing = true;
                    try
                    {
                        float masterVol = SystemAudioHelper.GetMasterVolume();
                        bool isMuted = SystemAudioHelper.GetMute();

                        VolumeSlider.Value = masterVol;
                        UpdateVisualIndicators((int)Math.Round(masterVol), isMuted);
                    }
                    finally
                    {
                        _isSyncing = false;
                    }
                }

                private void ToggleMic_Click(object sender, RoutedEventArgs e)
                {
                    _micMuted = !_micMuted;
                    if (FindName("MicToggleButton") is System.Windows.Controls.Primitives.ToggleButton toggle)
                    {
                        toggle.IsChecked = _micMuted;
                    }
                    if (FindName("MicStatusText") is TextBlock micStatus)
                    {
                        micStatus.Text = _micMuted ? "Aus" : "An";
                    }
                }

                private void ToggleSpeaker_Click(object sender, RoutedEventArgs e)
                {
                    MuteButton_Click(sender, e);
                }

                private void ToggleCam_Click(object sender, RoutedEventArgs e)
                {
                    _camMuted = !_camMuted;
                    if (FindName("CamToggleButton") is System.Windows.Controls.Primitives.ToggleButton toggle)
                    {
                        toggle.IsChecked = _camMuted;
                    }
                    if (FindName("CamStatusText") is TextBlock camStatus)
                    {
                        camStatus.Text = _camMuted ? "Aus" : "An";
                    }
                }
    }
}