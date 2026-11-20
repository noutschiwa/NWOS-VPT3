using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NWOS.DesktopShell.Ui.Button
{
    public partial class InfoFlyout : UserControl
    {
        public InfoFlyout()
        {
            InitializeComponent();
        }

        private void InfoContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            InfoPopup.IsOpen = true;
        }

        private void InfoContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            InfoPopup.IsOpen = false;
        }

        private void ClearNotifications_Click(object sender, RoutedEventArgs e)
        {
            NotificationListPanel.Children.Clear();

            var emptyText = new TextBlock
            {
                Text = "Keine neuen Benachrichtigungen",
                Foreground = Brushes.Gray,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            NotificationListPanel.Children.Add(emptyText);
        }
    }
}