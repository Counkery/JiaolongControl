using JiaoLongControl.Server.Core.Controllers;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Utils;

public class SelfStart
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(SelfStart));

    public SelfStart()
    {
        var bridge = Bridge.Instance;
        if (bridge.Config.App.BootAdvancedFanControlSystem) Fan();
        if (bridge.Config.App.BootAdvancedCPUSystem) CPU();
        if (bridge.Config.App.BootAdvancedGPUSystem) GPU();
        if (bridge.Config.App.BootSetRyzenSumCurveOptimizerAll)
        {
            if (bridge.Config.App.BootSetRyzenSmuCurveOptimizerPerCore)
            {
                // 分核模式：逐核写入已保存的偏移（含0值，用于重置之前应用的偏移）；列表为空时不写入
                var perCore = bridge.Config.Smu.CurveOptimizerPerCore;
                for (int i = 0; i < perCore.Count; i++)
                    bridge.RyzenSmu.SetCurveOptimizerPerCore((uint)i, perCore[i]);
            }
            else
            {
                bridge.RyzenSmu.SetCurveOptimizerAll(bridge.Config.Smu.CurveOptimizerAll);
            }
        }
        if (bridge.Config.App.BootKeyboardGradient) bridge.KeyboardGradient.Start();

        // 即使未开「开机高级 CPU」，仍可按配置启动功耗保活（监控采集不得承担下发）
        if (bridge.Config.App.CpuPowerKeepAliveEnabled && !bridge.CpuPowerKeepAlive.IsRunning)
        {
            bridge.CpuPowerKeepAlive.SyncFromConfig();
            bridge.CpuPowerKeepAlive.Start();
        }

        // 默认档为静音：冷启动/恢复后若无便携/安静日常瞬时场景，则切到 Quiet + saving
        EnsureDefaultQuietMode();
    }

    /// <summary>
    /// 出厂默认静音：EC Quiet + CPU saving + 静音风扇上限。
    /// 已在 QuietDaily / Portable135 瞬时会话中时跳过，避免冲掉场景。
    /// </summary>
    private static void EnsureDefaultQuietMode()
    {
        try
        {
            var bridge = Bridge.Instance;
            if (bridge.CpuPowerKeepAlive.HasTransientOverride)
                return;

            // 仅当当前配置档为静音（或尚未写入）时强制对齐 EC，避免用户手选高性能后每次 Resume 被打回
            var profile = bridge.Config.Cpu.CpuProfile;
            if (profile is not ("saving" or null or ""))
                return;

            bridge.Config.Cpu.CpuProfile = "saving";
            var current = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
            if (current != SystemPerMode.QuietMode)
            {
                if (!MethodServices.SetValue(MethodName.SystemPerMode, SystemPerMode.QuietMode))
                {
                    Logger.Warn("默认静音：写入 EC Quiet 失败");
                    return;
                }
            }

            OsdController.Instance?.NoteExpectedPerfMode(SystemPerMode.QuietMode);
            // 已是 Quiet 也要再绑一次风扇/CPU，保证上限与档位一致
            PerformanceModeBindings.OnModeChanged(SystemPerMode.QuietMode);
            try { ConfigSerializer.Save(bridge.Config); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            Logger.Warn($"默认静音对齐失败: {ex.Message}");
        }
    }

    private void Fan() { Bridge.Instance.AutoFan.Start(); }

    private void CPU()
    {
        var bridge = Bridge.Instance;
        var cpu = bridge.Config.Cpu.Active;
        bridge.CPU.SetCpuLongPower(cpu.CpuLongPower);
        bridge.CPU.SetCpuShortPower(cpu.CpuShortPower);
        bridge.CPU.SetCPUTempWall(cpu.CpuTempWall);
        bridge.Power.SetCPUMaxFrequency(cpu.CpuMaxFrequency);
        // 软调度含 Boost；勿再单独 EnableTurbo（会把 AC/DC 一律打成 2）
        bridge.Power.ApplySoftSchedule(cpu.SoftSchedule, cpu.CpuTurbo);

        // 首次下发后启动保活（与监控采集分离，避免功耗被电源策略冲掉）
        bridge.CpuPowerKeepAlive.UpdateTarget(cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall);
        if (bridge.Config.App.CpuPowerKeepAliveEnabled)
            bridge.CpuPowerKeepAlive.Start();
    }

    private void GPU()
    {
        var bridge = Bridge.Instance;
        var gpu = bridge.Config.Gpu;
        if (bridge.Config.App.BootGpuUseAdvancedOffsets)
        {
            // 高级超频: 应用已保存的核心/显存频率偏移 (内部自含清零+读回验证+失败回滚,
            // 电源状态过渡期曲线读取失败等场景由其返回失败结果, 不抛出不阻塞其它自启项); 均为 0 时无需写入
            if (gpu.CoreClockOffsetMhz != 0 || gpu.MemoryClockOffsetMhz != 0)
                bridge.NvidiaGpu.SetGpuOffsets(gpu.CoreClockOffsetMhz, gpu.MemoryClockOffsetMhz);
        }
        else
        {
            bridge.NvidiaGpu.LockGpuClock(gpu.GpuClock);
            bridge.NvidiaGpu.LockMemoryClock(gpu.MemoryClock);
            if (gpu.PowerLimit > 0)
                bridge.NvidiaGpu.SetPowerLimit(gpu.PowerLimit);
        }
    }
}
