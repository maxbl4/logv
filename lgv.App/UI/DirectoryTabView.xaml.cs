using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using lgv.Core;

using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;

namespace lgv.UI;

public partial class DirectoryTabView : System.Windows.Controls.UserControl, IDisposable
{
    public string DirectoryPath { get; }
    private DirectoryMonitor? _monitor;
    private AppSettings _settings = new();
    private string[] _globalFilterPatterns = [];
    private bool _disposed;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? FilePathChanged;

    public int ActiveChildTabIndex => FileTabControl.SelectedIndex;

    public DirectoryTabView(string directoryPath)
    {
        DirectoryPath = directoryPath;
        InitializeComponent();
    }

    public void StartMonitoring(AppSettings settings)
    {
        _settings = settings;
        _monitor = new DirectoryMonitor(DirectoryPath);

        _monitor.NewFileDetected += (_, path) =>
            Dispatcher.InvokeAsync(() => BlinkTabHeader(AddFileTab(path)));

        _monitor.FileRenamed += (_, e) =>
            Dispatcher.InvokeAsync(() =>
            {
                CloseFileTab(e.OldPath);
                BlinkTabHeader(AddFileTab(e.NewPath));
            });

        _monitor.Start();

        var newestFile = _monitor.GetNewestFile();
        if (newestFile is not null)
            FileTabControl.SelectedItem = AddFileTab(newestFile);
    }

    public TabItem AddFileTab(string filePath)
    {
        var viewer = new LogViewerControl { Settings = _settings };

        viewer.StatusChanged += (_, status) =>
        {
            if (FileTabControl.SelectedItem is TabItem sel && GetViewer(sel) == viewer)
                StatusChanged?.Invoke(this, status);
        };
        viewer.FilePathChanged += (_, path) =>
        {
            if (FileTabControl.SelectedItem is TabItem sel && GetViewer(sel) == viewer)
                FilePathChanged?.Invoke(this, path);
        };

        var label = new TextBlock
        {
            Text = Path.GetFileName(filePath),
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xDC, 0xDC, 0xDC)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = filePath
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

        var tab = new TabItem { Header = headerPanel, Content = viewer };

        closeBtn.Click += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };

        FileTabControl.Items.Add(tab);
        viewer.LoadFile(filePath);
        if (_globalFilterPatterns.Length > 0)
            viewer.ApplyGlobalFilter(_globalFilterPatterns);
        return tab;
    }

    public void RestoreChildTabs(List<TabState> states, int activeIndex)
    {
        foreach (var state in states)
        {
            if (string.IsNullOrEmpty(state.FilePath) || !File.Exists(state.FilePath))
                continue;
            var tab = AddFileTab(state.FilePath);
            var viewer = GetViewer(tab);
            viewer?.RestoreState(state);
        }

        if (activeIndex >= 0 && activeIndex < FileTabControl.Items.Count)
            FileTabControl.SelectedIndex = activeIndex;
        else if (FileTabControl.Items.Count > 0)
            FileTabControl.SelectedIndex = 0;
    }

    public IEnumerable<TabState> SaveChildStates()
    {
        foreach (TabItem tab in FileTabControl.Items)
        {
            var viewer = GetViewer(tab);
            if (viewer is not null)
                yield return viewer.SaveState();
        }
    }

    public void CloseFileTab(string filePath)
    {
        foreach (TabItem tab in FileTabControl.Items)
        {
            var viewer = GetViewer(tab);
            if (viewer is not null &&
                string.Equals(viewer.State.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                CloseTab(tab);
                return;
            }
        }
    }

    public void CloseCurrentFileTab()
    {
        if (FileTabControl.SelectedItem is TabItem tab)
            CloseTab(tab);
    }

    public void CloseAllFileTabs()
    {
        foreach (var tab in FileTabControl.Items.Cast<TabItem>().ToList())
            CloseTab(tab);
    }

    public void CloseAllButNewest()
    {
        var tabs = FileTabControl.Items.Cast<TabItem>().ToList();
        foreach (var tab in tabs.SkipLast(1))
            CloseTab(tab);
    }

    public LogViewerControl? GetCurrentViewer()
    {
        if (FileTabControl.SelectedItem is TabItem tab)
            return GetViewer(tab);
        return null;
    }

    public void SetGlobalFilter(string[] patterns)
    {
        _globalFilterPatterns = patterns;
        foreach (TabItem tab in FileTabControl.Items)
            GetViewer(tab)?.ApplyGlobalFilter(patterns);
    }

    public void RefreshAllColorizers()
    {
        foreach (TabItem tab in FileTabControl.Items)
            GetViewer(tab)?.RefreshColorizer();
    }

    private void CloseTab(TabItem tab)
    {
        GetViewer(tab)?.State.Dispose();
        FileTabControl.Items.Remove(tab);
    }

    private void BlinkTabHeader(TabItem tab)
    {
        if (tab.Header is not DockPanel headerPanel) return;

        var accentColor = WpfColor.FromRgb(0x3A, 0x5A, 0x8A);
        var anim = new ColorAnimation
        {
            From = accentColor,
            To = WpfColors.Transparent,
            Duration = new Duration(TimeSpan.FromSeconds(1.2)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            RepeatBehavior = new RepeatBehavior(3)
        };

        var brush = new WpfSolidColorBrush(accentColor);
        headerPanel.Background = brush;
        brush.BeginAnimation(WpfSolidColorBrush.ColorProperty, anim);
    }

    private void FileTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var viewer = GetCurrentViewer();
        if (viewer?.State.FilePath is string path)
            FilePathChanged?.Invoke(this, path);
    }

    private static LogViewerControl? GetViewer(TabItem tab) => tab.Content as LogViewerControl;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor?.Dispose();
        foreach (TabItem tab in FileTabControl.Items)
            GetViewer(tab)?.State.Dispose();
    }
}
