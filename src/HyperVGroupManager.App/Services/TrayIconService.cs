using System.ComponentModel;
using System.Drawing;
using System.Windows;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.Core.Interfaces;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace HyperVGroupManager.App.Services;

/// <summary>
/// Owns the notification-area icon for the lifetime of the application. Tray support is optional:
/// initialization failures are logged and never prevent the main window from being used.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ILogService _logService;
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ContextMenuStrip? _contextMenu;
    private WinForms.ToolStripMenuItem? _openMenuItem;
    private WinForms.ToolStripMenuItem? _exitMenuItem;
    private Icon? _icon;
    private WpfWindow? _mainWindow;
    private bool _localizationSubscribed;
    private bool _disposed;

    public TrayIconService(ILogService logService)
    {
        _logService = logService;
    }

    public void Initialize(WpfWindow mainWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (_notifyIcon is not null)
        {
            return;
        }

        _mainWindow = mainWindow;

        try
        {
            _icon = LoadIcon();
            _openMenuItem = new WinForms.ToolStripMenuItem();
            _exitMenuItem = new WinForms.ToolStripMenuItem();
            _openMenuItem.Click += OpenMenuItem_Click;
            _exitMenuItem.Click += ExitMenuItem_Click;

            _contextMenu = new WinForms.ContextMenuStrip();
            _contextMenu.Items.Add(_openMenuItem);
            _contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            _contextMenu.Items.Add(_exitMenuItem);

            _notifyIcon = new WinForms.NotifyIcon
            {
                ContextMenuStrip = _contextMenu,
                Icon = _icon,
            };
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            LocalizationService.Instance.PropertyChanged += LocalizationService_PropertyChanged;
            _localizationSubscribed = true;
            UpdateLocalizedText();

            // Set visibility last so a partially initialized icon can never remain in the tray.
            _notifyIcon.Visible = true;
        }
        catch (Exception ex)
        {
            DisposeTrayResources();
            _logService.LogWarning($"Tray-Icon konnte nicht initialisiert werden: {ex.Message}");
        }
    }

    private static Icon LoadIcon()
    {
        var resource = WpfApplication.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("Die eingebettete Tray-Icon-Ressource wurde nicht gefunden.");

        using var stream = resource.Stream;
        using var sourceIcon = new Icon(stream);
        return (Icon)sourceIcon.Clone();
    }

    private void LocalizationService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LocalizationService.CurrentLanguageCode) and not "Item[]")
        {
            return;
        }

        var dispatcher = _mainWindow?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            UpdateLocalizedText();
        }
        else
        {
            dispatcher.BeginInvoke(UpdateLocalizedText);
        }
    }

    private void UpdateLocalizedText()
    {
        if (_notifyIcon is null || _openMenuItem is null || _exitMenuItem is null)
        {
            return;
        }

        _notifyIcon.Text = LimitTooltip(LocalizationService.Instance.Get("Tray.Tooltip"));
        _openMenuItem.Text = LocalizationService.Instance.Get("Tray.Open");
        _exitMenuItem.Text = LocalizationService.Instance.Get("Tray.Exit");
    }

    private static string LimitTooltip(string value) =>
        value.Length <= 63 ? value : value[..63];

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e) => RestoreMainWindow();

    private void OpenMenuItem_Click(object? sender, EventArgs e) => RestoreMainWindow();

    private void ExitMenuItem_Click(object? sender, EventArgs e)
    {
        var window = _mainWindow;
        if (window is null || window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        window.Dispatcher.BeginInvoke(window.Close);
    }

    private void RestoreMainWindow()
    {
        var window = _mainWindow;
        if (window is null || window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.BeginInvoke(RestoreMainWindow);
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeTrayResources();
        GC.SuppressFinalize(this);
    }

    private void DisposeTrayResources()
    {
        if (_localizationSubscribed)
        {
            LocalizationService.Instance.PropertyChanged -= LocalizationService_PropertyChanged;
            _localizationSubscribed = false;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_openMenuItem is not null)
        {
            _openMenuItem.Click -= OpenMenuItem_Click;
            _openMenuItem = null;
        }

        if (_exitMenuItem is not null)
        {
            _exitMenuItem.Click -= ExitMenuItem_Click;
            _exitMenuItem = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;
        _icon?.Dispose();
        _icon = null;
        _mainWindow = null;
    }
}
