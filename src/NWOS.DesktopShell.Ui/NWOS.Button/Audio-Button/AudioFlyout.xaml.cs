using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NWOS.DesktopShell.Ui.Button
{
    public partial class AudioFlyout : UserControl
    {
        public AudioFlyout()
        {
            InitializeComponent();
        }

        private void AudioContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            AudioPopup.IsOpen = true;
        }

        private void AudioContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            AudioPopup.IsOpen = false;
        }

        private void ToggleMic_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Mikrofon-Status umgeschaltet.", "NWOS Audio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleSpeaker_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Lautsprecher-Status umgeschaltet.", "NWOS Audio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleCam_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Kamera-Status umgeschaltet.", "NWOS Audio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}