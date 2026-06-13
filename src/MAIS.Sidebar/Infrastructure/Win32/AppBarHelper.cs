using System.Windows;
using System.Windows.Interop;

namespace MAIS.Sidebar.Infrastructure.Win32;

/// <summary>
/// Manages registration of the MAIS sidebar as a Windows AppBar.
/// When registered, Windows reserves screen real estate on the chosen edge and
/// prevents other maximised windows from overlapping the sidebar — the same
/// mechanism used by the taskbar and classic third-party toolbars.
/// </summary>
public sealed class AppBarHelper : IDisposable
{
    private readonly Window _window;
    private readonly double _sidebarWidth;
    private IntPtr _hwnd;
    private bool _registered;
    private bool _disposed;

    private uint _edge;

    /// <summary>The screen edge the sidebar is docked to.</summary>
    public SidebarEdge Edge { get; private set; }

    public AppBarHelper(Window window, double sidebarWidth, SidebarEdge edge = SidebarEdge.Right)
    {
        _window = window;
        _sidebarWidth = sidebarWidth;
        Edge = edge;
        _edge = edge == SidebarEdge.Left ? NativeMethods.ABE_LEFT : NativeMethods.ABE_RIGHT;
    }

    /// <summary>
    /// Registers the window as an AppBar and positions it on the chosen screen edge.
    /// Must be called after the window's handle has been created (after <c>SourceInitialized</c>).
    /// </summary>
    public void Register()
    {
        if (_registered) return;

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                "Window handle not available. Call Register() after SourceInitialized.");

        // Hook WndProc so we can handle AppBar callback messages
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        var data = BuildAppBarData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data);
        _registered = true;

        PositionOnEdge();
    }

    /// <summary>Unregisters the AppBar, restoring the working area to its original size.</summary>
    public void Unregister()
    {
        if (!_registered) return;

        var data = BuildAppBarData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
        _registered = false;
    }

    /// <summary>
    /// Repositions the sidebar on the screen (call when the window position changes,
    /// e.g. when the user switches to a different monitor).
    /// </summary>
    public void PositionOnEdge()
    {
        if (!_registered) return;

        var screen = GetCurrentScreen();
        var dpiScale = GetDpiScale();
        var widthPx = (int)(_sidebarWidth * dpiScale);

        // Step 1: propose the position to Windows
        var data = BuildAppBarData();
        data.uEdge = _edge;
        data.rc = _edge == NativeMethods.ABE_RIGHT
            ? new NativeMethods.RECT
            {
                left   = screen.Right - widthPx,
                top    = screen.Top,
                right  = screen.Right,
                bottom = screen.Bottom
            }
            : new NativeMethods.RECT
            {
                left   = screen.Left,
                top    = screen.Top,
                right  = screen.Left + widthPx,
                bottom = screen.Bottom
            };

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);

        // Step 2: finalise position (Windows may adjust it)
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);

        // Step 3: move and size the actual window
        _window.Dispatcher.Invoke(() =>
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                data.rc.left, data.rc.top,
                data.rc.right - data.rc.left,
                data.rc.bottom - data.rc.top,
                NativeMethods.SWP_NOACTIVATE);
        });
    }

    // ── WndProc hook ────────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_APPBAR_CALLBACK)
        {
            PositionOnEdge(); // Re-dock when the work area changes
        }
        else if (msg == NativeMethods.WM_ACTIVATE && _registered)
        {
            var data = BuildAppBarData();
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_ACTIVATE, ref data);
        }
        else if (msg == NativeMethods.WM_WINDOWPOSCHANGED && _registered)
        {
            var data = BuildAppBarData();
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_WINDOWPOSCHANGED, ref data);
        }

        return IntPtr.Zero;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private NativeMethods.APPBARDATA BuildAppBarData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = NativeMethods.WM_APPBAR_CALLBACK
    };

    private System.Drawing.Rectangle GetCurrentScreen()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(_hwnd);
        return screen.WorkingArea; // Already excludes the taskbar
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(_window);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}

public enum SidebarEdge { Left, Right }
