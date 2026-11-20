using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NWOS.Contracts;
using NWOS.DesktopShell.Ui.ViewModels;

namespace NWOS.DesktopShell.Ui
{
    public class AppIconItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;

        private ImageSource? _iconImage;
        private bool _iconLoaded;
        private bool _isRunning;

        [JsonIgnore]
        public ImageSource? IconImage
        {
            get
            {
                if (!_iconLoaded)
                {
                    _iconImage = IconHelper.GetIcon(ExecutablePath);
                    _iconLoaded = true;
                }
                return _iconImage;
            }
        }

        [JsonIgnore]
        public bool HasIcon => IconImage != null;

        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed record WidgetTypeItem(string WidgetTypeId, string DisplayName);
    public sealed record SearchResultItem(string Path, string DisplayName);

    public partial class MainWindow : Window
    {
        private const string DesktopShellId = "nwos.desktopshell";
        private const string WidgetsId = "nwos.widgets";

        private static readonly string AppHubStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NWOS",
            "apphub.json");

        // Win32 APIs für Fensterfokus & Wiederherstellung
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        private readonly CardinalClient _cardinal;
        private readonly ObservableCollection<IconViewModel> _icons = new();
        private readonly ObservableCollection<BoxViewModel> _boxes = new();
        private readonly ObservableCollection<WidgetViewModel> _widgets = new();

        private readonly DispatcherTimer _widgetRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _appStateTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        private bool _refreshingWidgets;
        private int _lastIndexCount;

        // App-Hub Drag & Drop Felder
        private Point _dragStartPoint;
        private AppIconItem? _draggedItem;
        public ObservableCollection<AppIconItem> AppIcons { get; } = new();

        // 1. Parameterloser Konstruktor für den automatischen Start durch WPF (StartupUri)
        public MainWindow() : this(new CardinalClient())
        {
        }

        // 2. Hauptkonstruktor mit Cardinal-Client, App-Hub und Taskleisten-Positionierung
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

            // App-Hub Standard-Icons laden und Prozess-Überwachung starten
            InitializeAppHub();
            _appStateTimer.Tick += (_, _) => UpdateRunningAppsState();
            _appStateTimer.Start();
            UpdateRunningAppsState();

            // Weitere ItemsSources zuweisen
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

        #region App-Hub Initialisierung, Persistenz & Prozess-Tracking

        private void InitializeAppHub()
        {
            if (!LoadAppHubState())
            {
                // Standard-Apps nur beim allerersten Start anlegen
                AppIcons.Add(new AppIconItem { Name = "📁", FullName = "Explorer", ExecutablePath = "explorer.exe" });
                AppIcons.Add(new AppIconItem { Name = "🌐", FullName = "Microsoft Edge", ExecutablePath = "msedge.exe" });
                AppIcons.Add(new AppIconItem { Name = "💻", FullName = "Eingabeaufforderung", ExecutablePath = "cmd.exe" });
                AppIcons.Add(new AppIconItem { Name = "📝", FullName = "Editor", ExecutablePath = "notepad.exe" });

                SaveAppHubState();
            }

            AppHubItemsControl.ItemsSource = AppIcons;
        }

