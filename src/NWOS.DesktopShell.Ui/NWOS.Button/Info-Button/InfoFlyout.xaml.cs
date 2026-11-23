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
                    RefreshNotifications();
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
                    if (FindName("NotificationListPanel") is not Panel notificationPanel)
                        return;

                    notificationPanel.Children.Clear();

                    var emptyText = new TextBlock
                    {
                        Text = "Keine neuen Benachrichtigungen",
                        Foreground = Brushes.Gray,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 10, 0, 10)
                    };
                    notificationPanel.Children.Add(emptyText);
                }

        public void RefreshNotifications()
                {
                    if (FindName("NotificationListPanel") is not Panel panel)
                        return;

                    panel.Children.Clear();
                    panel.Children.Add(new TextBlock
                    {
                        Text = "• Cardinal Kernel: IPC-Pipe aktiv",
                        Foreground = System.Windows.Media.Brushes.LightGray,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                    panel.Children.Add(new TextBlock
                    {
                        Text = "• Systemstatus: Alle Dienste bereit",
                        Foreground = System.Windows.Media.Brushes.LightGray,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
        }
    }