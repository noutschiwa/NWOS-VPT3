using System.IO;
using System.Windows;
using System.Windows.Controls;
using NWOS.ConfigSetup;

namespace NWOS.DesktopShell.Ui;

public partial class NwosSettingsPanel : UserControl
{
    private readonly ConfigStore _configStore;
    private NwosConfiguration? _configuration;
    private bool _configurationLoaded;

    public event Action<NwosConfiguration>? ConfigurationChanged;
    public event EventHandler? BackRequested;

    public NwosSettingsPanel()
    {
        InitializeComponent();

        var configDirectory = Environment.GetEnvironmentVariable("NWOS_CONFIG_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NWOS");
        _configStore = new ConfigStore(configDirectory);
        ShowCategory("General");
    }

    public async Task LoadConfigurationAsync(bool forceReload = false)
    {
        if (_configurationLoaded && !forceReload)
        {
            return;
        }

        _configuration = null;
        _configurationLoaded = false;
        try
        {
            _configuration = await _configStore.LoadAsync(CancellationToken.None);
            DesktopShellCheckBox.IsChecked = _configuration.EnableDesktopShell;
            WidgetsCheckBox.IsChecked = _configuration.EnableWidgets;
            AliceCheckBox.IsChecked = _configuration.EnableAlice;
            MarcCheckBox.IsChecked = _configuration.EnableMarc;
            AlyaCheckBox.IsChecked = _configuration.EnableAlya;
            SystemCenterCheckBox.IsChecked = _configuration.EnableSystemCenter;
            AccentComboBox.SelectedValue = _configuration.MasterFlyoutAccent;
            FlyoutBackgroundCheckBox.IsChecked = _configuration.ShowMasterFlyoutBackground;
            _configurationLoaded = true;
            ConfigurationChanged?.Invoke(_configuration);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"NWOS-Konfiguration konnte nicht geladen werden.\n\n{ex.Message}",
                "NWOS-Einstellungen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void GeneralCategory_Click(object sender, RoutedEventArgs e) => ShowCategory("General");

    private void DesignCategory_Click(object sender, RoutedEventArgs e) => ShowCategory("Design");

    private void ShowCategory(string category)
    {
        var isGeneral = category == "General";
        GeneralSettingsPage.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        DesignSettingsPage.Visibility = isGeneral ? Visibility.Collapsed : Visibility.Visible;
        GeneralCategoryButton.Background = isGeneral
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 74, 105))
            : null;
        DesignCategoryButton.Background = isGeneral
            ? null
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 74, 105));
        SaveStatusText.Text = string.Empty;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_configuration is null)
        {
            await LoadConfigurationAsync();
            if (_configuration is null)
            {
                return;
            }
        }

        SaveButton.IsEnabled = false;
        SaveStatusText.Text = string.Empty;
        try
        {
            _configuration.EnableDesktopShell = DesktopShellCheckBox.IsChecked == true;
            _configuration.EnableWidgets = WidgetsCheckBox.IsChecked == true;
            _configuration.EnableAlice = AliceCheckBox.IsChecked == true;
            _configuration.EnableMarc = MarcCheckBox.IsChecked == true;
            _configuration.EnableAlya = AlyaCheckBox.IsChecked == true;
            _configuration.EnableSystemCenter = SystemCenterCheckBox.IsChecked == true;
            _configuration.MasterFlyoutAccent = AccentComboBox.SelectedValue as string ?? "Blue";
            _configuration.ShowMasterFlyoutBackground = FlyoutBackgroundCheckBox.IsChecked == true;
            await _configStore.SaveAsync(_configuration, CancellationToken.None);
            ConfigurationChanged?.Invoke(_configuration);
            SaveStatusText.Text = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"NWOS-Konfiguration konnte nicht gespeichert werden.\n\n{ex.Message}",
                "NWOS-Einstellungen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
