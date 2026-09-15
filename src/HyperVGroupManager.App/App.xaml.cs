using System.Windows;
using System.Windows.Threading;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.ViewModels;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HyperVGroupManager.App
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            LocalizationService.Instance.Initialize();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var logService = _serviceProvider.GetRequiredService<ILogService>();
            logService.LogInformation("Anwendung gestartet.");

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var logService = _serviceProvider?.GetService<ILogService>();
            var msg = $"Unbehandelter UI-Fehler: {e.Exception}";
            logService?.LogError(msg);
            TryWriteCrashLog(msg);
            e.Handled = true;
            MessageBox.Show(
                e.Exception.ToString(),
                LocalizationService.Instance.Get("Dialog.UnhandledError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var msg = $"Fataler Fehler: {e.ExceptionObject}";
            TryWriteCrashLog(msg);
        }

        private static void TryWriteCrashLog(string message)
        {
            try
            {
                var directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HyperVGroupManager");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(directory, "crash.log"),
                    $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
                // Ein Fehler im Crash-Logger darf den ursprünglichen Fehler nicht verdecken.
            }
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
            services.AddSingleton<EmailReportService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }
    }
}
