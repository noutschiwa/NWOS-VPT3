using System;
using System.Windows;
using System.Windows.Controls;
using NWOS.Contracts;

namespace NWOS.DesktopShell.Ui.Button;

public partial class AlyaButton : UserControl
{
    private CardinalClient? _cardinalClient;

    public AlyaButton()
    {
        InitializeComponent();
    }

    public void Configure(CardinalClient cardinalClient)
    {
        _cardinalClient = cardinalClient ?? throw new ArgumentNullException(nameof(cardinalClient));
    }

    private async void AlyaAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);

        if (_cardinalClient is null)
        {
            MessageBox.Show(
                owner,
                "Der Cardinal-Dienst ist für Alya nicht konfiguriert.",
                "NWOS Alya",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var response = await _cardinalClient.CallAsync(
                "nwos.alya",
                "chat.status",
                new { },
                PermissionScope.FileSystemRead);

            var message = response?.RootElement.ToString() ?? "Alya Kern bereit.";
            MessageBox.Show(
                owner,
                $"Alya Antwort:\n{message}",
                "NWOS Alya",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                $"Alya konnte nicht erreicht werden: {ex.Message}",
                "NWOS Alya",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}