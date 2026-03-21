using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using lgv.Core;

// WinForms ambiguity aliases
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace lgv.UI;

public partial class MainWindow : Window
{
    private AppSettings _settings = SettingsStore.Current;
    private readonly DispatcherTimer _filterDebounce;
    private double _filterPanelHeight = 150;

    // Commands for key bindings
    public ICommand OpenFileCommand { get; }
    public ICommand OpenDirectoryCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ToggleAutoScrollCommand { get; }
    public ICommand ToggleDirWatchCommand { get; }

    public MainWindow()
    {
        OpenFileCommand = new RelayCommand(_ => OpenFileDialog());
        OpenDirectoryCommand = new RelayCommand(_ => OpenDirectoryDialog());
        CloseTabCommand = new RelayCommand(_ => CloseCurrentTab());
        ToggleAutoScrollCommand = new RelayCommand(_ => ToggleAutoScroll());
        ToggleDirWatchCommand = new RelayCommand(_ => ToggleDirWatch());

        _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _filterDebounce.Tick += (_, _) => { _filterDebounce.Stop(); OnFilterChanged(); };

        InitializeComponent();

        WatchDirToggle.IsChecked = _settings.WatchDirectoryEnabled;
        WatchDirToggle.Content = _settings.WatchDirectoryEnabled ? "Watch Dir: ON" : "Watch Dir: OFF";
        ToggleHighlightingMenuItem.IsChecked = _settings.HighlightingEnabled;
        ToggleDirWatchMenuItem.IsChecked = _settings.WatchDirectoryEnabled;

        // Set poll interval combo
        foreach (ComboBoxItem item in PollIntervalCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int ms) && ms == _settings.PollIntervalMs)
            {
                PollIntervalCombo.SelectedItem = item;
                break;
            }
        }

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreSession();
    }

    private void RestoreSession()
    {
        var tabs = _settings.LastOpenTabs;
        if (tabs.Count == 0) return;

        foreach (var tabState in tabs)
        {
            if (!string.IsNullOrEmpty(tabState.DirectoryPath))
            {
                var tabItem = CreateDirectoryTabItem(tabState.DirectoryPath);
                var dirView = (DirectoryTabView)tabItem.Content;
                dirView.StartMonitoring(_settings);
                if (tabState.ChildTabs?.Count > 0)
                    dirView.RestoreChildTabs(tabState.ChildTabs, tabState.ActiveChildTabIndex);
            }
            else if (!string.IsNullOrEmpty(tabState.FilePath))
            {
                if (!File.Exists(tabState.FilePath))
                {
                    TabControl.Items.Add(CreateMissingFileTab(tabState.FilePath));
                    continue;
                }
                var tab = CreateFileTabItem(tabState.FilePath);
                var viewer = GetDirectViewer(tab);
                if (viewer is not null)
                {
                    viewer.LoadFile(tabState.FilePath);
                    viewer.RestoreState(tabState);
                }
            }
        }

        int activeIdx = _settings.LastActiveTabIndex;
        if (activeIdx >= 0 && activeIdx < TabControl.Items.Count)
            TabControl.SelectedIndex = activeIdx;
        else if (TabControl.Items.Count > 0)
            TabControl.SelectedIndex = 0;

        // Restore global filter
        if (!string.IsNullOrEmpty(_settings.GlobalFilterPatterns))
        {
            GlobalFilterBox.Text = _settings.GlobalFilterPatterns;
            // Panel stays hidden; filter is applied silently
            var patterns = ParseFilterPatterns(_settings.GlobalFilterPatterns);
            if (patterns.Length > 0)
                ApplyFilterToAllViewers(patterns);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSession();
        SettingsStore.Save(_settings);
    }

    private void SaveSession()
    {
        _settings.LastOpenTabs.Clear();
        _settings.LastActiveTabIndex = TabControl.SelectedIndex;

        foreach (TabItem tab in TabControl.Items)
        {
            if (tab.Content is DirectoryTabView dirView)
            {
                _settings.LastOpenTabs.Add(new TabState
                {
                    DirectoryPath = dirView.DirectoryPath,
                    WatchNewFiles = true,
                    ChildTabs = dirView.SaveChildStates().ToList(),
                    ActiveChildTabIndex = dirView.ActiveChildTabIndex
                });
            }
            else if (tab.Content is LogViewerControl viewer)
            {
                _settings.LastOpenTabs.Add(viewer.SaveState());
            }
        }
    }

    public void OpenFile(string path)
    {
        var tabItem = CreateFileTabItem(path);
        var viewer = GetDirectViewer(tabItem);
        viewer?.LoadFile(path);
        var patterns = ParseFilterPatterns(_settings.GlobalFilterPatterns);
        if (patterns.Length > 0)
            viewer?.ApplyGlobalFilter(patterns);
        TabControl.SelectedItem = tabItem;
        UpdateStatusForCurrentTab();
    }

    public void OpenDirectory(string path)
    {
        var tabItem = CreateDirectoryTabItem(path);
        var dirView = (DirectoryTabView)tabItem.Content;
        dirView.StartMonitoring(_settings);
        var patterns = ParseFilterPatterns(_settings.GlobalFilterPatterns);
        if (patterns.Length > 0)
            dirView.SetGlobalFilter(patterns);
        TabControl.SelectedItem = tabItem;
        UpdateStatusForCurrentTab();
    }

    // Creates a top-level tab for a directly opened file
    private TabItem CreateFileTabItem(string path)
    {
        var viewer = new LogViewerControl { Settings = _settings };

        viewer.StatusChanged += (_, status) =>
        {
            if (TabControl.SelectedItem is TabItem sel && sel.Content == viewer)
                StatusLineInfo.Text = status;
        };
        viewer.FilePathChanged += (_, filePath) =>
        {
            if (TabControl.SelectedItem is TabItem sel && sel.Content == viewer)
            {
                StatusFilePath.Text = filePath;
                StatusTail.Text = "Tailing...";
            }
        };

        var tabItem = new TabItem { Content = viewer };
        tabItem.Header = BuildTabHeader(Path.GetFileName(path), path, () => CloseTab(tabItem));
        TabControl.Items.Add(tabItem);
        return tabItem;
    }

    // Creates a top-level tab for a monitored directory (contains inner DirectoryTabView)
    private TabItem CreateDirectoryTabItem(string path)
    {
        var dirView = new DirectoryTabView(path);

        dirView.StatusChanged += (_, status) =>
        {
            if (TabControl.SelectedItem is TabItem sel && sel.Content == dirView)
                StatusLineInfo.Text = status;
        };
        dirView.FilePathChanged += (_, filePath) =>
        {
            if (TabControl.SelectedItem is TabItem sel && sel.Content == dirView)
            {
                StatusFilePath.Text = filePath;
                StatusTail.Text = "Tailing...";
            }
        };

        var dirName = Path.GetFileName(path.TrimEnd('/', '\\'));
        var tabItem = new TabItem { Content = dirView };
        var header = BuildTabHeader($"[{dirName}]", path, () => CloseTab(tabItem));
        header.ContextMenu = BuildDirTabContextMenu(dirView, tabItem);
        tabItem.Header = header;
        TabControl.Items.Add(tabItem);
        return tabItem;
    }

    private WpfContextMenu BuildDirTabContextMenu(DirectoryTabView dirView, TabItem tabItem)
    {
        var cm = new WpfContextMenu();
        cm.Items.Add(MakeMenuItem("Close All Files",      () => dirView.CloseAllFileTabs()));
        cm.Items.Add(MakeMenuItem("Close All But Newest", () => dirView.CloseAllButNewest()));
        cm.Items.Add(new Separator());
        cm.Items.Add(MakeMenuItem("Close Directory Tab",  () => CloseTab(tabItem)));
        return cm;
    }

    private static WpfMenuItem MakeMenuItem(string header, Action onClick)
    {
        var item = new WpfMenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private DockPanel BuildTabHeader(string labelText, string toolTip, Action onClose)
    {
        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = toolTip
        };
        var closeBtn = new WpfButton
        {
            Content = "×",
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3),
            FontSize = 14,
            Cursor = WpfCursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, e) => { e.Handled = true; onClose(); };

        var panel = new DockPanel { LastChildFill = false, Background = WpfBrushes.Transparent };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(closeBtn, Dock.Right);
        panel.Children.Add(label);
        panel.Children.Add(closeBtn);
        return panel;
    }

    private TabItem CreateMissingFileTab(string path)
    {
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"File not found:\n{path}",
            Foreground = WpfBrushes.DarkRed,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var retryBtn = new WpfButton
        {
            Content = "Retry",
            Padding = new Thickness(12, 4, 12, 4)
        };
        panel.Children.Add(retryBtn);

        var label = new TextBlock
        {
            Text = Path.GetFileName(path) + " [missing]",
            Foreground = WpfBrushes.DarkRed,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = path
        };

        var closeBtn = new WpfButton
        {
            Content = "×",
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3),
            FontSize = 14,
            Cursor = WpfCursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerPanel = new DockPanel { LastChildFill = false, Background = WpfBrushes.Transparent };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerPanel.Children.Add(label);
        headerPanel.Children.Add(closeBtn);

        var tabItem = new TabItem
        {
            Header = headerPanel,
            Content = panel
        };

        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            TabControl.Items.Remove(tabItem);
            SaveSession();
            SettingsStore.Save(_settings);
        };

        retryBtn.Click += (s, e) =>
        {
            if (File.Exists(path))
            {
                TabControl.Items.Remove(tabItem);
                OpenFile(path);
            }
            else
            {
                WpfMessageBox.Show($"File still not found:\n{path}", "File Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        return tabItem;
    }

    private void CloseTab(TabItem tabItem)
    {
        if (tabItem.Content is DirectoryTabView dirView)
            dirView.Dispose();
        else if (tabItem.Content is LogViewerControl viewer)
            viewer.State.Dispose();

        TabControl.Items.Remove(tabItem);
        SaveSession();
        SettingsStore.Save(_settings);
    }

    private void CloseCurrentTab()
    {
        if (TabControl.SelectedItem is not TabItem tabItem) return;

        // Inside a directory tab: Ctrl+W closes the active file sub-tab
        if (tabItem.Content is DirectoryTabView dirView)
            dirView.CloseCurrentFileTab();
        else
            CloseTab(tabItem);
    }

    // Returns the LogViewerControl directly hosted in a top-level file tab
    private static LogViewerControl? GetDirectViewer(TabItem tabItem) =>
        tabItem.Content as LogViewerControl;

    // Returns the currently visible LogViewerControl regardless of nesting level
    private LogViewerControl? GetCurrentViewer()
    {
        if (TabControl.SelectedItem is not TabItem tabItem) return null;
        if (tabItem.Content is DirectoryTabView dirView) return dirView.GetCurrentViewer();
        return tabItem.Content as LogViewerControl;
    }

    private void UpdateStatusForCurrentTab()
    {
        var viewer = GetCurrentViewer();
        if (viewer is null)
        {
            StatusFilePath.Text = "No file open";
            StatusLineInfo.Text = "";
            StatusTail.Text = "Idle";
            return;
        }
        StatusFilePath.Text = viewer.State.FilePath ?? "No file";
        StatusTail.Text = viewer.State.Tailer is not null ? "Tailing..." : "Idle";
    }

    // ---- Event Handlers ----

    private void MainWindow_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                int idx = TabControl.SelectedIndex - 1;
                if (idx < 0) idx = TabControl.Items.Count - 1;
                if (idx >= 0) TabControl.SelectedIndex = idx;
            }
            else
            {
                if (TabControl.Items.Count > 0)
                {
                    int idx = (TabControl.SelectedIndex + 1) % TabControl.Items.Count;
                    TabControl.SelectedIndex = idx;
                }
            }
        }
        else if (e.Key == Key.F3)
        {
            e.Handled = true;
            var viewer = GetCurrentViewer();
            if (viewer is not null)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    viewer.NavigatePrev();
                else
                    viewer.NavigateNext();
            }
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            GetCurrentViewer()?.ToggleSearch();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            ToggleGlobalFilter();
        }
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            GetCurrentViewer()?.ShowGoToLineDialog();
        }
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            CloseCurrentTab();
        }
        else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleAutoScroll();
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleDirWatch();
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenFileDialog();
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OpenDirectoryDialog();
        }
    }

    private void OpenFileMenu_Click(object sender, RoutedEventArgs e) => OpenFileDialog();
    private void OpenDirectoryMenu_Click(object sender, RoutedEventArgs e) => OpenDirectoryDialog();
    private void CloseTabMenu_Click(object sender, RoutedEventArgs e) => CloseCurrentTab();
    private void ExitMenu_Click(object sender, RoutedEventArgs e) => Close();
    private void GoToLineMenu_Click(object sender, RoutedEventArgs e) =>
        GetCurrentViewer()?.ShowGoToLineDialog();
    private void ReloadFileMenu_Click(object sender, RoutedEventArgs e) =>
        GetCurrentViewer()?.ReloadFile();
    private void CloseAllFilesGlobal_Click(object sender, RoutedEventArgs e)
    {
        foreach (TabItem tab in TabControl.Items)
            if (tab.Content is DirectoryTabView dirView)
                dirView.CloseAllFileTabs();
    }
    private void CloseAllButNewestGlobal_Click(object sender, RoutedEventArgs e)
    {
        foreach (TabItem tab in TabControl.Items)
            if (tab.Content is DirectoryTabView dirView)
                dirView.CloseAllButNewest();
    }
    private void FindMenu_Click(object sender, RoutedEventArgs e) =>
        GetCurrentViewer()?.ToggleSearch();
    private void FilterMenu_Click(object sender, RoutedEventArgs e) =>
        ToggleGlobalFilter();
    private void NextMatchMenu_Click(object sender, RoutedEventArgs e) =>
        GetCurrentViewer()?.NavigateNext();
    private void PrevMatchMenu_Click(object sender, RoutedEventArgs e) =>
        GetCurrentViewer()?.NavigatePrev();
    private void ToggleDirWatchMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.WatchDirectoryEnabled = ToggleDirWatchMenuItem.IsChecked;
        WatchDirToggle.IsChecked = ToggleDirWatchMenuItem.IsChecked;
        WatchDirToggle.Content = ToggleDirWatchMenuItem.IsChecked ? "Watch Dir: ON" : "Watch Dir: OFF";
    }

    private void OpenFileDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Log File",
            Filter = "Log files|*.log;*.txt;*.json;*.csv|All files|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
            OpenFile(dlg.FileName);
    }

    private void OpenDirectoryDialog()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select log directory to monitor",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OpenDirectory(dlg.SelectedPath);
    }

    private void ToggleHighlighting_Click(object sender, RoutedEventArgs e)
    {
        _settings.HighlightingEnabled = ToggleHighlightingMenuItem.IsChecked;
        foreach (TabItem tab in TabControl.Items)
        {
            if (tab.Content is DirectoryTabView dirView)
                dirView.RefreshAllColorizers();
            else
                GetDirectViewer(tab)?.RefreshColorizer();
        }
    }

    private void ToggleAutoScroll_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ToggleAutoScrollMenuItem.IsChecked;
        AutoScrollToggle.IsChecked = enabled;
        GetCurrentViewer()?.SetAutoScroll(enabled);
    }

    private void AutoScrollToggle_Checked(object sender, RoutedEventArgs e)
    {
        AutoScrollToggle.Content = "AutoScroll: ON";
        ToggleAutoScrollMenuItem.IsChecked = true;
        GetCurrentViewer()?.SetAutoScroll(true);
    }

    private void AutoScrollToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        AutoScrollToggle.Content = "AutoScroll: OFF";
        ToggleAutoScrollMenuItem.IsChecked = false;
        GetCurrentViewer()?.SetAutoScroll(false);
    }

    private void WatchDirToggle_Checked(object sender, RoutedEventArgs e)
    {
        WatchDirToggle.Content = "Watch Dir: ON";
        _settings.WatchDirectoryEnabled = true;
        ToggleDirWatchMenuItem.IsChecked = true;
    }

    private void WatchDirToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        WatchDirToggle.Content = "Watch Dir: OFF";
        _settings.WatchDirectoryEnabled = false;
        ToggleDirWatchMenuItem.IsChecked = false;
    }

    private void PollIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PollIntervalCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out int ms))
        {
            _settings.PollIntervalMs = ms;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatusForCurrentTab();
    }

    private void ToggleAutoScroll()
    {
        AutoScrollToggle.IsChecked = !(AutoScrollToggle.IsChecked ?? false);
    }

    private void ToggleDirWatch()
    {
        WatchDirToggle.IsChecked = !(WatchDirToggle.IsChecked ?? false);
    }

    // ---- Global Filter ----

    private static string[] ParseFilterPatterns(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToArray();

    private void ToggleGlobalFilter()
    {
        if (GlobalFilterPanel.Visibility == Visibility.Visible)
            HideGlobalFilterPanel();
        else
            ShowGlobalFilterPanel();
    }

    private void ShowGlobalFilterPanel()
    {
        GlobalFilterSplitterRow.Height = new GridLength(5);
        GlobalFilterPanelRow.Height = new GridLength(_filterPanelHeight);
        GlobalFilterSplitter.Visibility = Visibility.Visible;
        GlobalFilterPanel.Visibility = Visibility.Visible;
        GlobalFilterBox.Focus();
    }

    private void HideGlobalFilterPanel()
    {
        if (GlobalFilterPanelRow.ActualHeight > 10)
            _filterPanelHeight = GlobalFilterPanelRow.ActualHeight;
        GlobalFilterSplitterRow.Height = new GridLength(0);
        GlobalFilterPanelRow.Height = new GridLength(0);
        GlobalFilterSplitter.Visibility = Visibility.Collapsed;
        GlobalFilterPanel.Visibility = Visibility.Collapsed;
    }

    private void OnFilterChanged()
    {
        var text = GlobalFilterBox.Text;
        _settings.GlobalFilterPatterns = text;
        SettingsStore.Save(_settings);

        var patterns = ParseFilterPatterns(text);
        ApplyFilterToAllViewers(patterns);

        string info = patterns.Length == 0
            ? ""
            : $"{patterns.Length} pattern{(patterns.Length == 1 ? "" : "s")} active";
        GlobalFilterStatusLabel.Content = info;
    }

    private void ApplyFilterToAllViewers(string[] patterns)
    {
        foreach (TabItem tab in TabControl.Items)
        {
            if (tab.Content is DirectoryTabView dirView)
                dirView.SetGlobalFilter(patterns);
            else if (tab.Content is LogViewerControl viewer)
                viewer.ApplyGlobalFilter(patterns);
        }
    }

    private void GlobalFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void GlobalFilterBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideGlobalFilterPanel();
            e.Handled = true;
        }
    }

    private void ShowFilterPanelBtn_Click(object sender, RoutedEventArgs e) =>
        ShowGlobalFilterPanel();

    private void GlobalFilterCloseBtn_Click(object sender, RoutedEventArgs e) =>
        HideGlobalFilterPanel();

    private void GlobalFilterClearBtn_Click(object sender, RoutedEventArgs e)
    {
        GlobalFilterBox.Text = "";
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                OpenDirectory(path);
            else if (File.Exists(path))
                OpenFile(path);
        }
    }
}

// Simple relay command implementation
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
