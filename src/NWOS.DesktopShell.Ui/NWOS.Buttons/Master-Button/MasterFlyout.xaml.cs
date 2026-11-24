using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NWOS.DesktopShell.Ui.Button;

public enum AllowedConsoleAction
{
    None,
    AlyaStatus,
    AliceEvents,
    AliceScan,
    HideConsole
}

public partial class MasterFlyout : UserControl
{
    private const string AdminAccessCode = "135798642";
    private const double MenuWidth = 560;
    private const double MenuHeightLimit = 760;
    private const double ScreenMargin = 12;

    private readonly ObservableCollection<ConsoleEntry> _consoleMessages = new();
    private bool _consoleInitialized;
    private string? _activeChatTarget;

    public event Action<string>? SearchRequested;
    public Func<AllowedConsoleAction, Task>? ConsoleActionRequested { get; set; }
    public Func<string, bool>? ConsoleTextCommandRequested { get; set; }
    public Func<string, string, Task<string>>? ChatMessageRequested { get; set; }

    private sealed record ConsoleEntry(string Sender, string Message);

    public MasterFlyout()
    {
        InitializeComponent();
        CardinalConsoleMessagesControl.ItemsSource = _consoleMessages;
    }

    private void MasterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (MasterToggleButton.IsChecked == true)
        {
            PreparePopup();
            MasterPopup.IsOpen = true;
        }
        else
        {
            MasterPopup.IsOpen = false;
        }
    }

    public void ToggleFromSystemKey()
    {
        if (MasterPopup.IsOpen)
        {
            MasterPopup.IsOpen = false;
            MasterToggleButton.IsChecked = false;
            return;
        }

        PreparePopup();
        MasterToggleButton.IsChecked = true;
        MasterPopup.IsOpen = true;
    }

    private void PreparePopup()
    {
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(240, workArea.Width - 2 * ScreenMargin);
        var width = Math.Min(MenuWidth, maxWidth);
        var maxHeight = Math.Min(MenuHeightLimit, Math.Max(160, workArea.Height - 2 * ScreenMargin));
        var availableAbove = GetAvailableHeightAboveButton(workArea);
        var height = Math.Min(maxHeight, Math.Max(160, availableAbove));

        PopupRoot.Width = width;
        PopupRoot.MaxHeight = height;
        MenuPanel.Width = width;
        MenuPanel.MaxHeight = height;
        MenuScrollViewer.MaxHeight = height;
        MasterPopup.HorizontalOffset = (MasterToggleButton.ActualWidth - width) / 2;
        MasterPopup.VerticalOffset = -8;
    }

    private double GetAvailableHeightAboveButton(Rect workArea)
    {
        var source = PresentationSource.FromVisual(MasterToggleButton);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        if (!transform.HasValue)
        {
            return Math.Min(MenuHeightLimit, workArea.Height - 2 * ScreenMargin);
        }

        var buttonTop = transform.Value.Transform(MasterToggleButton.PointToScreen(new Point(0, 0))).Y;
        return buttonTop - workArea.Top - ScreenMargin;
    }

    private void MasterPopup_Opened(object? sender, EventArgs e)
    {
        MenuPanel.BeginAnimation(UIElement.OpacityProperty, null);
        MenuTransform.BeginAnimation(TranslateTransform.YProperty, null);
        MenuPanel.Opacity = 0;
        MenuTransform.Y = 12;

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        MenuPanel.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        MenuTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = easing });
    }

    private void MasterPopup_Closed(object? sender, EventArgs e)
    {
        MasterToggleButton.IsChecked = false;
        MenuPanel.BeginAnimation(UIElement.OpacityProperty, null);
        MenuTransform.BeginAnimation(TranslateTransform.YProperty, null);
        MenuPanel.Opacity = 1;
        MenuTransform.Y = 0;
    }

    public void RefreshMenuLayout()
    {
        if (MasterPopup.IsOpen)
        {
            PreparePopup();
        }
    }

    private void PopupRoot_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            MasterPopup.IsOpen = false;
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => SubmitSearch();

    private void MasterSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        SubmitSearch();
    }

    private void MasterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var isSlashCommand = MasterSearchBox.Text.TrimStart().StartsWith("/", StringComparison.Ordinal);
        CommandHelpPanel.Visibility = isSlashCommand ? Visibility.Visible : Visibility.Collapsed;
        if (!isSlashCommand)
        {
            CommandHelpStatus.Visibility = Visibility.Collapsed;
        }
    }

    private void SubmitSearch()
    {
        var query = MasterSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            MasterSearchBox.Focus();
            return;
        }

        if (query.StartsWith("/", StringComparison.Ordinal))
        {
            if (query.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                MasterSearchBox.Clear();
                CommandHelpPanel.Visibility = Visibility.Visible;
                CommandHelpStatus.Visibility = Visibility.Collapsed;
                return;
            }

            if (TryUnlockConsole(query))
            {
                MasterSearchBox.Clear();
                CommandHelpPanel.Visibility = Visibility.Collapsed;
                CommandHelpStatus.Visibility = Visibility.Collapsed;
                if (CardinalAdminConsolePanel.Visibility == Visibility.Visible)
                {
                    HideConsole();
                }
                else
                {
                    ShowConsole();
                }

                return;
            }

            CommandHelpPanel.Visibility = Visibility.Visible;
            CommandHelpStatus.Text = "Code ungültig oder Befehl unbekannt. Verwende /help für die Übersicht.";
            CommandHelpStatus.Visibility = Visibility.Visible;
            return;
        }

        CommandHelpPanel.Visibility = Visibility.Collapsed;
        MasterPopup.IsOpen = false;
        SearchRequested?.Invoke(query);
    }

    private static bool TryUnlockConsole(string command)
    {
        const string prefix = "/admin access ";
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedCode = command[prefix.Length..].Trim();
        return string.Equals(suppliedCode, AdminAccessCode, StringComparison.Ordinal);
    }

    private void ShowConsole()
    {
        EnsureConsoleInitialized();
        PinnedActionsPanel.Visibility = Visibility.Collapsed;
        CardinalAdminConsolePanel.Visibility = Visibility.Visible;
        RefreshMenuLayout();
        CardinalCommandInputBox.Focus();
    }

    public void SetActiveChatTarget(string target)
    {
        _activeChatTarget = target;
        ConsoleChatTargetTextBlock.Text = $"Chat mit {target}";
        AddConsoleMessage("Konsole", $"Chat mit {target} bereit. Schreibe deine Nachricht.");
    }

    private void HideConsole()
    {
        CardinalAdminConsolePanel.Visibility = Visibility.Collapsed;
        PinnedActionsPanel.Visibility = Visibility.Visible;
        RefreshMenuLayout();
    }

    private void EnsureConsoleInitialized()
    {
        if (_consoleInitialized)
        {
            return;
        }

        _consoleInitialized = true;
        _consoleMessages.Add(new ConsoleEntry(
            "Cardinal",
            "Funktionskanal bereit. Erlaubt: status, events, scan, hide."));
    }

    private static AllowedConsoleAction ResolveAllowedAction(string? input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "status" or "alya" or "alya.status" => AllowedConsoleAction.AlyaStatus,
            "events" or "alice.events" => AllowedConsoleAction.AliceEvents,
            "scan" or "alice.scan" or "sicherheits-scan" => AllowedConsoleAction.AliceScan,
            "hide" or "close" or "/hide" => AllowedConsoleAction.HideConsole,
            _ => AllowedConsoleAction.None
        };
    }

    private async Task ExecuteConsoleInputAsync(string? input)
    {
        EnsureConsoleInitialized();
        CardinalAdminConsolePanel.Visibility = Visibility.Visible;
        RefreshMenuLayout();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (input.TrimStart().StartsWith("/call", StringComparison.OrdinalIgnoreCase)
            && ConsoleTextCommandRequested?.Invoke(input) == true)
        {
            CardinalConsoleScrollViewer.ScrollToEnd();
            return;
        }

        var action = ResolveAllowedAction(input);
        if (action == AllowedConsoleAction.HideConsole)
        {
            HideConsole();
            return;
        }

        if (action == AllowedConsoleAction.None)
        {
            if (input.TrimStart().StartsWith("/", StringComparison.Ordinal))
            {
                AddConsoleMessage(
                    "Konsole",
                    "Unbekannter Befehl. Erlaubt: /call \"Alya\", /call \"Alice\", status, events, scan, hide.");
                return;
            }

            if (_activeChatTarget is null || ChatMessageRequested is null)
            {
                AddConsoleMessage("Konsole", "Öffne zuerst einen Chat mit /call \"Alya\" oder /call \"Alice\".");
                return;
            }

            AddConsoleMessage("Du", input.Trim());
            try
            {
                var reply = await ChatMessageRequested(_activeChatTarget, input.Trim());
                AddConsoleMessage(_activeChatTarget, reply);
            }
            catch (Exception ex)
            {
                AddConsoleMessage("Chat-Fehler", ex.Message);
            }

            return;
        }

        AddConsoleMessage("Du", input.Trim());
        if (ConsoleActionRequested is null)
        {
            AddConsoleMessage("Konsole", "Die NWOS-Funktionsschnittstelle ist nicht verfügbar.");
            return;
        }

        await ConsoleActionRequested(action);
        CardinalConsoleScrollViewer.ScrollToEnd();
    }

    public void AddConsoleMessage(string sender, string message)
    {
        EnsureConsoleInitialized();
        _consoleMessages.Add(new ConsoleEntry(sender, message));
        CardinalConsoleScrollViewer.ScrollToEnd();
    }

    private async void CardinalCommandInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        var command = CardinalCommandInputBox.Text;
        CardinalCommandInputBox.Clear();
        await ExecuteConsoleInputAsync(command);
    }

    private async void SendCardinalCommand_Click(object sender, RoutedEventArgs e)
    {
        var command = CardinalCommandInputBox.Text;
        CardinalCommandInputBox.Clear();
        await ExecuteConsoleInputAsync(command);
    }

    private async void ConsoleQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string command })
        {
            await ExecuteConsoleInputAsync(command);
        }
    }

    private void PcSettings_Click(object sender, RoutedEventArgs e)
    {
        MasterPopup.IsOpen = false;
        StartTarget("ms-settings:", null, "Windows-Einstellungen konnten nicht geöffnet werden.");
    }

    private void PowerSettings_Click(object sender, RoutedEventArgs e)
    {
        MasterPopup.IsOpen = false;
        StartTarget("ms-settings:power", null, "Energieeinstellungen konnten nicht geöffnet werden.");
    }

    private void ExitToWindows_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                Window.GetWindow(this),
                "NWOS beenden und zum Windows-Desktop wechseln?",
                "Zu Windows wechseln",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Windows konnte nicht gestartet werden.\n\n{ex.Message}",
                "NWOS Startmenü",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void NwosSettings_Click(object sender, RoutedEventArgs e)
    {
        MasterPopup.IsOpen = false;
        var settingsWindow = new NwosSettingsWindow
        {
            Owner = Window.GetWindow(this),
        };
        settingsWindow.ShowDialog();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Möchtest du den PC wirklich neu starten?", "Neustart bestätigen"))
        {
            StartTarget("shutdown.exe", "/r /t 0", "Der PC konnte nicht neu gestartet werden.");
        }

        MasterPopup.IsOpen = false;
    }

    private void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Möchtest du den PC wirklich herunterfahren?", "Herunterfahren bestätigen"))
        {
            StartTarget("shutdown.exe", "/s /t 0", "Der PC konnte nicht heruntergefahren werden.");
        }

        MasterPopup.IsOpen = false;
    }

    private bool ConfirmPowerAction(string message, string title) =>
        MessageBox.Show(Window.GetWindow(this), message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
        == MessageBoxResult.Yes;

    private void StartTarget(string fileName, string? arguments, string errorMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"{errorMessage}\n\n{ex.Message}",
                "NWOS Startmenü",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
