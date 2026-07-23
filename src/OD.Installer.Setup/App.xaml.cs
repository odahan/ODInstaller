using System.Windows;

namespace OD.Installer.Setup;

/// <summary>
/// Defines application-level behavior for the setup application.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the application and configures global exception logging.
    /// </summary>
    public App()
    {
        InstallerLog.Start();

        // Log exceptions that are not handled by the application.
        DispatcherUnhandledException += (_, eventArgs) =>
            InstallerLog.Error(eventArgs.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            InstallerLog.Error((Exception)eventArgs.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            InstallerLog.Error(eventArgs.Exception);
    }
}
