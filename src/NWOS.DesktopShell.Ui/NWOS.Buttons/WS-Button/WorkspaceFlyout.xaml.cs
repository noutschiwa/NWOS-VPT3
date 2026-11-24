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

        private void PreviousDesktop_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDesktopAction(VirtualDesktopController.SwitchToPreviousDesktop, "Zum vorherigen Desktop gewechselt.");
        }

        private void AddDesktop_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDesktopAction(VirtualDesktopController.CreateDesktop, "Neuer virtueller Desktop erstellt.");
        }

        private void NextDesktop_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDesktopAction(VirtualDesktopController.SwitchToNextDesktop, "Zum nächsten Desktop gewechselt.");
        }

        private void CloseCurrentDesktop_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDesktopAction(VirtualDesktopController.CloseCurrentDesktop,
                "Aktueller virtueller Desktop geschlossen; geöffnete Fenster wurden von Windows verschoben.");
        }

        private void ExecuteDesktopAction(Action action, string successMessage)
        {
            try
            {
                action();
                WorkspaceStatusTextBlock.Text = successMessage;
            }
            catch (Exception ex)
            {
                WorkspaceStatusTextBlock.Text = $"Aktion nicht verfügbar: {ex.Message}";
            }
        }
    }
}
