using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NWOS.DesktopShell.Ui.Button
{
    public partial class MasterFlyout : UserControl
    {
        public MasterFlyout()
        {
            InitializeComponent();
        }

        private void MasterContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            MasterPopup.IsOpen = true;
        }

        private void MasterContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            MasterPopup.IsOpen = false;
        }

        private void PcSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PC-Einstellungen konnten nicht geöffnet werden: {ex.Message}", "NWOS Master", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            MasterPopup.IsOpen = false;
        }

        private void NwosSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("NWOS Settings-Modul wird geöffnet...", "NWOS Master", MessageBoxButton.OK, MessageBoxImage.Information);
            MasterPopup.IsOpen = false;
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Möchtest du das System wirklich neu starten?", "NWOS Master - Neustart", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("shutdown.exe", "/r /t 0");
            }
            MasterPopup.IsOpen = false;
        }

        private void Shutdown_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Möchtest du NWOS und das System herunterfahren?", "NWOS Master - Herunterfahren", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("shutdown.exe", "/s /t 0");
            }
            MasterPopup.IsOpen = false;
        }
    }
}