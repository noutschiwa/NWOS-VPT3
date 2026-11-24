using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;

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
    private const int ScreenWidthMetric = 0;
    private const int ScreenHeightMetric = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int metric);

    private const string AdminAccessCode = "135798642";
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(600);
    private Rect _hiddenBackgroundRect;
    private Rect _visibleBackgroundRect;
    private double _fortressRise;
    private double _menuLeft;
    private bool _isClosing;
    private readonly ObservableCollection<ConsoleEntry> _consoleMessages = new();
    private bool _consoleInitialized;

    public event Action<string>? SearchRequested;
    public Func<AllowedConsoleAction, Task>? ConsoleActionRequested { get; set; }
    public Func<string, bool>? ConsoleTextCommandRequested { get; set; }
    public Func<string, string, Task<string>>? ChatMessageRequested { get; set; }

    private sealed record ConsoleEntry(string Sender, string Message);
    private string? _activeChatTarget;

    public MasterFlyout()
    {
        InitializeComponent();
        OverlayRoot.SizeChanged += OverlayRoot_SizeChanged;
    }

    private void MasterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (MasterToggleButton.IsChecked == true)
        {
            OpenMenu();
        }
        else
        {
            CloseMenu();
        }
    }

    private void OpenMenu()
    {
        _isClosing = false;
        RemoveAnimations();
        var taskbarWindow = Window.GetWindow(this)
            ?? throw new InvalidOperationException("Das Master-Menü ist keinem Taskleistenfenster zugeordnet.");
        var taskbarHandle = new WindowInteropHelper(taskbarWindow).Handle;
        if (taskbarHandle == IntPtr.Zero || !GetWindowRect(taskbarHandle, out var taskbarBounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Taskleistenposition konnte nicht ermittelt werden.");
        }

        OverlayRoot.Width = SystemParameters.PrimaryScreenWidth;
        OverlayRoot.Height = SystemParameters.PrimaryScreenHeight;
        MasterPopup.HorizontalOffset = 0;
        MasterPopup.VerticalOffset = 0;
        MasterPopup.IsOpen = true;

        OverlayRoot.UpdateLayout();
        var popupSource = PresentationSource.FromVisual(OverlayRoot)
            ?? throw new InvalidOperationException("Das Master-Menü-Popup konnte nicht initialisiert werden.");
        var deviceToDip = popupSource.CompositionTarget.TransformFromDevice;
        var screenSize = new Point(GetSystemMetrics(ScreenWidthMetric), GetSystemMetrics(ScreenHeightMetric));
        if (screenSize.X <= 0 || screenSize.Y <= 0)
        {
            throw new Win32Exception("Die Bildschirmgröße konnte nicht ermittelt werden.");
        }

        var screenDipSize = deviceToDip.Transform(screenSize);
        var screenWidth = screenDipSize.X;
        var screenHeight = Math.Clamp(
            deviceToDip.Transform(new Point(0, taskbarBounds.Top)).Y,
            0,
            screenDipSize.Y);
        OverlayRoot.Width = screenWidth;
        OverlayRoot.Height = screenHeight;
        OverlayRoot.UpdateLayout();

        var anchorPoint = deviceToDip.Transform(MasterToggleButton.PointToScreen(
            new Point(MasterToggleButton.ActualWidth / 2, MasterToggleButton.ActualHeight / 2)));
        _menuLeft = Math.Clamp(
            anchorPoint.X - MenuPanel.Width / 2,
            12,
            Math.Max(12, screenWidth - MenuPanel.Width - 12));
        MenuPanel.MaxHeight = Math.Min(660, Math.Max(0, screenHeight - 8));
        Canvas.SetLeft(MenuPanel, _menuLeft);

        MenuPanel.UpdateLayout();
        var menuTop = PositionMenuAtTaskbar();
        PositionFortressAndBackground(menuTop, screenHeight);

        const double backgroundOpacity = 0.48;
        var backgroundClip = new RectangleGeometry(_hiddenBackgroundRect);
        MasterBackgroundImage.Clip = backgroundClip;
        MasterBackgroundImage.Opacity = backgroundOpacity;
        MenuTransform.Y = 0;
        MenuPanel.Opacity = 1;

        backgroundClip.Rect = _visibleBackgroundRect;
        AnimateRect(backgroundClip, _hiddenBackgroundRect,
            _visibleBackgroundRect, OpenDuration);
        AnimateDouble(MasterBackgroundImage, UIElement.OpacityProperty, 0, backgroundOpacity, OpenDuration);
        AnimateDouble(FortressTransform, TranslateTransform.YProperty, 0, _fortressRise, OpenDuration);
        AnimateDouble(MenuTransform, TranslateTransform.YProperty, 24, 0, OpenDuration);
        AnimateDouble(MenuPanel, UIElement.OpacityProperty, 0, 1, OpenDuration);
    }

    private void CloseMenu()
    {
        if (!MasterPopup.IsOpen || _isClosing)
        {
            MasterToggleButton.IsChecked = false;
            return;
        }

        _isClosing = true;
        MasterToggleButton.IsChecked = false;

        var backgroundClip = (RectangleGeometry)MasterBackgroundImage.Clip!;
        backgroundClip.Rect = _hiddenBackgroundRect;
        MasterBackgroundImage.Opacity = 0;
        FortressTransform.Y = 0;
        MenuTransform.Y = 24;
        MenuPanel.Opacity = 0;
        AnimateRect(backgroundClip, _visibleBackgroundRect,
            _hiddenBackgroundRect, CloseDuration, () =>
            {
                MasterPopup.IsOpen = false;
                _isClosing = false;
                RemoveAnimations();
            });

        AnimateDouble(MasterBackgroundImage, UIElement.OpacityProperty, 0.48, 0, CloseDuration);
        AnimateDouble(FortressTransform, TranslateTransform.YProperty, _fortressRise, 0, CloseDuration);
        AnimateDouble(MenuTransform, TranslateTransform.YProperty, 0, 24, CloseDuration);
        AnimateDouble(MenuPanel, UIElement.OpacityProperty, 1, 0, CloseDuration);
    }

    private void RemoveAnimations()
    {
        MasterBackgroundImage.BeginAnimation(UIElement.OpacityProperty, null);
        MasterBackgroundImage.Clip?.BeginAnimation(RectangleGeometry.RectProperty, null);
        FortressTransform.BeginAnimation(TranslateTransform.YProperty, null);
        MenuTransform.BeginAnimation(TranslateTransform.YProperty, null);
        MenuPanel.BeginAnimation(UIElement.OpacityProperty, null);
    }

    public void RefreshMenuLayout()
    {
        if (!MasterPopup.IsOpen || _isClosing)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!MasterPopup.IsOpen || _isClosing)
            {
                return;
            }

            var overlayHeight = OverlayRoot.ActualHeight > 0 ? OverlayRoot.ActualHeight : OverlayRoot.Height;
            var menuTop = PositionMenuAtTaskbar();
            PositionFortressAndBackground(menuTop, overlayHeight);
        }));
    }

    private double PositionMenuAtTaskbar(double gapAboveTaskbar = 8)
    {
        var overlayHeight = OverlayRoot.ActualHeight > 0 ? OverlayRoot.ActualHeight : OverlayRoot.Height;
        if (overlayHeight <= 0)
        {
            return 0;
        }

        MenuPanel.MaxHeight = Math.Min(660, Math.Max(0, overlayHeight - gapAboveTaskbar));
        MenuPanel.UpdateLayout();

        var panelWidth = MenuPanel.ActualWidth > 0 ? MenuPanel.ActualWidth : MenuPanel.Width;
        var panelHeight = MenuPanel.ActualHeight > 0
            ? MenuPanel.ActualHeight
            : MenuPanel.MaxHeight;
        _menuLeft = Math.Clamp(_menuLeft, 12, Math.Max(12, OverlayRoot.ActualWidth - panelWidth - 12));
        var menuTop = Math.Max(0, overlayHeight - panelHeight - gapAboveTaskbar);

        Canvas.SetLeft(MenuPanel, _menuLeft);
        Canvas.SetTop(MenuPanel, menuTop);
        return menuTop;
    }

    private void PositionFortressAndBackground(double menuTop, double overlayHeight)
    {
        var panelWidth = MenuPanel.ActualWidth > 0 ? MenuPanel.ActualWidth : MenuPanel.Width;
        var menuCenterX = _menuLeft + panelWidth / 2;
        var fortressWidth = FortressImage.ActualWidth > 0 ? FortressImage.ActualWidth : FortressImage.Width;
        var fortressHeight = FortressImage.ActualHeight > 0 ? FortressImage.ActualHeight : FortressImage.Height;
        Canvas.SetLeft(FortressImage, menuCenterX - fortressWidth / 2);

        var fortressStartTop = overlayHeight - fortressHeight + 64;
        var fortressTargetTop = Math.Max(8, menuTop - fortressHeight - 12);
        Canvas.SetTop(FortressImage, fortressStartTop);
        _fortressRise = fortressTargetTop - fortressStartTop;

        MasterBackgroundImage.Width = panelWidth + 44;
        MasterBackgroundImage.Height = overlayHeight;
        Canvas.SetLeft(MasterBackgroundImage, menuCenterX - MasterBackgroundImage.Width / 2);
        Canvas.SetTop(MasterBackgroundImage, 0);
        _hiddenBackgroundRect = new Rect(0, 0, MasterBackgroundImage.Width, 0);
        _visibleBackgroundRect = new Rect(0, 0, MasterBackgroundImage.Width, MasterBackgroundImage.Height);
    }

    private void OverlayRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshMenuLayout();
    }

    private static void AnimateDouble(
        IAnimatable target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration)
    {
        var easingMode = duration == OpenDuration ? EasingMode.EaseOut : EasingMode.EaseIn;
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easingMode },
            FillBehavior = FillBehavior.Stop,
        };

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateRect(
        RectangleGeometry target,
        Rect from,
        Rect to,
        TimeSpan duration,
        Action? completed = null)
    {
        var easingMode = duration == OpenDuration ? EasingMode.EaseOut : EasingMode.EaseIn;
        var animation = new RectAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easingMode },
            FillBehavior = FillBehavior.Stop,
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        target.BeginAnimation(RectangleGeometry.RectProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OverlayRoot_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseMenu();
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
        var newVisibility = isSlashCommand ? Visibility.Visible : Visibility.Collapsed;
        if (CommandHelpPanel.Visibility != newVisibility)
        {
            CommandHelpPanel.Visibility = newVisibility;
            if (!isSlashCommand)
            {
                CommandHelpStatus.Visibility = Visibility.Collapsed;
            }

            RefreshMenuLayout();
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
                RefreshMenuLayout();
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
            RefreshMenuLayout();
            return;
        }

        CommandHelpPanel.Visibility = Visibility.Collapsed;
        RefreshMenuLayout();
        CloseMenu();
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
        CardinalConsoleMessagesControl.ItemsSource = _consoleMessages;
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
                AddConsoleMessage("Konsole", "Unbekannter Befehl. Erlaubt: /call \"Alya\", /call \"Alice\", status, events, scan, hide.");
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

        _consoleMessages.Add(new ConsoleEntry("Du", input.Trim()));
        CardinalConsoleScrollViewer.ScrollToEnd();

        if (ConsoleActionRequested is null)
        {
            _consoleMessages.Add(new ConsoleEntry("Konsole", "Die NWOS-Funktionsschnittstelle ist nicht verfügbar."));
            CardinalConsoleScrollViewer.ScrollToEnd();
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
        CloseMenu();
        StartTarget("ms-settings:", null, "Windows-Einstellungen konnten nicht geöffnet werden.");
    }

    private void PowerSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        StartTarget("ms-settings:power", null, "Energieeinstellungen konnten nicht geöffnet werden.");
    }

    private void NwosSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
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

        CloseMenu();
    }

    private void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmPowerAction("Möchtest du den PC wirklich herunterfahren?", "Herunterfahren bestätigen"))
        {
            StartTarget("shutdown.exe", "/s /t 0", "Der PC konnte nicht heruntergefahren werden.");
        }

        CloseMenu();
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
            MessageBox.Show(Window.GetWindow(this), $"{errorMessage}\n\n{ex.Message}",
                "NWOS Startmenü", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
