using System.Management;
using log4net;

namespace JiaoLongControl.Server.Core.Native;

/// <summary>
/// 通过 WMI 读写内置屏亮度, 并订阅 <c>WmiMonitorBrightnessEvent</c> 做即时变化通知
/// (接近 Windows 原生亮度 OSD 的响应速度)。外接屏/桌面无接口时读写与订阅均失败。
/// </summary>
internal static class DisplayBrightness
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(DisplayBrightness));
    private static ManagementEventWatcher? _watcher;
    private static Action<int>? _onChanged;

    /// <summary>亮度百分比变化 (0~100); 回调可能在 WMI 线程。</summary>
    public static event Action<int>? Changed
    {
        add
        {
            _onChanged += value;
            EnsureWatcher();
        }
        remove
        {
            _onChanged -= value;
            if (_onChanged == null) StopWatcher();
        }
    }

    /// <summary>读取当前亮度百分比；不可用时返回 null。</summary>
    public static int? TryGetPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var v = Convert.ToInt32(obj["CurrentBrightness"]);
                    return Math.Clamp(v, 0, 100);
                }
            }
        }
        catch
        {
            // 无 WMI 亮度接口 (外接屏/桌面)
        }

        return null;
    }

    /// <summary>设置亮度百分比；成功返回 true。</summary>
    public static bool TrySetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)percent });
                    return true;
                }
            }
        }
        catch
        {
            // 忽略写入失败
        }

        return false;
    }

    /// <summary>是否已成功订阅亮度事件 (否则调用方应启用快轮询兜底)。</summary>
    public static bool IsEventWatching => _watcher != null;

    private static void EnsureWatcher()
    {
        if (_watcher != null) return;
        try
        {
            // WithinInterval 尽量短, 降低事件投递延迟
            var options = new EventWatcherOptions(null, TimeSpan.FromMilliseconds(50), 1);
            _watcher = new ManagementEventWatcher(
                new ManagementScope(@"root\WMI"),
                new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"),
                options);
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
            Logger.Info("WmiMonitorBrightnessEvent 订阅成功");
        }
        catch (Exception ex)
        {
            Logger.Warn($"WmiMonitorBrightnessEvent 订阅失败, 将退回快轮询: {ex.Message}");
            StopWatcher();
        }
    }

    private static void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var raw = e.NewEvent?.Properties["Brightness"]?.Value;
            if (raw == null) return;
            var percent = Math.Clamp(Convert.ToInt32(raw), 0, 100);
            _onChanged?.Invoke(percent);
        }
        catch (Exception ex)
        {
            Logger.Warn($"WmiMonitorBrightnessEvent 解析失败: {ex.Message}");
        }
    }

    private static void StopWatcher()
    {
        try
        {
            if (_watcher == null) return;
            _watcher.EventArrived -= OnEventArrived;
            _watcher.Stop();
            _watcher.Dispose();
        }
        catch
        {
            // 忽略
        }
        finally
        {
            _watcher = null;
        }
    }
}
