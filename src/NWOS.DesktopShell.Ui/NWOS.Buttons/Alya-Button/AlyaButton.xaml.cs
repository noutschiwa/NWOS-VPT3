using System;
using System.Windows;
using System.Windows.Controls;

namespace NWOS.DesktopShell.Ui.Button;

public partial class AlyaButton : UserControl
{
    private CardinalClient? _cardinalClient;
    private AlyaChatWindow? _chatWindow;

    public AlyaButton()
    {
        InitializeComponent();
    }

    public void Configure(CardinalClient cardinalClient)
    {
        _cardinalClient = cardinalClient ?? throw new ArgumentNullException(nameof(cardinalClient));
    }

    private void CaiAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cardinalClient is null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Der Cardinal-Dienst ist für den KI-Assistenten nicht konfiguriert.",
                "NWOS CAI",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_chatWindow is null)
        {
            _chatWindow = new AlyaChatWindow(_cardinalClient);
            _chatWindow.Closed += (_, _) => _chatWindow = null;
        }

        if (_chatWindow.WindowState == WindowState.Minimized)
        {
            _chatWindow.WindowState = WindowState.Normal;
        }

        _chatWindow.Show();
        _chatWindow.Activate();
    }
}