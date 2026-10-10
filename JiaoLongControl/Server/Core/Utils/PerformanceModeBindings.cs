using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Utils;

/// <summary>
/// EC 性能模式变更时的风扇上限 / CPU 档位绑定（UI Set 与热键 OSD 共用）。
/// </summary>
public static class PerformanceModeBindings
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(PerformanceModeBindings));

    public static void OnModeChanged(SystemPerMode mode)
    {
        // 真实切模式：结束 QuietDaily 瞬时会话，保活目标回到 Active 档
        bool hadQuietDaily = false;
        try
        {
            var keep = Bridge.Instance.CpuPowerKeepAlive;
            hadQuietDaily = keep.HasTransientOverride;
            // SyncFromConfig(force) 已清瞬时，无需再 Clear
            keep.SyncFromConfig(force: true);
        }
        catch { /* Bridge 未就绪 */ }

        TryApplyModeFanCeiling(mode);
        bool appliedCpu = TryApplyModeCpuProfile(mode);

        // 关闭档位绑定时仍须退出 QuietDaily 的半生效：立刻恢复 Active 软调度/墙
        if (hadQuietDaily && !appliedCpu)
        {
            try { Bridge.Instance.RestoreActiveCpuProfileHardware(); }
            catch { /* ignore */ }
        }

        if (hadQuietDaily)
        {
            try { Bridge.Instance.NotifyQuietDailyChanged(active: false); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 官方语义：切模式后按该模式记忆的转速上限重下发（WMI 0x14/0x15）。
    /// AutoFan 软件曲线运行中则跳过，避免双轨打架。
    /// </summary>
    private static void TryApplyModeFanCeiling(SystemPerMode mode)
    {
        try
        {
            var fanCfg = Bridge.Instance.Config.Fan;
            if (!fanCfg.ApplyFanOnModeChange)
                return;

            var running = Bridge.Instance.AutoFan.IsRunning();
            if (running.Success && running.Data is true)
                return;

            int rpm = mode switch
            {
                SystemPerMode.QuietMode => fanCfg.QuietModeFanRpm,
                SystemPerMode.PerformanceMode => fanCfg.PerformanceModeFanRpm,
                SystemPerMode.BalanceMode => fanCfg.BalanceModeFanRpm,
                _ => fanCfg.BalanceModeFanRpm
            };

            var set = Bridge.Instance.Fan.SetFanCeilingRpm(rpm);
            if (!set.Success)
                Logger.Warn($"模式 {mode} 转速上限失败: {set.Message}");
            else
                Logger.Info($"模式 {mode} 已设转速上限约 {rpm} RPM: {set.Message}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"模式风扇上限绑定异常: {ex.Message}");
        }
    }

    /// <returns>是否实际下发了 CPU 档硬件。</returns>
    private static bool TryApplyModeCpuProfile(SystemPerMode mode)
    {
        try
        {
            var cfg = Bridge.Instance.Config;
            if (!cfg.App.ApplyCpuProfileOnModeChange)
            {
                Logger.Info($"模式 {mode}：ApplyCpuProfileOnModeChange=false，跳过 CPU 档下发");
                return false;
            }

            string profile = mode switch
            {
                SystemPerMode.QuietMode => "saving",
                SystemPerMode.BalanceMode => "default",
                SystemPerMode.PerformanceMode => "performance",
                _ => cfg.Cpu.CpuProfile
            };

            cfg.Cpu.CpuProfile = profile;
            var cpu = cfg.Cpu.Active;
            var bridge = Bridge.Instance;

            var r1 = bridge.CPU.SetCpuLongPower(cpu.CpuLongPower);
            var r2 = bridge.CPU.SetCpuShortPower(cpu.CpuShortPower);
            var r3 = bridge.CPU.SetCPUTempWall(cpu.CpuTempWall);
            var freq = bridge.Power.SetCPUMaxFrequency(cpu.CpuMaxFrequency);
            var soft = bridge.Power.ApplySoftSchedule(cpu.SoftSchedule, cpu.CpuTurbo);
            if (!freq.Success)
                Logger.Warn($"模式 {mode} 频率上限失败: {freq.Message}");
            if (!soft.Success)
                Logger.Warn($"模式 {mode} 软调度失败: {soft.Message}");

            bridge.CpuPowerKeepAlive.UpdateTarget(cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall);
            if (cfg.App.CpuPowerKeepAliveEnabled && !bridge.CpuPowerKeepAlive.IsRunning)
                bridge.CpuPowerKeepAlive.Start();

            Bridge.Instance.FlushIfDirty();

            if (!r1.Success || !r2.Success || !r3.Success || !freq.Success || !soft.Success)
                Logger.Warn(
                    $"模式 {mode} → CPU 档 {profile} 部分失败: L={r1.Success} S={r2.Success} T={r3.Success} " +
                    $"Freq={freq.Success} Soft={soft.Success} " +
                    $"(target {cpu.CpuLongPower}/{cpu.CpuShortPower}/{cpu.CpuTempWall})");
            else
                Logger.Info(
                    $"模式 {mode} → CPU 档 {profile}: L={cpu.CpuLongPower}W S={cpu.CpuShortPower}W T={cpu.CpuTempWall}℃");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"模式 CPU 档位同步异常: {ex.Message}");
            return false;
        }
    }
}
