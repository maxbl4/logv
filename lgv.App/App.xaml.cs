using lgv.Core;

namespace lgv;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show(
                $"Unhandled error: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "LGV Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Ensure settings are loaded and seeded
        _ = SettingsStore.Current;

        // Open file passed as CLI argument (used by UI tests)
        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0]))
        {
            string filePath = e.Args[0];
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                if (MainWindow is lgv.UI.MainWindow mw)
                    mw.OpenFile(filePath);
            });
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        base.OnExit(e);
        SettingsStore.Save(SettingsStore.Current);
    }
}
