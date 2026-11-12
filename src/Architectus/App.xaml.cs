using Architectus.Services;
using Microsoft.UI.Xaml;

namespace Architectus;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the app alive on a stray error and show it instead of crashing.
            e.Handled = true;
            Window?.ShowError(e.Exception?.Message ?? "Something went wrong.");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Used by the uninstaller when the user chooses to remove saved data.
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Any(a => a.Equals("--purge", StringComparison.OrdinalIgnoreCase)))
        {
            CredentialStore.DeleteAll();
            Exit();
            return;
        }

        AppPaths.Ensure();
        Window = new MainWindow();
        Window.Activate();
    }
}
