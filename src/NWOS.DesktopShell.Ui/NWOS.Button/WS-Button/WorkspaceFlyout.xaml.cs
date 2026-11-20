using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NWOS.DesktopShell.Ui.Button
{
    public partial class WorkspaceFlyout : UserControl
    {
        public WorkspaceFlyout()
        {
            InitializeComponent();
        }

        private void WorkspaceContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            WorkspacePopup.IsOpen = true;
        }

        private void WorkspaceContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            WorkspacePopup.IsOpen = false;
        }

        private void Desktop1_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Bereits auf Desktop 1 (Hier) aktiv.", "NWOS Workspaces", MessageBoxButton.OK, MessageBoxImage.Information);
            WorkspacePopup.IsOpen = false;
        }

        private void AddDesktop_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Neuer virtueller Desktop wird erstellt...", "NWOS Workspaces", MessageBoxButton.OK, MessageBoxImage.Information);
            WorkspacePopup.IsOpen = false;
        }
    }
}