using System.Windows;
using GNSSR.Host.Core.Services;
using GNSSR.Host.Infrastructure.Logging.Services;
using GNSSR.Host.Infrastructure.Serial.Services;
using GNSSR.Host.Infrastructure.Storage.Services;
using GNSSR.Host.Infrastructure.USB.Services;
using GNSSR.Host.UI.ViewModels;

namespace GNSSR.Host.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
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
}
