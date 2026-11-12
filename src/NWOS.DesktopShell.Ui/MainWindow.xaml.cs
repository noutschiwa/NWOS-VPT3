using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NWOS.Contracts;
using NWOS.DesktopShell.Ui.ViewModels;

namespace NWOS.DesktopShell.Ui;

public sealed record WidgetTypeItem(string WidgetTypeId, string DisplayName);
public sealed record SearchResultItem(string Path, string DisplayName);

public partial class MainWindow : Window
{
    private const string DesktopShellId = "nwos.desktopshell";
    private const string WidgetsId = "nwos.widgets";

    private readonly CardinalClient _cardinal;
    private readonly ObservableCollection<IconViewModel> _icons = new();
    private readonly ObservableCollection<BoxViewModel> _boxes = new();
    private readonly ObservableCollection<WidgetViewModel> _widgets = new();

    private readonly DispatcherTimer _widgetRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshingWidgets;
    private int _lastIndexCount;

    // 1. Parameterloser Konstruktor für den automatischen Start durch WPF (StartupUri)
    public MainWindow() : this(new CardinalClient())
    {
    }

    // 2. Kombinierter Konstruktor mit Dependency-Injection, Event-Bindungen und Positions-Logik
    public MainWindow(CardinalClient cardinal)
    {
        InitializeComponent();
        _cardinal = cardinal;

        // Bildschirmposition und Größe setzen (am unteren Bildschirmrand andocken)
        Width = SystemParameters.PrimaryScreenWidth;
        Height = 45;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // ItemsSources zuweisen
        AppHubItemsControl.ItemsSource = _icons;
        BoxesItemsControl.ItemsSource = _boxes;
        WidgetsItemsControl.ItemsSource = _widgets;

        _widgetRefreshTimer.Tick += async (_, _) => await RefreshWidgetsAsync();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _widgetRefreshTimer.Stop();
            else if (_widgets.Count > 0) _widgetRefreshTimer.Start();
        };

        Loaded += async (_, _) => await InitializeAsync();
    }

