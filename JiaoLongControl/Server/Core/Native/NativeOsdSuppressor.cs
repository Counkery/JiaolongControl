using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JiaoLongControl.Server.Interop;

namespace JiaoLongControl.Server.Core.Native;

/// <summary>
/// 本程序 OSD 打开时，藏起会叠在一起的旧提示：
/// 厂家 BLDFnHotkeyUtility 的 FloatingNativeWindow，以及 Windows 音量/亮度/媒体浮层。
/// 不结束厂家热键进程，Fn 仍由 EC / WMI 生效。
/// </summary>
internal static class NativeOsdSuppressor
{
    private static User32.WinEventDelegate? _callback;
    private static IntPtr _showHook;
    private static IntPtr _moveHook;
    private static readonly StringBuilder ClassName = new(256);
    private static readonly StringBuilder Title = new(256);

    public static void Start()
    {
        if (_showHook != IntPtr.Zero) return;
        _callback = OnWinEvent;
        uint flags = User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS;
        _showHook = User32.SetWinEventHook(
            User32.EVENT_OBJECT_SHOW, User32.EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, flags);
        _moveHook = User32.SetWinEventHook(
            User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _callback, 0, 0, flags);
    }

    public static void Stop()
    {
        if (_showHook != IntPtr.Zero) User32.UnhookWinEvent(_showHook);
        if (_moveHook != IntPtr.Zero) User32.UnhookWinEvent(_moveHook);
        _showHook = IntPtr.Zero;
        _moveHook = IntPtr.Zero;
    }

    private static void OnWinEvent(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
        try
        {
            var osd = Bridge.Instance.Config.Osd;
            if (!osd.Enabled) return;

            Title.Clear();
            User32.GetWindowText(hwnd, Title, Title.Capacity);
            if (Title.ToString() == "FloatingNativeWindow")
            {
                User32.ShowWindow(hwnd, User32.SW_HIDE);
                return;
            }

            if (!osd.SuppressNativeVolumeOsd) return;
            ClassName.Clear();
            User32.GetClassName(hwnd, ClassName, ClassName.Capacity);
            var cls = ClassName.ToString();
            if (cls is not ("XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow" or "NativeHWNDHost"))
                return;
            if (!IsSmallFlyout(hwnd) || !IsShellFlyoutProcess(hwnd)) return;
            User32.ShowWindow(hwnd, User32.SW_HIDE);
        }
        catch
        {
            // 钩子里不能抛
        }
    }

    private static bool IsSmallFlyout(IntPtr hwnd)
    {
        if (!User32.GetWindowRect(hwnd, out var rect)) return false;
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        return w is >= 48 and <= 640 && h is >= 36 and <= 420;
    }

    private static bool IsShellFlyoutProcess(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        try
        {
            var name = Process.GetProcessById((int)pid).ProcessName;
            return name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