        private void SaveAppHubState()
        {
            try
            {
                string? directory = Path.GetDirectoryName(AppHubStoragePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(AppIcons, options);
                File.WriteAllText(AppHubStoragePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Speichern des App-Hubs: {ex.Message}");
            }
        }

        private bool LoadAppHubState()
        {
            try
            {
                if (!File.Exists(AppHubStoragePath))
                    return false;

                string json = File.ReadAllText(AppHubStoragePath);
                var savedItems = JsonSerializer.Deserialize<List<AppIconItem>>(json);

                if (savedItems != null && savedItems.Count > 0)
                {
                    AppIcons.Clear();
                    foreach (var item in savedItems)
                    {
                        AppIcons.Add(item);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Laden des App-Hubs: {ex.Message}");
            }

            return false;
        }

        private void UpdateRunningAppsState()
        {
            if (AppIcons.Count == 0) return;

            var allProcesses = Process.GetProcesses();
            var runningProcessNames = new HashSet<string>(
                allProcesses.Select(p => p.ProcessName),
                StringComparer.OrdinalIgnoreCase);

            // Für explorer.exe prüfen, ob tatsächlich ein sichtbares Explorer-Fenster offen ist
            bool hasOpenExplorerWindow = allProcesses.Any(p =>
                p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                p.MainWindowHandle != IntPtr.Zero &&
                !string.IsNullOrWhiteSpace(p.MainWindowTitle));

            foreach (var item in AppIcons)
            {
                string processName = Path.GetFileNameWithoutExtension(item.ExecutablePath);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    item.IsRunning = false;
                    continue;
                }

                if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsRunning = hasOpenExplorerWindow;
                }
                else
                {
                    item.IsRunning = runningProcessNames.Contains(processName);
                }
            }

            foreach (var p in allProcesses)
            {
                p.Dispose();
            }
        }

        private bool TryBringProcessToFront(string executablePath)
        {
            string processName = Path.GetFileNameWithoutExtension(executablePath);
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            var processes = Process.GetProcessesByName(processName);
            try
            {
                var targetProcess = processes.FirstOrDefault(p =>
                    p.MainWindowHandle != IntPtr.Zero &&
                    (!processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                     !string.IsNullOrWhiteSpace(p.MainWindowTitle)));

                if (targetProcess != null)
                {
                    IntPtr hWnd = targetProcess.MainWindowHandle;

                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                    }

                    SetForegroundWindow(hWnd);
                    return true;
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }

            return false;
        }

        #endregion

        #region Taskbar Zone Actions (Alya & Monitor)

        private async void AlyaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("Alya AI-Interface wird initialisiert...", "NWOS Alya Assistant", MessageBoxButton.OK, MessageBoxImage.Information);

                var doc = await _cardinal.CallAsync("nwos.alya", "chat.status", new { }, PermissionScope.FileSystemRead);
                string response = doc != null ? doc.RootElement.ToString() : "Alya Kern bereit.";

                MessageBox.Show($"Alya Antwort:\n{response}", "NWOS Alya", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Alya konnte nicht erreicht werden: {ex.Message}", "NWOS Alya", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Task Manager konnte nicht geöffnet werden: {ex.Message}", "NWOS Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region App-Hub Drag & Drop, Kontextmenü und App-Start

        private void RemoveAppIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item)
            {
                if (AppIcons.Remove(item))
                {
                    SaveAppHubState();
                }
            }
        }

        private void OpenNewInstance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.ExecutablePath,
                        UseShellExecute = true
                    });

                    UpdateRunningAppsState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Neues Fenster für '{item.FullName}' konnte nicht geöffnet werden: {ex.Message}", "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item)
            {
                string processName = Path.GetFileNameWithoutExtension(item.ExecutablePath);
                if (string.IsNullOrWhiteSpace(processName))
                    return;

                var processes = Process.GetProcessesByName(processName);
                try
                {
                    foreach (var p in processes)
                    {
                        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                            {
                                p.CloseMainWindow();
                            }
                            continue;
                        }

                        if (!p.CloseMainWindow())
                        {
                            p.Kill();
                        }
                    }

                    UpdateRunningAppsState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"'{item.FullName}' konnte nicht beendet werden: {ex.Message}", "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
                }
            }
        }

        private void AppIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            if (sender is FrameworkElement element && element.DataContext is AppIconItem item)
            {
                _draggedItem = item;
            }
        }

        private void AppIcon_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is DependencyObject dragSource)
                    {
                        DataObject dragData = new DataObject(typeof(AppIconItem), _draggedItem);
                        DragDrop.DoDragDrop(dragSource, dragData, DragDropEffects.Move);
                        _draggedItem = null;
                    }
                }
            }
        }

        private void AppHub_Drop(object sender, DragEventArgs e)
        {
            // FALL 1: Internes Umsortieren bestehender App-Icons
            if (e.Data.GetDataPresent(typeof(AppIconItem)))
            {
                var droppedItem = e.Data.GetData(typeof(AppIconItem)) as AppIconItem;
                if (droppedItem == null) return;

                if (sender is FrameworkElement targetElement && targetElement.DataContext is AppIconItem targetItem)
                {
                    int oldIndex = AppIcons.IndexOf(droppedItem);
                    int newIndex = AppIcons.IndexOf(targetItem);

                    if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex)
                    {
                        AppIcons.Move(oldIndex, newIndex);
                        SaveAppHubState();
                    }
                }
                else
                {
                    int oldIndex = AppIcons.IndexOf(droppedItem);
                    if (oldIndex != -1 && oldIndex != AppIcons.Count - 1)
                    {
                        AppIcons.Move(oldIndex, AppIcons.Count - 1);
                        SaveAppHubState();
                    }
                }

                e.Handled = true;
                return;
            }

            // FALL 2: Externe Dateien (.exe, .lnk, Ordner etc.) anheften
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
                {
                    int insertIndex = AppIcons.Count;
                    if (sender is FrameworkElement targetElement && targetElement.DataContext is AppIconItem targetItem)
                    {
                        int targetIdx = AppIcons.IndexOf(targetItem);
                        if (targetIdx != -1) insertIndex = targetIdx;
                    }

                    bool changed = false;
                    foreach (string filePath in droppedFiles)
                    {
                        if (AppIcons.Any(a => a.ExecutablePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (string.IsNullOrWhiteSpace(fileName))
                            fileName = filePath;

                        string shortLabel = fileName.Length >= 2
                            ? fileName.Substring(0, 2).ToUpperInvariant()
                            : fileName.ToUpperInvariant();

                        AppIcons.Insert(insertIndex, new AppIconItem
                        {
                            Name = shortLabel,
                            FullName = fileName,
                            ExecutablePath = filePath
                        });
                        insertIndex++;
                        changed = true;
                    }

                    if (changed)
                    {
                        SaveAppHubState();
                    }
                }

                e.Handled = true;
            }
        }

        private void AppIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AppIconItem item)
            {
                bool forceNewInstance = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (!forceNewInstance && TryBringProcessToFront(item.ExecutablePath))
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.ExecutablePath,
                        UseShellExecute = true
                    });

                    UpdateRunningAppsState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"App '{item.Name}' konnte nicht gestartet werden: {ex.Message}", "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Cardinal IPC, Desktop Icons, Boxes & Widgets

        private void UpdateConnectionState()
        {
            Title = _cardinal.LastError is null
                ? "NWOS System Shell — mit Cardinal verbunden"
                : $"NWOS System Shell — NICHT verbunden ({_cardinal.LastError})";
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
            catch (Exception) { }
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

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
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

        #endregion
    }
}