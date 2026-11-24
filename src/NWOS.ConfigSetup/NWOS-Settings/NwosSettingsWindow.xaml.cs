using System.IO;
using System.Windows;
using NWOS.ConfigSetup;

namespace NWOS.DesktopShell.Ui;

public partial class NwosSettingsWindow : Window
{
    private readonly ConfigStore _configStore;
    private NwosConfiguration? _configuration;

    public NwosSettingsWindow()
    {
        InitializeComponent();

        var configDirectory = Environment.GetEnvironmentVariable("NWOS_CONFIG_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NWOS");
        _configStore = new ConfigStore(configDirectory);
        Loaded += LoadConfigurationAsync;
    }

    private async void LoadConfigurationAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _configuration = await _configStore.LoadAsync(CancellationToken.None);
            DesktopShellCheckBox.IsChecked = _configuration.EnableDesktopShell;
            WidgetsCheckBox.IsChecked = _configuration.EnableWidgets;
            AliceCheckBox.IsChecked = _configuration.EnableAlice;
            MarcCheckBox.IsChecked = _configuration.EnableMarc;
            AlyaCheckBox.IsChecked = _configuration.EnableAlya;
            SystemCenterCheckBox.IsChecked = _configuration.EnableSystemCenter;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"NWOS-Konfiguration konnte nicht geladen werden.\n\n{ex.Message}",
                "NWOS-Einstellungen", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_configuration is null)
        {
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            _configuration.EnableDesktopShell = DesktopShellCheckBox.IsChecked == true;
            _configuration.EnableWidgets = WidgetsCheckBox.IsChecked == true;
            _configuration.EnableAlice = AliceCheckBox.IsChecked == true;
            _configuration.EnableMarc = MarcCheckBox.IsChecked == true;
            _configuration.EnableAlya = AlyaCheckBox.IsChecked == true;
            _configuration.EnableSystemCenter = SystemCenterCheckBox.IsChecked == true;
            await _configStore.SaveAsync(_configuration, CancellationToken.None);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"NWOS-Konfiguration konnte nicht gespeichert werden.\n\n{ex.Message}",
                "NWOS-Einstellungen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
