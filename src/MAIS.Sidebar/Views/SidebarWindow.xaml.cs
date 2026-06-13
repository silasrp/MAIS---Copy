using System.Windows;
using System.Windows.Interop;
using MAIS.Sidebar.Infrastructure.Win32;
using MAIS.Sidebar.ViewModels;

namespace MAIS.Sidebar.Views;

/// <summary>
/// Code-behind for the MAIS sidebar window.
/// Owns the AppBar registration and delegates all UI logic to <see cref="SidebarViewModel"/>.
/// </summary>
public partial class SidebarWindow : Window
{
    private readonly SidebarViewModel _viewModel;

    public SidebarWindow(SidebarViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        // Wire window events
        SourceInitialized += OnSourceInitialised;
        Loaded             += OnLoaded;
        Closing            += OnClosing;
    }

    // ── Window lifecycle ──────────────────────────────────────────────────

    private void OnSourceInitialised(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Window handle not available.");

        // Make it a tool window (removes from Alt+Tab and taskbar)
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);

        // Hook WndProc to keep window responsive and handle screen changes
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        PositionWindowOnEdge(hwnd);
    }

    private void PositionWindowOnEdge(IntPtr hwnd)
    {
        // Get the monitor where the window is currently displayed
        IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            // Fallback to SystemParameters if we can't get monitor info
            var workArea = SystemParameters.WorkArea;
            var dpiScale = GetDpiScale();
            int widthPx = (int)(Width * dpiScale);
            int heightPx = (int)((workArea.Bottom - workArea.Top) * dpiScale);
            int posX = (int)(workArea.Right - widthPx);
            int posY = (int)workArea.Top;

            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, posX, posY, widthPx, heightPx, NativeMethods.SWP_NOACTIVATE);
            return;
        }

        // Use the actual monitor's working area
        var workRect = monitorInfo.rcWork;
        var dpiScaleActual = GetDpiScale();
        int widthPixels = (int)(Width * dpiScaleActual);
        int heightPixels = (int)((workRect.bottom - workRect.top) * dpiScaleActual);

        // Position at RIGHT edge of this monitor
        int posXActual = (int)(workRect.right - widthPixels);
        int posYActual = (int)workRect.top;

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            posXActual, posYActual,
            widthPixels, heightPixels,
            NativeMethods.SWP_NOACTIVATE);
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Re-position if screen resolution or working area changes
        if (msg == NativeMethods.WM_SETTINGCHANGE || msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            PositionWindowOnEdge(hwnd);
        }
        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Fire-and-forget initialization so window shows immediately
        // Don't await — let it run in background
        _ = _viewModel.InitialiseAsync();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();

        // Allow time for async cleanup if needed
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch { }
    }

    // ── Header button handlers ────────────────────────────────────────────

    private void OnMinimiseToTray(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // ── Public API (called by SystemTrayService) ──────────────────────────

    public void ShowSidebar()
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            PositionWindowOnEdge(hwnd);
        }
        Activate();
    }

    public void HideSidebar() => Hide();
}
