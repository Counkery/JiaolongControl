using System.Runtime.InteropServices;
using JiaoLongControl.Server.Core.Controllers;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Core.Utils;
using log4net;
using Microsoft.Web.WebView2.Core;

namespace JiaoLongControl.Server.Interop
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Bridge : IDisposable
    {
        private const int SaveIntervalMs = 5000;

        private static readonly ILog Logger =
            LogManager.GetLogger(typeof(Bridge));

        private readonly object _configLock = new();
        private Timer _saveTimer;

        private CoreWebView2? _webView;

        public Bridge()
        {
            Config = ConfigSerializer.Load();
            _saveTimer = new Timer(
                _ => FlushIfDirty(),
                null,
                SaveIntervalMs,
                SaveIntervalMs);
        }

        public static Bridge Instance { get; } = new();
        internal JiaoLongConfig Config { get; private set; } = null!;

        public CpuController CPU { get; } = new();
        public FanController Fan { get; } = new();
        public GpuController GPU { get; } = new();
        public LogoLightController LogoLight { get; } = new();
        public KeyboardController Keyboard { get; } = new();
        public PerformanceModeController PerformanceMode { get; } = new();
        public ConfigController ConfigCtrl { get; } = new();
        public AutoStartController AutoStart { get; } = new();
        public AutoFanControl AutoFan { get; } = new();
        public KeyboardGradientController KeyboardGradient { get; } = new();
        public PowerController Power { get; } = new();
        public NvidiaGpuController NvidiaGpu { get; } = new();
        public RyzenSmuController RyzenSmu { get; } = new();
        public SystemInfoController SystemInfo { get; } = new();
        public OsdController Osd { get; } = new();

        /// <summary>CPU 功耗保活（与 OSD/监控轮询分离）。</summary>
        public CpuPowerKeepAliveService CpuPowerKeepAlive { get; } = new();

        public void Dispose()
        {
            _saveTimer.Dispose();
            _saveTimer = null;
            FlushIfDirty();
            CpuPowerKeepAlive.Dispose();
            CPU.Dispose();
            Fan.Dispose();
            AutoFan.Dispose();
            KeyboardGradient.Dispose();
            RyzenSmu.Dispose();
            NvidiaGpu.Dispose();
            Osd.Dispose();
        }

        public void ApplyConfig(JiaoLongConfig config)
        {
            bool hadTransient = CpuPowerKeepAlive.HasTransientOverride;
            string? priorScene = CpuPowerKeepAlive.TransientScene;
            JiaoLongConfig previous;
            lock (_configLock)
            {
                previous = Config;
                Config = config;
            }

            // 仅当 CPU/保活相关字段变化时才结束瞬时场景；改主题/OSD 等不应冲掉
            bool endTransient = hadTransient && IsCpuPowerRelatedChange(previous, config);

            try
            {
                if (endTransient)
                {
                    CpuPowerKeepAlive.SyncFromConfig(force: true);
                    RestoreGpuAfterPortableScene(priorScene);
                    RestoreActiveCpuProfileHardware();
                }
                else if (!hadTransient)
                {
                    CpuPowerKeepAlive.SyncFromConfig(force: true);
                }
                else
                {
                    // 瞬时保持中：不 force，避免无关 SetConfig 清瞬时
                    CpuPowerKeepAlive.SyncFromConfig(force: false);
                }

                if (Config.App.CpuPowerKeepAliveEnabled && !CpuPowerKeepAlive.IsRunning)
                    CpuPowerKeepAlive.Start();
                else if (!Config.App.CpuPowerKeepAliveEnabled && CpuPowerKeepAlive.IsRunning)
                    CpuPowerKeepAlive.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn($"同步 CPU 功耗保活失败: {ex.Message}");
            }

            NotifyConfigChanged();
            if (endTransient)
                NotifyQuietDailyChanged(active: false, scene: priorScene);
        }

        private static bool IsCpuPowerRelatedChange(JiaoLongConfig a, JiaoLongConfig b)
        {
            if (a.App.CpuPowerKeepAliveEnabled != b.App.CpuPowerKeepAliveEnabled)
                return true;
            if (a.Cpu.CpuProfile != b.Cpu.CpuProfile)
                return true;
            return !SameCpuProfile(a.Cpu.Saving, b.Cpu.Saving)
                   || !SameCpuProfile(a.Cpu.Default, b.Cpu.Default)
                   || !SameCpuProfile(a.Cpu.Performance, b.Cpu.Performance)
                   || !SameCpuProfile(a.Cpu.Custom, b.Cpu.Custom);
        }

        private static bool SameCpuProfile(CpuProfileData? x, CpuProfileData? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.CpuLongPower != y.CpuLongPower || x.CpuShortPower != y.CpuShortPower ||
                x.CpuTempWall != y.CpuTempWall || x.CpuMaxFrequency != y.CpuMaxFrequency ||
                x.CpuTurbo != y.CpuTurbo)
                return false;
            return SameSoft(x.SoftSchedule, y.SoftSchedule);
        }

        private static bool SameSoft(SoftScheduleData? x, SoftScheduleData? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.AcEpp == y.AcEpp && x.DcEpp == y.DcEpp &&
                   x.AcCpMinCores == y.AcCpMinCores && x.DcCpMinCores == y.DcCpMinCores &&
                   x.AcBoostMode == y.AcBoostMode && x.DcBoostMode == y.DcBoostMode;
        }

        internal void NotifyConfigChanged()
        {
            try
            {
                _webView?.PostWebMessageAsJson("{\"type\":\"config-changed\"}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"config-changed 通知失败: {ex.Message}");
            }
        }

        /// <summary>退出 QuietDaily 后立即按 Active 档重下发墙/频率/软调度，避免半生效。</summary>
        internal void RestoreActiveCpuProfileHardware()
        {
            try
            {
                var cpu = Config.Cpu.Active;
                CPU.SetCpuLongPower(cpu.CpuLongPower);
                CPU.SetCpuShortPower(cpu.CpuShortPower);
                CPU.SetCPUTempWall(cpu.CpuTempWall);
                Power.SetCPUMaxFrequency(cpu.CpuMaxFrequency);
                Power.ApplySoftSchedule(cpu.SoftSchedule, cpu.CpuTurbo);
                var cleared = CpuPowerKeepAlive.UpdateTarget(
                    cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall);
                RestoreGpuAfterPortableScene(cleared);
            }
            catch (Exception ex)
            {
                Logger.Warn($"恢复 Active CPU 档硬件失败: {ex.Message}");
            }
        }

        /// <summary>退出 Portable135 时把 GPU 功耗墙写回配置里的值。</summary>
        internal void RestoreGpuAfterPortableScene(string? clearedScene)
        {
            if (!string.Equals(clearedScene, "Portable135", StringComparison.Ordinal))
                return;
            try
            {
                int watts = Config.Gpu.PowerLimit;
                if (watts > 0)
                {
                    var r = NvidiaGpu.SetPowerLimit(watts);
                    Logger.Info($"Portable135 结束，GPU 功耗墙恢复 {watts}W: {r.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Portable135 恢复 GPU 功耗失败: {ex.Message}");
            }
        }

        internal void NotifyQuietDailyChanged(bool active, string? scene = null)
        {
            try
            {
                string sceneJson = string.IsNullOrEmpty(scene)
                    ? "null"
                    : $"\"{scene.Replace("\"", "", StringComparison.Ordinal)}\"";
                var json = active
                    ? $"{{\"type\":\"quiet-daily-changed\",\"active\":true,\"scene\":{sceneJson}}}"
                    : $"{{\"type\":\"quiet-daily-changed\",\"active\":false,\"scene\":{sceneJson}}}";
                _webView?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"quiet-daily-changed 通知失败: {ex.Message}");
            }
        }

        internal void FlushIfDirty()
        {
            try
            {
                string memoryYaml;
                lock (_configLock)
                {
                    memoryYaml = ConfigSerializer.Serialize(Config);
                }

                var diskYaml = ConfigSerializer.ReadFileContent();
                if (diskYaml != memoryYaml)
                {
                    lock (_configLock)
                    {
                        ConfigSerializer.Save(Config);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"配置差异落盘失败: {ex.Message}");
            }
        }

        public void InitWebView(CoreWebView2 webView)
    {
        _webView = webView;
    }

        /// <summary>向前端推送 JSON 消息 (需在 UI 线程调用); WebView 未就绪时静默忽略。</summary>
    public void PostWebMessage(string json)
    {
        try
        {
            _webView?.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            Logger.Warn($"web message 发送失败: {ex.Message}");
        }
    }
    }
}
