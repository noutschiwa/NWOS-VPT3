using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NWOS.DesktopShell.Ui.Button;

public partial class MasterFlyout : UserControl
{
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(600);
    private Rect _hiddenBackgroundRect;
    private Rect _visibleBackgroundRect;
    private double _fortressRise;
    private bool _isClosing;

    public event Action<string>? SearchRequested;

    public MasterFlyout()
    {
        InitializeComponent();
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
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = Math.Max(0, SystemParameters.PrimaryScreenHeight - Height);
        OverlayRoot.Width = screenWidth;
        OverlayRoot.Height = screenHeight;

        var anchorPoint = MasterToggleButton.PointToScreen(
            new Point(MasterToggleButton.ActualWidth / 2, MasterToggleButton.ActualHeight / 2));
        if (PresentationSource.FromVisual(MasterToggleButton)?.CompositionTarget is { } compositionTarget)
        {
            anchorPoint = compositionTarget.TransformFromDevice.Transform(anchorPoint);
        }

        var menuLeft = Math.Clamp(anchorPoint.X - MenuPanel.Width / 2, 12, screenWidth - MenuPanel.Width - 12);
        var menuTop = Math.Max(12, screenHeight - MenuPanel.Height - 12);
        Canvas.SetLeft(MenuPanel, menuLeft);
        Canvas.SetTop(MenuPanel, menuTop);

        MasterBackgroundImage.Width = MenuPanel.Width + 44;
        MasterBackgroundImage.Height = MenuPanel.Height + FortressImage.Height + 64;
        _hiddenBackgroundRect = new Rect(0, MasterBackgroundImage.Height, MasterBackgroundImage.Width, 0);
        _visibleBackgroundRect = new Rect(0, 0, MasterBackgroundImage.Width, MasterBackgroundImage.Height);
        const double backgroundOpacity = 0.48;
        var backgroundClip = new RectangleGeometry(_hiddenBackgroundRect);
        MasterBackgroundImage.Clip = backgroundClip;
        MasterBackgroundImage.Opacity = backgroundOpacity;

        var menuCenterX = menuLeft + MenuPanel.Width / 2;
        Canvas.SetLeft(FortressImage, menuCenterX - FortressImage.Width / 2);
        var fortressStartTop = screenHeight - FortressImage.Height + 64;
        var fortressTargetTop = Math.Max(12, menuTop - FortressImage.Height - 14);
        Canvas.SetTop(FortressImage, fortressStartTop);
        _fortressRise = fortressTargetTop - fortressStartTop;
        FortressTransform.Y = _fortressRise;

        Canvas.SetLeft(MasterBackgroundImage, menuCenterX - MasterBackgroundImage.Width / 2);
        Canvas.SetTop(MasterBackgroundImage, fortressTargetTop - 24);
        MenuTransform.Y = 0;
        MenuPanel.Opacity = 1;

        MasterPopup.HorizontalOffset = 0;
        MasterPopup.VerticalOffset = 0;
        MasterPopup.IsOpen = true;

        backgroundClip.Rect = _visibleBackgroundRect;
        AnimateRect(backgroundClip, _hiddenBackgroundRect,
            _visibleBackgroundRect, OpenDuration);
        AnimateDouble(MasterBackgroundImage, UIElement.OpacityProperty, 0, backgroundOpacity, OpenDuration);
        AnimateDouble(FortressTransform, TranslateTransform.YProperty, 0, _fortressRise, OpenDuration);
        AnimateDouble(MenuTransform, TranslateTransform.YProperty, 48, 0, OpenDuration);
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
        MenuTransform.Y = 48;
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
        AnimateDouble(MenuTransform, TranslateTransform.YProperty, 0, 48, CloseDuration);
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

    private void SubmitSearch()
    {
        var query = MasterSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            MasterSearchBox.Focus();
            return;
        }

        CloseMenu();
        SearchRequested?.Invoke(query);
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
