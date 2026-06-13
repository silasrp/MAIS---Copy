using System.IO;
using System.Windows;
using System.Windows.Forms;
using MAIS.Sidebar.Views;
using Microsoft.Extensions.Logging;

namespace MAIS.Sidebar.Services;

/// <summary>
/// Manages the MAIS system tray icon. Provides show/hide controls for the sidebar
/// and a clean-exit option without needing the sidebar to be visible.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private readonly NotifyIcon _trayIcon;
    private SidebarWindow? _sidebarWindow;
    private bool _disposed;

    public SystemTrayService(ILogger<SystemTrayService> logger)
    {
        _logger = logger;
        _trayIcon = BuildTrayIcon();
    }

    public void Initialise(SidebarWindow window)
    {
        _sidebarWindow = window;
        _trayIcon.Visible = true;
        _logger.LogInformation("System tray icon initialised");
    }

    // ── Tray icon construction ────────────────────────────────────────────

    private NotifyIcon BuildTrayIcon()
    {
        var icon = new NotifyIcon
        {
            Text = "MAIS — Multi Agentic Intelligent Suite",
            Icon = LoadIcon(),
            Visible = false
        };

        icon.DoubleClick  += (_, _) => ShowSidebar();
        icon.ContextMenuStrip = BuildContextMenu();

        return icon;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Show sidebar");
        showItem.Click += (_, _) => ShowSidebar();
        showItem.Font = new System.Drawing.Font(showItem.Font, System.Drawing.FontStyle.Bold);

        var hideItem = new ToolStripMenuItem("Hide sidebar");
        hideItem.Click += (_, _) => HideSidebar();

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("Exit MAIS");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.AddRange([showItem, hideItem, separator, exitItem]);
        return menu;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        // Load from embedded resource or file
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "mais.ico");
        return File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.SystemIcons.Application;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private void ShowSidebar()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _sidebarWindow?.ShowSidebar());
    }

    private void HideSidebar()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _sidebarWindow?.HideSidebar());
    }

    private static void ExitApplication()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
