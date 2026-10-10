using System.Collections.Concurrent;
using System.Management;
using JiaoLongControl.Server.Core.Models;
using log4net;

namespace JiaoLongControl.Server.Core.Services
{
    /// <summary>EC 热键事件参数。</summary>
    public class WmiHotKeyEvent : EventArgs
    {
        public byte EventType { get; init; }
        public EventName EventName { get; init; }
        public int Value { get; init; }
    }

    /// <summary>
    /// 订阅 root\WMI 的 HID_EVENT20 事件类 —— 官方 EC 热键事件通道。
    /// EventDetail 为字节数组: [0]=EventType(HotKey=1), [1]=EventName, [2]=值
    /// (CPU/GPU 风扇速度为 [2]&lt;&lt;8|[3] 双字节, 其余为单字节)。
    /// 订阅失败 (机型无此事件类等) 由调用方退回轮询兜底。
    /// 对相同 EventName+Value 在时间窗内去重，避免固件双发导致 UI/下发重复。
    /// </summary>
    public class WMIEventService : IDisposable
    {
        private const string Wql = "SELECT * FROM HID_EVENT20";

        /// <summary>相同事件名+值在此毫秒内只向上游投递一次。</summary>
        public static int DebounceWindowMs { get; set; } = 250;

        private static readonly ILog Logger = LogManager.GetLogger(typeof(WMIEventService));

        private readonly ConcurrentDictionary<long, long> _lastDeliveredTicks = new();
        private ManagementEventWatcher? _watcher;

        public void Dispose() => Stop();

        /// <summary>事件到达 (WMI 线程池线程回调)。</summary>
        public event Action<WmiHotKeyEvent>? EventArrived;

        /// <summary>启动订阅; 成功返回 true。</summary>
        public bool Start()
        {
            try
            {
                _watcher = new ManagementEventWatcher("root\\WMI", Wql);
                _watcher.EventArrived += OnEventArrived;
                _watcher.Start();
                Logger.Info("HID_EVENT20 事件订阅成功");
                return true;
            }
            catch (ManagementException ex)
            {
                Logger.Warn($"HID_EVENT20 事件订阅失败: {ex.Message}");
                Stop();
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"HID_EVENT20 事件订阅异常: {ex.Message}");
                Stop();
                return false;
            }
        }

        private void OnEventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent?.Properties["EventDetail"]?.Value is not byte[] detail || detail.Length < 3)
                    return;

                var evt = new WmiHotKeyEvent
                {
                    EventType = detail[0],
                    EventName = (EventName)detail[1],
                    Value = detail[1] == (byte)EventName.CPUFanSpeed || detail[1] == (byte)EventName.GPUFanSpeed
                        ? (detail[2] << 8) + (detail.Length > 3 ? detail[3] : 0)
                        : detail[2],
                };

                if (ShouldSuppress(evt))
                {
                    Logger.Debug($"HID_EVENT20 去重抑制: {evt.EventName}={evt.Value}");
                    return;
                }

                EventArrived?.Invoke(evt);
            }
            catch (Exception ex)
            {
                Logger.Warn($"HID_EVENT20 事件解析失败: {ex.Message}");
            }
        }

        /// <summary>按 EventName+Value 做时间窗去重。风扇转速类事件不过滤（连续变化）。</summary>
        private bool ShouldSuppress(WmiHotKeyEvent evt)
        {
            if (evt.EventName is EventName.CPUFanSpeed or EventName.GPUFanSpeed)
                return false;

            long key = ((long)evt.EventName << 32) | (uint)evt.Value;
            long now = Environment.TickCount64;
            long window = Math.Clamp(DebounceWindowMs, 50, 2000);

            if (_lastDeliveredTicks.TryGetValue(key, out long last) && now - last < window)
                return true;

            _lastDeliveredTicks[key] = now;

            // 偶发清理，避免字典无限增长
            if (_lastDeliveredTicks.Count > 64)
            {
                foreach (var pair in _lastDeliveredTicks)
                {
                    if (now - pair.Value > window * 8)
                        _lastDeliveredTicks.TryRemove(pair.Key, out _);
                }
            }

            return false;
        }

        public void Stop()
        {
            var watcher = _watcher;
            _watcher = null;
            if (watcher == null) return;
            try
            {
                watcher.Stop();
                watcher.EventArrived -= OnEventArrived;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"HID_EVENT20 事件订阅停止异常: {ex.Message}");
            }
        }
    }
}
