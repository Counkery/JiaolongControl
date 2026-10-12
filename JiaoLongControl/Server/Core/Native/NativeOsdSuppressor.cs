using System.Diagnostics;
using System.Text;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Native;

/// <summary>
/// 藏起和自绘 OSD 叠在一起的旧提示。
/// 厂家 OSD 是 BLDFnHotkeyUtility 的 FloatingNativeWindow，弹出时不一定发 EVENT_OBJECT_SHOW，
/// 所以按进程轮询并移出屏幕。Windows 音量/亮度/媒体浮层在「屏蔽原生浮层」打开时同样处理。
/// 不结束厂家热键进程，Fn 仍由 EC / WMI 生效。
/// </summary>
internal static class NativeOsdSuppressor
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(NativeOsdSuppressor));
    private static readonly User32.EnumWindowsProc EnumProc = OnEnumWindow;
    private static CancellationTokenSource? _cts;
    private static int _bldUtilityPid;
    private static int _bldServicePid;
    private static long _pidStamp;
    private static int _logged;

    public static void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Factory.StartNew(() => Loop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); }
        catch { /* ignore */ }
        _cts = null;
    }

    private static void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var osd = Bridge.Instance.Config.Osd;
                if (osd.Enabled || osd.SuppressNativeVolumeOsd)
                {
                    RefreshPids();
                    User32.EnumWindows(EnumProc, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                    Logger.Warn($"屏蔽旧 OSD 轮询异常: {ex.Message}");
            }

            token.WaitHandle.WaitOne(80);
        }
    }

    private static void RefreshPids()
    {
        long now = Environment.TickCount64;
        if (now - _pidStamp < 2000) return;
        _pidStamp = now;
        _bldUtilityPid = FirstPid("BLDFnHotkeyUtility");
        _bldServicePid = FirstPid("BLDHotKeyService");
    }

    private static int FirstPid(string name)
    {
        try
        {
            var list = Process.GetProcessesByName(name);
            return list.Length > 0 ? list[0].Id : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
    {
        if (!User32.IsWindowVisible(hwnd)) return true;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return true;

        bool factory = (int)pid == _bldUtilityPid || (int)pid == _bldServicePid;
        if (factory && IsFactoryOsd(hwnd))
        {
            Hide(hwnd);
            return true;
        }

        try
        {
            if (!Bridge.Instance.Config.Osd.SuppressNativeVolumeOsd) return true;
        }
        catch
        {
            return true;
        }

        if (!IsShellProcess((int)pid)) return true;
        if (!IsShellFlyoutClass(hwnd) || !IsSmallFlyout(hwnd)) return true;
        Hide(hwnd);
        return true;
    }

    private static bool IsFactoryOsd(IntPtr hwnd)
    {
        var title = ReadText(hwnd, windowTitle: true);
        if (title.Contains("FloatingNativeWindow", StringComparison.Ordinal))
            return true;
        var cls = ReadText(hwnd, windowTitle: false);
        return cls.StartsWith("WindowsForms10.Window", StringComparison.Ordinal) && IsSmallFlyout(hwnd);
    }

    private static bool IsShellFlyoutClass(IntPtr hwnd)
    {
        var cls = ReadText(hwnd, windowTitle: false);
        return cls is "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow" or "NativeHWNDHost";
    }

    private static bool IsShellProcess(int pid)
    {
        try
        {
            var name = Process.GetProcessById(pid).ProcessName;
            return name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("ShellHost", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSmallFlyout(IntPtr hwnd)
    {
        if (!User32.GetWindowRect(hwnd, out var rect)) return false;
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        return w is >= 40 and <= 720 && h is >= 24 and <= 640;
    }

    private static void Hide(IntPtr hwnd)
    {
        User32.ShowWindow(hwnd, User32.SW_HIDE);
        User32.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    private static string ReadText(IntPtr hwnd, bool windowTitle)
    {
        var sb = new StringBuilder(256);
        if (windowTitle) User32.GetWindowText(hwnd, sb, sb.Capacity);
        else User32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
