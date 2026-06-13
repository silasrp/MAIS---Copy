using System.Runtime.InteropServices;

namespace MAIS.Sidebar.Infrastructure.Win32;

/// <summary>
/// P/Invoke declarations for Windows APIs used by the MAIS sidebar.
/// All native types and constants are defined here; helper logic lives in <see cref="AppBarHelper"/>.
/// </summary>
internal static class NativeMethods
{
    // ── AppBar messages ────────────────────────────────────────────────────

    public const uint ABM_NEW            = 0x00000000;
    public const uint ABM_REMOVE         = 0x00000001;
    public const uint ABM_QUERYPOS       = 0x00000002;
    public const uint ABM_SETPOS         = 0x00000003;
    public const uint ABM_GETSTATE       = 0x00000004;
    public const uint ABM_WINDOWPOSCHANGED = 0x00000009;
    public const uint ABM_ACTIVATE       = 0x00000006;

    // ── AppBar edges ───────────────────────────────────────────────────────

    public const uint ABE_LEFT           = 0;
    public const uint ABE_TOP            = 1;
    public const uint ABE_RIGHT          = 2;
    public const uint ABE_BOTTOM         = 3;

    // ── AppBar notification message sent to window ─────────────────────────

    public const int WM_COPYDATA         = 0x004A;
    public const int WM_ACTIVATE         = 0x0006;
    public const int WM_WINDOWPOSCHANGED = 0x0047;
    public const int WM_SETTINGCHANGE    = 0x001A;
    public const int WM_DISPLAYCHANGE    = 0x007E;
    public static readonly uint WM_APPBAR_CALLBACK =
        RegisterWindowMessage("MAIS_APPBAR_CALLBACK");

    // ── Window styles ──────────────────────────────────────────────────────

    public const int GWL_STYLE           = -16;
    public const int GWL_EXSTYLE         = -20;
    public const int WS_CAPTION          = 0x00C00000;
    public const int WS_EX_TOOLWINDOW   = 0x00000080;
    public const int WS_EX_APPWINDOW    = 0x00040000;

    // ── SetWindowPos flags ─────────────────────────────────────────────────

    public const uint SWP_NOACTIVATE     = 0x0010;
    public const uint SWP_NOMOVE        = 0x0002;
    public const uint SWP_NOSIZE        = 0x0001;
    public const uint SWP_NOZORDER      = 0x0004;
    public static readonly IntPtr HWND_TOPMOST  = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    // ── Structs ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    // ── Imports ────────────────────────────────────────────────────────────

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
