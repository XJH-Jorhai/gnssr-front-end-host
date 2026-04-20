using System.IO;
using System.Windows;
using System.Windows.Threading;
using GNSSR.Host.Core.Services;
using GNSSR.Host.Infrastructure.Logging.Services;
using GNSSR.Host.Infrastructure.Serial.Services;
using GNSSR.Host.Infrastructure.Storage.Services;
using GNSSR.Host.Infrastructure.USB.Services;
using GNSSR.Host.UI.ViewModels;

namespace GNSSR.Host.UI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var logger = new InMemoryLogService();
            var fileNamingPolicy = new FileNamingPolicy();
            var fx3UsbService = new MockFx3UsbService(logger);
            var frontendSerialService = new MockFrontendSerialService(logger);
            var captureSessionService = new MockCaptureSessionService(fileNamingPolicy, logger);

            var viewModel = new MainViewModel(
                fx3UsbService,
                frontendSerialService,
                captureSessionService,
                logger);

            var mainWindow = new MainWindow(viewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            ShowFatalError("Startup failure", exception);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError("Unhandled UI exception", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ShowFatalError("Unhandled application exception", exception);
            return;
        }

        ShowFatalError("Unhandled application exception", new InvalidOperationException("Unknown fatal error."));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ShowFatalError("Background task exception", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalError(string title, Exception exception)
    {
        try
        {
            var message =
                $"{title}\n\n{exception.GetType().FullName}\n{exception.Message}\n\n{exception.StackTrace}";

            var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            File.WriteAllText(logPath, $"{DateTimeOffset.Now:u}\n{message}");

            MessageBox.Show(
                $"{message}\n\nA copy was written to:\n{logPath}",
                "GNSSR Host Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