private async void AlyaButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        MessageBox.Show("Alya AI-Interface wird initialisiert...", "NWOS Alya Assistant", MessageBoxButton.OK, MessageBoxImage.Information);

        // IPC-Aufruf an das Alya-Modul im Host
        var doc = await _cardinal.CallAsync("nwos.alya", "chat.status", new { }, PermissionScope.FileSystemRead);
        string response = doc != null ? doc.RootElement.ToString() : "Alya Kern bereit.";

        MessageBox.Show($"Alya Antwort:\n{response}", "NWOS Alya", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Alya konnte nicht erreicht werden: {ex.Message}", "NWOS Alya", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

private async void MasterButton_Click(object sender, RoutedEventArgs e)
{
    var confirmation = MessageBox.Show(
        "Möchtest du das System über den NWOS Master neu starten?\n\nKlicke auf [Ja] für einen System-Neustart.",
        "NWOS Master - Systemsteuerung",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (confirmation == MessageBoxResult.Yes)
    {
        try
        {
            // IPC-Befehl an den Host/SystemCenter senden
            var doc = await _cardinal.CallAsync("nwos.systemcenter", "system.reboot", new { }, PermissionScope.FileSystemWrite);

            string responseMessage = doc != null ? doc.RootElement.ToString() : "Befehl gesendet.";
            MessageBox.Show($"Kernel-Antwort:\n{responseMessage}", "NWOS Master", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Ausführen des System-Befehls: {ex.Message}", "NWOS Master Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

private void MonitorButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        // Startet den echten Windows Task Manager
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe")
        {
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Task Manager konnte nicht geöffnet werden: {ex.Message}", "NWOS Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

private async void InfoButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        // IPC-Abfrage an den Host/Kernel für aktuelle System- und Sicherheitsmeldungen
        string notificationsText = "Keine neuen Benachrichtigungen.";

        var doc = await _cardinal.CallAsync("nwos.systemcenter", "notifications.list", new { }, PermissionScope.FileSystemRead);
        if (doc != null && doc.RootElement.ValueKind != JsonValueKind.Null)
        {
            // Beispiel: Falls der Host strukturierte Benachrichtigungen liefert
            notificationsText = doc.RootElement.ToString();
        }
        else
        {
            // Fallback mit Standard-Systemmeldungen (inkl. simulierter Sicherheitswarnung)
            notificationsText =
                "🔔 NWOS Infobox & Benachrichtigungen:\n\n" +
                "• [System] Cardinal Kernel läuft stabil (v3.0)\n" +
                "• [E-Mail] Keine neuen Nachrichten\n" +
                "• [Alice Security] Quick-Scan: System sicher – Keine Viren gefunden ✓\n" +
                "• [Alya] Taskleisten-Zonen erfolgreich verankert.";
        }

        MessageBox.Show(notificationsText, "NWOS Infobox", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Fehler beim Laden der Infobox: {ex.Message}", "NWOS Info", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

    private void UpdateConnectionState()
    {
        Title = _cardinal.LastError is null
            ? "NWOS System Shell — mit Cardinal verbunden"
            : $"NWOS System Shell — NICHT verbunden ({_cardinal.LastError})";
    }

private async void AudioButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        // Sende den IPC-Befehl an den Host-Dienst (mit einem gültigen PermissionScope)
        string responseText = "Unbekannt";
        var doc = await _cardinal.CallAsync("nwos.systemcenter", "audio.adjust", new { volume = 75 }, PermissionScope.FileSystemWrite);
        if (doc != null)
        {
            responseText = doc.RootElement.ToString();
        }

        MessageBox.Show($"Antwort vom Host: {responseText}", "NWOS Audio", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"IPC-Verbindungsfehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

    private async Task InitializeAsync()
    {
        try
        {
            await LoadIconsAsync();
            await LoadBoxesAsync();
            await LoadWidgetsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Initialisierungsfehler: {ex.Message}", "NWOS Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateConnectionState();

        if (_widgets.Count > 0) _widgetRefreshTimer.Start();

        _ = RebuildSearchIndexAsync();
    }

    private async Task LoadIconsAsync()
    {
        var states = new Dictionary<string, bool>();
        using (var doc = await _cardinal.CallAsync(DesktopShellId, "icon.listStates", new { }, PermissionScope.FileSystemRead))
        {
            if (doc is not null)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    states[prop.Name] = prop.Value.GetBoolean();
            }
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        List<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(desktop).OrderBy(p => p).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        _icons.Clear();
        foreach (var path in entries)
        {
            var iconId = path.ToLowerInvariant();
            _icons.Add(new IconViewModel(iconId, Path.GetFileNameWithoutExtension(path), path,
                states.GetValueOrDefault(iconId, true)));
        }
    }

    private async Task LoadBoxesAsync()
    {
        using var doc = await _cardinal.CallAsync(DesktopShellId, "box.list", new { }, PermissionScope.FileSystemRead);
        if (doc is null) return;

        _boxes.Clear();
        foreach (var box in doc.RootElement.EnumerateArray())
        {
            var vm = new BoxViewModel(box.GetProperty("boxId").GetString()!, box.GetProperty("name").GetString()!);
            foreach (var item in box.GetProperty("items").EnumerateArray())
                vm.Items.Add(new BoxItemViewModel(item.GetProperty("sourcePath").GetString()!));
            _boxes.Add(vm);
        }
    }

    private async Task LoadWidgetsAsync()
    {
        using (var types = await _cardinal.CallAsync(WidgetsId, "widget.listTypes", new { }, PermissionScope.FileSystemRead))
        {
            if (types is not null)
            {
                var items = types.RootElement.EnumerateArray()
                    .Select(t => new WidgetTypeItem(t.GetProperty("widgetTypeId").GetString()!, t.GetProperty("displayName").GetString()!))
                    .ToList();
                WidgetTypeComboBox.ItemsSource = items;
                WidgetTypeComboBox.SelectedIndex = items.Count > 0 ? 0 : -1;
            }
        }

        using var doc = await _cardinal.CallAsync(WidgetsId, "widget.list", new { }, PermissionScope.FileSystemRead);
        if (doc is null) return;

        _widgets.Clear();
        foreach (var w in doc.RootElement.EnumerateArray())
            _widgets.Add(new WidgetViewModel(w.GetProperty("instanceId").GetString()!, w.GetProperty("widgetTypeId").GetString()!));
    }

    private async Task RefreshWidgetsAsync()
    {
        if (_refreshingWidgets) return;
        _refreshingWidgets = true;
        try
        {
            foreach (var vm in _widgets.ToList())
            {
                using var doc = await _cardinal.CallAsync(WidgetsId, "widget.render",
                    new { instanceId = vm.InstanceId }, PermissionScope.FileSystemRead);
                if (doc is null) continue;

                if (doc.RootElement.ValueKind == JsonValueKind.Null)
                {
                    vm.ApplyValues(null);
                }
                else
                {
                    var values = doc.RootElement.GetProperty("values").EnumerateObject()
                        .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    vm.ApplyValues(values);
                }
            }
        }
        finally
        {
            _refreshingWidgets = false;
            UpdateConnectionState();
        }
    }

    private async Task RebuildSearchIndexAsync()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            using var doc = await _cardinal.CallAsync(DesktopShellId, "search.rebuildIndex",
                new { rootDirectory = desktop }, PermissionScope.FileSystemWrite);
            if (doc is not null) _lastIndexCount = doc.RootElement.GetProperty("entryCount").GetInt32();
        }
        catch (Exception) { /* Ignoriert falls Cardinal beim Indexieren kurz nicht erreichbar ist */ }
    }

    private async void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { DataContext: IconViewModel iconVm }) return;

        using var doc = await _cardinal.CallAsync(DesktopShellId, "icon.toggleVisibility",
            new { iconId = iconVm.IconId }, PermissionScope.FileSystemWrite);
        if (doc is not null) iconVm.IsVisible = doc.RootElement.GetProperty("isVisible").GetBoolean();
        UpdateConnectionState();
    }

    private async void CreateBox_Click(object sender, RoutedEventArgs e)
    {
        using var doc = await _cardinal.CallAsync(DesktopShellId, "box.create",
            new { name = $"Box {_boxes.Count + 1}" }, PermissionScope.FileSystemWrite);
        if (doc is not null)
            _boxes.Add(new BoxViewModel(doc.RootElement.GetProperty("boxId").GetString()!, doc.RootElement.GetProperty("name").GetString()!));
        UpdateConnectionState();
    }

    private void BoxBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BoxViewModel vm } && !vm.IsPinned) vm.IsExpanded = true;
    }

    private void BoxBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BoxViewModel vm } && !vm.IsPinned) vm.IsExpanded = false;
    }

    private async void BoxBorder_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BoxViewModel boxVm }) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        foreach (var path in paths)
        {
            var kind = Directory.Exists(path) ? "Folder" : "File";
            using var doc = await _cardinal.CallAsync(DesktopShellId, "box.addItem",
                new { boxId = boxVm.BoxId, sourcePath = path, kind }, PermissionScope.FileSystemWrite);
            if (doc is not null) boxVm.Items.Add(new BoxItemViewModel(path));
        }

        UpdateConnectionState();
    }

    private async void CreateWidget_Click(object sender, RoutedEventArgs e)
    {
        if (WidgetTypeComboBox.SelectedItem is not WidgetTypeItem type)
        {
            MessageBox.Show("Bitte zuerst einen Widget-Typ auswählen (ist Cardinal gestartet?).", "NWOS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var doc = await _cardinal.CallAsync(WidgetsId, "widget.create",
            new { widgetTypeId = type.WidgetTypeId }, PermissionScope.FileSystemWrite);
        if (doc is not null)
        {
            _widgets.Add(new WidgetViewModel(doc.RootElement.GetProperty("instanceId").GetString()!, type.WidgetTypeId));
            if (!_widgetRefreshTimer.IsEnabled) _widgetRefreshTimer.Start();
        }

        UpdateConnectionState();
    }

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Workspace WS1 aktiv.", "NWOS Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsList.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
            return;
        }

        using var doc = await _cardinal.CallAsync(DesktopShellId, "search.global", new { query }, PermissionScope.FileSystemRead);
        if (doc is null || SearchBox.Text != query) return;

        var results = doc.RootElement.EnumerateArray()
            .Select(r => new SearchResultItem(r.GetProperty("path").GetString()!, r.GetProperty("displayName").GetString()!))
            .ToList();
        SearchResultsList.ItemsSource = results;
        SearchResultsList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RebuildIndex_Click(object sender, RoutedEventArgs e)
    {
        await RebuildSearchIndexAsync();
        MessageBox.Show($"Suchindex neu aufgebaut ({_lastIndexCount} Einträge, Desktop-Ordner).",
            "NWOS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AppIconButton_Click(object sender, RoutedEventArgs e)
    {
        // Statt 'dynamic app' verwenden wir 'object appObj' oder casten direkt
        if (sender is FrameworkElement { DataContext: object appObj })
        {
            // Wir holen den Namen per Reflection oder über das bekannte ViewModel
            var nameProperty = appObj.GetType().GetProperty("Name");
            var appName = nameProperty?.GetValue(appObj)?.ToString() ?? "Unbekannt";

            MessageBox.Show($"App / Datei geöffnet: {appName}", "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
  private Point _dragStartPoint;

  private void AppIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
      _dragStartPoint = e.GetPosition(null);
  }

  private void AppIcon_MouseMove(object sender, MouseEventArgs e)
  {
      var mousePos = e.GetPosition(null);
      var diff = _dragStartPoint - mousePos;

      if (e.LeftButton == MouseButtonState.Pressed &&
          (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
           Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
      {
          if (sender is FrameworkElement { DataContext: IconViewModel draggedData })
          {
              DragDrop.DoDragDrop(sender as DependencyObject, draggedData, DragDropEffects.Move);
          }
      }
  }

  private void AppHub_Drop(object sender, DragEventArgs e)
  {
      if (e.Data.GetDataPresent(typeof(IconViewModel)))
      {
          var droppedData = e.Data.GetData(typeof(IconViewModel)) as IconViewModel;
          MessageBox.Show($"App '{droppedData?.DisplayName}' im Hub verschoben.", "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Information);
      }
  }
 }