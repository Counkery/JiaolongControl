using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Native;

/// <summary>
/// 藏起和自绘 OSD 叠在一起的旧提示。
/// 厂家 BLDFnHotkeyUtility 跑在 System 完整性，管理员进程无法对其窗口 ShowWindow/SetWindowPos
/// （UIPI），因此改为挂起该 OSD 进程；Fn / EC 仍由 BLDHotKeyService 处理。
/// Windows 音量/亮度/媒体浮层在「屏蔽原生浮层」打开时继续用藏窗处理。
/// </summary>
internal static class NativeOsdSuppressor
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(NativeOsdSuppressor));
    private static readonly User32.EnumWindowsProc EnumProc = OnEnumWindow;
    private static readonly ConcurrentDictionary<int, byte> SuspendedUtilityPids = new();

    private static CancellationTokenSource? _cts;
    private static int _logged;
    private static int _suspendLogged;

    private const uint ProcessSuspendResume = 0x0800;
    private const uint ProcessQueryLimited = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

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
        ResumeAllUtilities();
    }

    private static void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var osd = Bridge.Instance.Config.Osd;
                bool suppressFactory = osd.Enabled || osd.SuppressNativeVolumeOsd;
                if (suppressFactory)
                    SuppressFactoryOsdProcess();
                else
                    ResumeAllUtilities();

                if (osd.SuppressNativeVolumeOsd)
                    User32.EnumWindows(EnumProc, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                    Logger.Warn($"屏蔽旧 OSD 轮询异常: {ex.Message}");
            }

            token.WaitHandle.WaitOne(80);
        }

        ResumeAllUtilities();
    }

    /// <summary>
    /// 厂家 OSD 进程在 System IL，藏窗会被 UIPI 拒绝；挂起进程即可阻止 Caps/Fn 旧 OSD。
    /// 若窗口已显示，先结束进程清掉残影，服务约数秒后会拉起新实例再挂起。
    /// </summary>
    private static void SuppressFactoryOsdProcess()
    {
        Process[] list;
        try { list = Process.GetProcessesByName("BLDFnHotkeyUtility"); }
        catch { return; }

        if (list.Length == 0)
        {
            PruneDeadSuspendedPids();
            return;
        }

        foreach (var p in list)
        {
            try
            {
                if (HasVisibleFactoryOsd(p.Id))
                {
                    p.Kill();
                    SuspendedUtilityPids.TryRemove(p.Id, out _);
                    if (Interlocked.Exchange(ref _suspendLogged, 1) == 0)
                        Logger.Info("厂家 OSD 窗口已显示且无法跨完整性藏窗，已结束 BLDFnHotkeyUtility 以清除残影");
                    continue;
                }

                SuspendUtility(p.Id);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _logged, 1) == 0)
                    Logger.Warn($"抑制厂家 OSD 进程失败: {ex.Message}");
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        PruneDeadSuspendedPids();
    }

    private static void SuspendUtility(int pid)
    {
        if (pid <= 0 || SuspendedUtilityPids.ContainsKey(pid)) return;

        IntPtr handle = OpenProcess(ProcessSuspendResume | ProcessQueryLimited, false, pid);
        if (handle == IntPtr.Zero) return;
        try
        {
            int status = NtSuspendProcess(handle);
            if (status == 0)
            {
                SuspendedUtilityPids[pid] = 0;
                if (Interlocked.Exchange(ref _suspendLogged, 1) == 0)
                    Logger.Info($"已挂起厂家 OSD 进程 BLDFnHotkeyUtility (pid={pid})，避免与自绘 OSD 叠层");
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void ResumeAllUtilities()
    {
        foreach (var pid in SuspendedUtilityPids.Keys)
        {
            IntPtr handle = OpenProcess(ProcessSuspendResume | ProcessQueryLimited, false, pid);
            if (handle == IntPtr.Zero)
            {
                SuspendedUtilityPids.TryRemove(pid, out _);
                continue;
            }

            try { NtResumeProcess(handle); }
            finally
            {
                CloseHandle(handle);
                SuspendedUtilityPids.TryRemove(pid, out _);
            }
        }
    }

    private static void PruneDeadSuspendedPids()
    {
        foreach (var pid in SuspendedUtilityPids.Keys)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) SuspendedUtilityPids.TryRemove(pid, out _);
            }
            catch
            {
                SuspendedUtilityPids.TryRemove(pid, out _);
            }
        }
    }

    private static bool HasVisibleFactoryOsd(int pid)
    {
        bool found = false;
        User32.EnumWindows((hwnd, _) =>
        {
            User32.GetWindowThreadProcessId(hwnd, out uint windowPid);
            if ((int)windowPid != pid || !User32.IsWindowVisible(hwnd)) return true;
            if (!IsFactoryOsd(hwnd)) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
    {
        if (!User32.IsWindowVisible(hwnd)) return true;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || !IsShellProcess((int)pid)) return true;
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
