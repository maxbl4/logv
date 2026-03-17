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
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        base.OnExit(e);
        SettingsStore.Save(SettingsStore.Current);
    }
}
