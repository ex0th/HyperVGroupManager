using System.Windows;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.ViewModels;
using HyperVGroupManager.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HyperVGroupManager.App
{
    /// <summary>
    /// Interaction logic for App.xaml. Enthält die Dependency-Injection-Konfiguration
    /// (Composition Root) - keine Business-Logik, kein Service-Locator-Pattern.
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var logService = _serviceProvider.GetRequiredService<ILogService>();
            logService.LogInformation("Anwendung gestartet.");

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.GetService<ILogService>()?.LogInformation("Anwendung beendet.");
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            var settings = AppSettingsLoader.Load(AppContext.BaseDirectory);

            services.AddSingleton(settings.PowerShell);
            services.AddSingleton(settings.Application);

            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddDebug();
                builder.AddProvider(new FileLoggerProvider());
            });

            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<IPowerShellExecutor, PowerShellExecutor>();
            services.AddSingleton<IHyperVGroupService, HyperVGroupService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }
    }
}
