using System.Configuration;
using System.Data;
using System.Windows;

namespace OdInstaller.Setup;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        InstallerLog.Start();
        DispatcherUnhandledException += (_, eventArgs) => InstallerLog.Error(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => InstallerLog.Error((Exception)eventArgs.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => InstallerLog.Error(eventArgs.Exception);
    }
}

