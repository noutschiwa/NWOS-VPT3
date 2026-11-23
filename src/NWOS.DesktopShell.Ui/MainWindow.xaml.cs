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

    public sealed class SearchResultItem
        {
            private ImageSource? _iconImage;
            private bool _iconLoaded;

            public string Icon { get; init; } = "🔹";
            public string Title { get; init; } = string.Empty;
            public string Subtitle { get; init; } = string.Empty;
            public string Category { get; init; } = "App";
            public string? TargetPath { get; init; }
            public string? AlicePrompt { get; init; }

            [JsonIgnore]
            public ImageSource? IconImage
            {
                get
                {
                    if (!_iconLoaded)
                    {
                        if (!string.IsNullOrWhiteSpace(TargetPath))
                        {
                            _iconImage = IconHelper.GetIcon(TargetPath);
                        }
                        _iconLoaded = true;
                    }
                    return _iconImage;
                }
            }

            [JsonIgnore]
            public bool HasIcon => IconImage != null;
        }

    public sealed class AliceSuggestedAction
    {
        public string Label { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public string LaunchPath { get; init; } = string.Empty;
    }

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
        private readonly ObservableCollection<AppIconItem> _leftAppIcons = new();
        private readonly ObservableCollection<AppIconItem> _rightAppIcons = new();

        private readonly DispatcherTimer _widgetRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _appStateTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
        private bool _refreshingWidgets;
        private int _lastIndexCount;

        // App-Hub Drag & Drop Felder
        private Point _dragStartPoint;
        private AppIconItem? _draggedItem;
        public ObservableCollection<AppIconItem> AppIcons { get; } = new();

        // Zone-2 Such-Cache
        private List<SearchResultItem>? _cachedStartMenuApps;

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
            AppIcons.CollectionChanged += (_, _) => RefreshAppHubHalves();
            CenterMasterFlyout.SearchRequested += query =>
            {
                SearchBox.Text = query;
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            };
            _appStateTimer.Tick += (_, _) => UpdateRunningAppsState();
            _appStateTimer.Start();
            UpdateRunningAppsState();

            // Weitere ItemsSources zuweisen
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

        private void PinSearchResultToAppHub_Click(object sender, RoutedEventArgs e)
                {
                    SearchResultItem? item = null;

                    if (sender is MenuItem { Tag: SearchResultItem taggedItem })
                    {
                        item = taggedItem;
                    }
                    else if (sender is MenuItem { DataContext: SearchResultItem dcItem })
                    {
                        item = dcItem;
                    }
                    else if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { Tag: SearchResultItem targetItem } } })
                    {
                        item = targetItem;
                    }

                    if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                    {
                        MessageBox.Show("Dieser Eintrag besitzt keinen startbaren Pfad zum Anheften.",
                            "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    bool alreadyPinned = AppIcons.Any(a =>
                        a.ExecutablePath.Equals(item.TargetPath, StringComparison.OrdinalIgnoreCase) ||
                        a.FullName.Equals(item.Title, StringComparison.OrdinalIgnoreCase));

                    if (alreadyPinned)
                    {
                        MessageBox.Show($"\"{item.Title}\" ist bereits im App-Hub angeheftet.",
                            "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    string cleanTitle = item.Title.Trim();
                    string shortLabel = cleanTitle.Length >= 2
                        ? cleanTitle[..2].ToUpperInvariant()
                        : cleanTitle.ToUpperInvariant();

                    AppIcons.Add(new AppIconItem
                    {
                        Name = shortLabel,
                        FullName = cleanTitle,
                        ExecutablePath = item.TargetPath
                    });

                    SaveAppHubState();
                    UpdateRunningAppsState();
                }


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

            AppHubLeftItemsControl.ItemsSource = _leftAppIcons;
            AppHubRightItemsControl.ItemsSource = _rightAppIcons;
            RefreshAppHubHalves();
        }

        private void RefreshAppHubHalves()
        {
            _leftAppIcons.Clear();
            _rightAppIcons.Clear();
            var splitIndex = (AppIcons.Count + 1) / 2;
            for (var index = 0; index < AppIcons.Count; index++)
            {
                (index < splitIndex ? _leftAppIcons : _rightAppIcons).Add(AppIcons[index]);
            }
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

        #region Taskbar Zone Actions (Alya, Zone-2 Suche & Monitor)

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            RefreshSearchDropdown();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSearchDropdown();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var firstItem = SearchResultsItemsControl.Items.OfType<SearchResultItem>().FirstOrDefault();
                if (firstItem != null)
                {
                    ExecuteSearchResult(firstItem);
                }
            }
            else if (e.Key == Key.Escape)
            {
                SearchResultsPopup.IsOpen = false;
            }
        }

        private async void AliceSearchButton_Click(object sender, RoutedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                await StartAliceSecurityScanAsync();
                return;
            }

            ExecuteSearchResult(new SearchResultItem
            {
                Icon = "🔍",
                Title = $"Alice fragen: \"{query}\"",
                Subtitle = "Anfrage an Alice / NWOS übergeben",
                Category = "Alice",
                AlicePrompt = query
            });
        }

        private async Task StartAliceSecurityScanAsync()
        {
            SearchResultsPopup.IsOpen = true;
            AliceResponseBorder.Visibility = Visibility.Visible;
            AliceResponseTextBlock.Text = "Alice startet den manuellen Software- und Viren-Scan...";
            AliceActionButtonsControl.ItemsSource = null;
            SearchStatusTextBlock.Text = "Alice-Sicherheits-Scan läuft";

            try
            {
                using var doc = await _cardinal.CallAsync(
                    "nwos.alice",
                    "alice.manualScan",
                    new { },
                    PermissionScope.FileSystemRead);

                AliceResponseTextBlock.Text = doc is null
                    ? $"Alice konnte den Sicherheits-Scan nicht starten: {_cardinal.LastError}"
                    : doc.RootElement.ToString();
                SearchStatusTextBlock.Text = doc is null
                    ? "Alice-Sicherheits-Scan nicht verfügbar"
                    : "Alice-Sicherheits-Scan abgeschlossen";
            }
            catch (Exception ex)
            {
                AliceResponseTextBlock.Text = $"Alice-Scan fehlgeschlagen: {ex.Message}";
                SearchStatusTextBlock.Text = "Alice-Sicherheits-Scan fehlgeschlagen";
            }
        }

        private void RefreshSearchDropdown()
        {
            AliceResponseBorder.Visibility = Visibility.Collapsed;
            AliceActionButtonsControl.ItemsSource = null;

            string query = SearchBox.Text.Trim();
            var items = new List<SearchResultItem>();

            if (!string.IsNullOrWhiteSpace(query))
            {
                items.Add(new SearchResultItem
                {
                    Icon = "🔍",
                    Title = $"Mit Alice suchen: \"{query}\"",
                    Subtitle = "KI-Suche & Systembefehl ausführen",
                    Category = "Alice",
                    AlicePrompt = query
                });

                SearchStatusTextBlock.Text = $"Treffer für \"{query}\"";
            }
            else
            {
                SearchStatusTextBlock.Text = "Schnellzugriffe & Alice";
            }

            var quickActions = new List<SearchResultItem>
            {
                new() { Icon = "📊", Title = "System Monitor", Subtitle = "Auslastung & Prozesse öffnen", Category = "System", TargetPath = "taskmgr.exe" },
                new() { Icon = "⚙️", Title = "Windows-Einstellungen", Subtitle = "Systemkonfiguration öffnen", Category = "System", TargetPath = "ms-settings:" },
                new() { Icon = "📁", Title = "Explorer", Subtitle = "Dateisystem durchsuchen", Category = "App", TargetPath = "explorer.exe" }
            };

            _cachedStartMenuApps ??= LoadStartMenuShortcuts();

            var candidates = quickActions.Concat(_cachedStartMenuApps);
            if (!string.IsNullOrWhiteSpace(query))
            {
                candidates = candidates
                    .Where(x =>
                        x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Title.Length)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase);
            }

            items.AddRange(candidates.Take(8));

            SearchResultsItemsControl.ItemsSource = items;
            SearchResultsPopup.IsOpen = SearchBox.IsFocused || !string.IsNullOrWhiteSpace(query);
        }

        private static void StartShortcutOrTarget(string targetPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                };

                if (File.Exists(targetPath))
                {
                    string? dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        psi.WorkingDirectory = dir;
                }

                Process.Start(psi);
            }
            catch
            {
                // Fallback über explorer.exe (löst UWP-/Shell-Links zuverlässig wie ein Doppelklick auf dem Desktop)
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{targetPath}\"",
                    UseShellExecute = true
                });
            }
        }

        private static List<object> FindStartMenuShortcutActions(string query, int maxResults = 3)
        {
            var actions = new List<object>();
            if (string.IsNullOrWhiteSpace(query)) return actions;

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            var matches = new List<(string Name, string FullPath)>();

            foreach (var folder in folders.Where(Directory.Exists))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(name) ||
                            name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Deinstall", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            matches.Add((name, file));
                        }
                    }
                }
                catch { }
            }

            foreach (var match in matches
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(m => m.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(m => m.Name.Length)
                .Take(maxResults))
            {
                actions.Add(new
                {
                    label = $"🚀 {match.Name} starten",
                    target = "",
                    action = "",
                    payload = "",
                    launchPath = match.FullPath
                });
            }

            return actions;
        }

        private static List<SearchResultItem> LoadStartMenuShortcuts()
        {
            var results = new List<SearchResultItem>();
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            foreach (var rootFolder in folders.Where(Directory.Exists))
            {
                foreach (var file in EnumerateFilesSafe(rootFolder, "*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Deinstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Readme", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Hilfe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string parentFolder = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
                    string subtitle = string.IsNullOrWhiteSpace(parentFolder) ||
                                      parentFolder.Equals("Programs", StringComparison.OrdinalIgnoreCase)
                        ? "Startmenü-Verknüpfung"
                        : $"Startmenü • {parentFolder}";

                    results.Add(new SearchResultItem
                    {
                        Icon = "🚀",
                        Title = name,
                        Subtitle = subtitle,
                        Category = "App",
                        TargetPath = file
                    });
                }
            }

            return results
                .GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> EnumerateFilesSafe(string rootPath, string searchPattern)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                string currentDir = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir, searchPattern);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                    yield return file;

                try
                {
                    foreach (var subDir in Directory.GetDirectories(currentDir))
                        pending.Push(subDir);
                }
                catch
                {
                    // Geschützte Unterordner überspringen
                }
            }
        }

        private void SearchResultItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: SearchResultItem item })
            {
                ExecuteSearchResult(item);
            }
        }

        private async void ExecuteSearchResult(SearchResultItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.AlicePrompt))
            {
                await SendQueryToAliceAsync(item.AlicePrompt);
                return;
            }

            SearchResultsPopup.IsOpen = false;
            AliceResponseBorder.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(item.TargetPath))
            {
                try
                {
                    StartShortcutOrTarget(item.TargetPath);
                    SearchBox.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Konnte \"{item.Title}\" nicht starten:\n{ex.Message}",
                        "NWOS Suche", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async Task SendQueryToAliceAsync(string query)
        {
            SearchResultsPopup.IsOpen = true;
            AliceResponseBorder.Visibility = Visibility.Visible;
            AliceResponseTextBlock.Text = "Alice verarbeitet Anfrage...";
            AliceActionButtonsControl.ItemsSource = null;
            SearchStatusTextBlock.Text = $"Alice-Anfrage: \"{query}\"";

            try
            {
                using var doc = await _cardinal.CallAsync(
                    "nwos.alice",
                    "search",
                    new { query },
                    PermissionScope.FileSystemRead);

                if (doc is null)
                {
                    AliceResponseTextBlock.Text = "Keine Antwort von Alice erhalten.";
                    return;
                }

                ApplyAliceResponseElement(doc.RootElement);
            }
            catch (Exception ex)
            {
                AliceResponseTextBlock.Text = $"IPC-Fehler zu Alice: {ex.Message}";
            }
        }

        private void ApplyAliceResponseElement(JsonElement root)
        {
            // Falls CallAsync das Ergebnis in einem Wrapper-Feld (z. B. "result" oder "response") verpackt hat:
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("result", out var resProp) && resProp.ValueKind == JsonValueKind.String)
                {
                    TryParseStructuredAliceJson(resProp.GetString() ?? string.Empty);
                    return;
                }

                if (root.TryGetProperty("response", out var respProp) && respProp.ValueKind == JsonValueKind.String)
                {
                    TryParseStructuredAliceJson(respProp.GetString() ?? string.Empty);
                    return;
                }
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                TryParseStructuredAliceJson(root.GetString() ?? string.Empty);
                return;
            }

            AliceResponseTextBlock.Text = root.ToString();
        }

        private void TryParseStructuredAliceJson(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                AliceResponseTextBlock.Text = "Keine Antwort von Alice erhalten.";
                return;
            }

            try
            {
                using var innerDoc = JsonDocument.Parse(rawText);
                var obj = innerDoc.RootElement;

                string message = obj.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String
                    ? (msgProp.GetString() ?? rawText)
                    : rawText;

                AliceResponseTextBlock.Text = message;

                var actions = new List<AliceSuggestedAction>();
                if (obj.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var act in actionsProp.EnumerateArray())
                    {
                        actions.Add(new AliceSuggestedAction
                        {
                            Label = act.TryGetProperty("label", out var l) ? (l.GetString() ?? "Aktion") : "Aktion",
                            Target = act.TryGetProperty("target", out var t) ? (t.GetString() ?? string.Empty) : string.Empty,
                            Action = act.TryGetProperty("action", out var a) ? (a.GetString() ?? string.Empty) : string.Empty,
                            Payload = act.TryGetProperty("payload", out var p) ? (p.GetString() ?? "{}") : "{}",
                            LaunchPath = act.TryGetProperty("launchPath", out var lp) ? (lp.GetString() ?? string.Empty) : string.Empty
                        });
                    }
                }

                AliceActionButtonsControl.ItemsSource = actions.Count > 0 ? actions : null;
            }
            catch (JsonException)
            {
                // Reiner Text-Fallback für bestehende Antworten wie "security.quickscan"
                AliceResponseTextBlock.Text = rawText;
                AliceActionButtonsControl.ItemsSource = null;
            }
        }

        private async void AliceActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: AliceSuggestedAction actionItem })
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(actionItem.LaunchPath))
                {
                    StartShortcutOrTarget(actionItem.LaunchPath);
                    SearchResultsPopup.IsOpen = false;
                    SearchBox.Clear();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(actionItem.Target) && !string.IsNullOrWhiteSpace(actionItem.Action))
                {
                    AliceResponseTextBlock.Text = $"Führe \"{actionItem.Label}\" aus...";
                    AliceActionButtonsControl.ItemsSource = null;

                    using var doc = await _cardinal.CallAsync(
                        actionItem.Target,
                        actionItem.Action,
                        new { query = actionItem.Payload },
                        PermissionScope.FileSystemRead);

                    AliceResponseTextBlock.Text = doc != null
                        ? doc.RootElement.ToString()
                        : "Aktion abgeschlossen.";
                }
            }
            catch (Exception ex)
            {
                AliceResponseTextBlock.Text = $"Fehler bei Aktion: {ex.Message}";
            }
        }

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

        private void UpdateConnectionState()
                {
                    Title = _cardinal.LastError is null
                        ? "NWOS System Shell — mit Cardinal verbunden"
                        : $"NWOS System Shell — NICHT verbunden ({_cardinal.LastError})";
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

                private void RemoveAppIcon_Click(object sender, RoutedEventArgs e)
                {
                    if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item && AppIcons.Remove(item))
                        SaveAppHubState();
                }

                private void OpenNewInstance_Click(object sender, RoutedEventArgs e)
                {
                    if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo { FileName = item.ExecutablePath, UseShellExecute = true });
                            UpdateRunningAppsState();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Neues Fenster für '{item.FullName}' konnte nicht geöffnet werden: {ex.Message}",
                                "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }

                private void CloseApp_Click(object sender, RoutedEventArgs e)
                {
                    if (sender is MenuItem menuItem && menuItem.DataContext is AppIconItem item)
                    {
                        string processName = Path.GetFileNameWithoutExtension(item.ExecutablePath);
                        if (string.IsNullOrWhiteSpace(processName)) return;

                        var processes = Process.GetProcessesByName(processName);
                        try
                        {
                            foreach (var p in processes)
                            {
                                if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                                        p.CloseMainWindow();
                                    continue;
                                }
                                if (!p.CloseMainWindow()) p.Kill();
                            }
                            UpdateRunningAppsState();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"'{item.FullName}' konnte nicht beendet werden: {ex.Message}",
                                "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        finally
                        {
                            foreach (var p in processes) p.Dispose();
                        }
                    }
                }

                private void AppIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
                {
                    _dragStartPoint = e.GetPosition(null);
                    if (sender is FrameworkElement element && element.DataContext is AppIconItem item)
                        _draggedItem = item;
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
                    if (e.Data.GetDataPresent(typeof(AppIconItem)))
                    {
                        if (e.Data.GetData(typeof(AppIconItem)) is not AppIconItem droppedItem) return;

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

                    if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
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
                            if (string.IsNullOrWhiteSpace(fileName)) fileName = filePath;
                            string shortLabel = fileName.Length >= 2 ? fileName[..2].ToUpperInvariant() : fileName.ToUpperInvariant();

                            AppIcons.Insert(insertIndex, new AppIconItem
                            {
                                Name = shortLabel,
                                FullName = fileName,
                                ExecutablePath = filePath
                            });
                            insertIndex++;
                            changed = true;
                        }

                        if (changed) SaveAppHubState();
                        e.Handled = true;
                    }
                }

                private void AppIconButton_Click(object sender, RoutedEventArgs e)
                {
                    if (sender is FrameworkElement element && element.DataContext is AppIconItem item)
                    {
                        bool forceNewInstance = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                        if (!forceNewInstance && TryBringProcessToFront(item.ExecutablePath))
                            return;

                        try
                        {
                            Process.Start(new ProcessStartInfo { FileName = item.ExecutablePath, UseShellExecute = true });
                            UpdateRunningAppsState();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"App '{item.Name}' konnte nicht gestartet werden: {ex.Message}",
                                "NWOS App-Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
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
            UpdateClock();

            if (_widgets.Count > 0) _widgetRefreshTimer.Start();

            await RebuildSearchIndexAsync();
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
                    if (doc is not null && doc.RootElement.TryGetProperty("content", out var contentProp))
                    {
                        Debug.WriteLine($"Widget {vm.InstanceId}: {contentProp.GetString()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler beim Aktualisieren der Widgets: {ex.Message}");
            }
            finally
            {
                _refreshingWidgets = false;
            }
        }

        private Task RebuildSearchIndexAsync()
        {
            _cachedStartMenuApps = LoadStartMenuShortcuts();
            _lastIndexCount = _cachedStartMenuApps.Count;
            Debug.WriteLine($"Suchindex aktualisiert: {_lastIndexCount} Startmenü-Einträge.");
            return Task.CompletedTask;
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            TimeTextBlock.Text = now.ToString("HH:mm");
            DateTextBlock.Text = now.ToString("dd.MM.yyyy");
            DateTextBlock.Tag = now.ToString("dddd, dd. MMMM yyyy");
            PopupClockTextBlock.Text = now.ToString("HH:mm:ss");
            PopupFullDateTextBlock.Text = now.ToString("dddd, dd. MMMM yyyy");
        }

        private void ClockPopup_Opened(object? sender, EventArgs e)
        {
            UpdateClock();
            if (TaskbarCalendar.SelectedDate == null)
            {
                TaskbarCalendar.SelectedDate = DateTime.Today;
                TaskbarCalendar.DisplayDate = DateTime.Today;
            }
        }

        private void JumpToToday_Click(object sender, RoutedEventArgs e)
        {
            DateTime today = DateTime.Today;
            TaskbarCalendar.SelectedDate = today;
            TaskbarCalendar.DisplayDate = today;
        }
    }
}
