using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

namespace lgv.UI;

public partial class MainWindow : Window
{
    private AppSettings _settings = SettingsStore.Current;

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

        InitializeComponent();

        WatchDirToggle.IsChecked = _settings.WatchDirectoryEnabled;
        WatchDirToggle.Content = _settings.WatchDirectoryEnabled ? "Watch Dir: ON" : "Watch Dir: OFF";
        ToggleHighlightingMenuItem.IsChecked = _settings.HighlightingEnabled;

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
                var tabItem = CreateTabItem(tabState.DirectoryPath, isDirectory: true);
                var viewer = GetViewer(tabItem);
                if (viewer is not null)
                {
                    viewer.LoadDirectory(tabState.DirectoryPath);
                    viewer.RestoreState(tabState);
                }
            }
            else if (!string.IsNullOrEmpty(tabState.FilePath))
            {
                if (!File.Exists(tabState.FilePath))
                {
                    var tabItem = CreateMissingFileTab(tabState.FilePath);
                    TabControl.Items.Add(tabItem);
                    continue;
                }
                var tab = CreateTabItem(tabState.FilePath, isDirectory: false);
                var viewer = GetViewer(tab);
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
            var viewer = GetViewer(tab);
            if (viewer is not null)
            {
                var tabState = viewer.SaveState();
                _settings.LastOpenTabs.Add(tabState);
            }
        }
    }

    public void OpenFile(string path)
    {
        var tabItem = CreateTabItem(path, isDirectory: false);
        var viewer = GetViewer(tabItem);
        viewer?.LoadFile(path);
        TabControl.SelectedItem = tabItem;
        UpdateStatusForCurrentTab();
    }

    public void OpenDirectory(string path)
    {
        var tabItem = CreateTabItem(path, isDirectory: true);
        var viewer = GetViewer(tabItem);
        viewer?.LoadDirectory(path);

        // Wire up directory monitor for new files
        if (viewer is not null && _settings.WatchDirectoryEnabled)
        {
            var monitor = viewer.State.Monitor;
            if (monitor is not null)
            {
                monitor.NewFileDetected += (s, newFilePath) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        OpenFileInBackground(newFilePath);
                    });
                };
            }
        }

        TabControl.SelectedItem = tabItem;
        UpdateStatusForCurrentTab();
    }

    private void OpenFileInBackground(string path)
    {
        var tabItem = CreateTabItem(path, isDirectory: false);
        var viewer = GetViewer(tabItem);
        viewer?.LoadFile(path);

        // Don't switch to the new tab — blink it
        BlinkTabHeader(tabItem);
    }

    private void BlinkTabHeader(TabItem tabItem)
    {
        if (tabItem.Header is DockPanel headerPanel)
        {
            var accentColor = WpfColor.FromRgb(0x3A, 0x5A, 0x8A);
            var transparentColor = WpfColors.Transparent;

            var anim = new ColorAnimation
            {
                From = accentColor,
                To = transparentColor,
                Duration = new Duration(TimeSpan.FromSeconds(1.2)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                RepeatBehavior = new RepeatBehavior(3),
                AutoReverse = false
            };

            var brush = new WpfSolidColorBrush(accentColor);
            headerPanel.Background = brush;
            brush.BeginAnimation(WpfSolidColorBrush.ColorProperty, anim);
        }
    }

    private TabItem CreateTabItem(string path, bool isDirectory)
    {
        var viewer = new LogViewerControl
        {
            Settings = _settings
        };

        viewer.StatusChanged += (s, status) =>
        {
            if (TabControl.SelectedItem is TabItem selectedTab &&
                GetViewer(selectedTab) == viewer)
            {
                StatusLineInfo.Text = status;
            }
        };

        viewer.FilePathChanged += (s, filePath) =>
        {
            if (TabControl.SelectedItem is TabItem selectedTab &&
                GetViewer(selectedTab) == viewer)
            {
                StatusFilePath.Text = filePath;
                StatusTail.Text = "Tailing...";
            }
        };

        var label = new TextBlock
        {
            Text = isDirectory
                ? $"[Dir] {Path.GetFileName(path.TrimEnd('/', '\\'))}"
                : Path.GetFileName(path),
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xDC, 0xDC, 0xDC)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = path
        };

        var closeBtn = new WpfButton
        {
            Content = "×",
            Background = WpfBrushes.Transparent,
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xAA, 0xAA, 0xAA)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3),
            FontSize = 14,
            Cursor = WpfCursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerPanel = new DockPanel
        {
            LastChildFill = false,
            Background = WpfBrushes.Transparent
        };

        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(closeBtn, Dock.Right);
        headerPanel.Children.Add(label);
        headerPanel.Children.Add(closeBtn);

        var tabItem = new TabItem
        {
            Header = headerPanel,
            Content = viewer,
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xAA, 0xAA, 0xAA)),
            BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0x3F, 0x3F, 0x3F))
        };

        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            CloseTab(tabItem);
        };

        TabControl.Items.Add(tabItem);
        return tabItem;
    }

    private TabItem CreateMissingFileTab(string path)
    {
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x1E))
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"File not found:\n{path}",
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xFF, 0x6B, 0x6B)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var retryBtn = new WpfButton
        {
            Content = "Retry",
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x3C, 0x3C, 0x3C)),
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xDC, 0xDC, 0xDC)),
            BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0x55, 0x55, 0x55)),
            Padding = new Thickness(12, 4, 12, 4)
        };
        panel.Children.Add(retryBtn);

        var label = new TextBlock
        {
            Text = Path.GetFileName(path) + " [missing]",
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xFF, 0x6B, 0x6B)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = path
        };

        var closeBtn = new WpfButton
        {
            Content = "×",
            Background = WpfBrushes.Transparent,
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xAA, 0xAA, 0xAA)),
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
            Content = panel,
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xFF, 0x6B, 0x6B))
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
        var viewer = GetViewer(tabItem);
        viewer?.State.Dispose();
        TabControl.Items.Remove(tabItem);
        SaveSession();
        SettingsStore.Save(_settings);
    }

    private void CloseCurrentTab()
    {
        if (TabControl.SelectedItem is TabItem tabItem)
            CloseTab(tabItem);
    }

    private static LogViewerControl? GetViewer(TabItem tabItem) =>
        tabItem.Content as LogViewerControl;

    private LogViewerControl? GetCurrentViewer()
    {
        if (TabControl.SelectedItem is TabItem tabItem)
            return GetViewer(tabItem);
        return null;
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

        var state = viewer.State;
        StatusFilePath.Text = state.FilePath ?? state.DirectoryPath ?? "No file";
        StatusTail.Text = state.Tailer is not null ? "Tailing..." : "Idle";
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
            GetCurrentViewer()?.ToggleFilter();
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
            GetViewer(tab)?.RefreshColorizer();
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
    }

    private void WatchDirToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        WatchDirToggle.Content = "Watch Dir: OFF";
        _settings.WatchDirectoryEnabled = false;
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
