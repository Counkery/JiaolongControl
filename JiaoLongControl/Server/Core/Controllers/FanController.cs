using System.Runtime.InteropServices;
using JiaoLongControl.Server.Core.Drivers;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;

namespace JiaoLongControl.Server.Core.Controllers;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class FanController : Blding64
{
    public CommandResult GetFanSpeed()
    {
        Tuple<int, int> CPUGPUFanSpeed = MethodServices.GetValue<Tuple<int, int>>(MethodName.CPUGPUFanSpeed);
        var fanSpeedInfo = new FanSpeedInfo
        {
            CPUFanSpeed = CPUGPUFanSpeed.Item2,
            GPUFanSpeed = CPUGPUFanSpeed.Item1
        };
        return new CommandResult(true, "获取成功", fanSpeedInfo);
    }

    /// <summary>
    /// 设置当前模式的 WMI 转速上限（官方语义：MaxFanSpeedSwitch + MaxFanSpeed）。
    /// rpm&lt;=0 表示关闭上限、交回 EC 自动控制。
    /// </summary>
    public CommandResult SetFanCeilingRpm(int rpm)
    {
        var mode = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
        if (mode == SystemPerMode.Unknow)
            mode = SystemPerMode.BalanceMode;

        if (rpm <= 0)
        {
            var off = SetMaxFanSpeedSwitch(false);
            return new CommandResult(off.Success, off.Success ? "已交回 EC 自动风扇" : "关闭上限失败", new
            {
                Enabled = false,
                Rpm = 0,
                Mode = mode.ToString()
            });
        }

        var (min, max) = FanCeilingRanges.ForMode(mode);
        int clamped = FanCeilingRanges.ClampRpm(mode, rpm);
        byte unit = FanCeilingRanges.ToUnit(clamped);

        // 显式设 WMI 上限时只停曲线线程，避免 Stop() 再写回旧上限造成递归。
        try
        {
            Bridge.Instance.AutoFan.StopLoop();
            ClearForcedSpeed();
        }
        catch { /* Bridge 未就绪时忽略 */ }

        if (!SetMaxFanSpeedSwitch(true).Success)
            return new CommandResult(false, "打开转速上限开关失败");

        var ok = MethodServices.SetValue(MethodName.MaxFanSpeed, unit);
        string msg = ok
            ? (clamped != rpm
                ? $"已设转速上限 {clamped} RPM（已按 {mode} 模式钳制到 {min}-{max}）"
                : $"已设转速上限 {clamped} RPM")
            : "设置转速上限失败";

        return new CommandResult(ok, msg, new
        {
            Enabled = true,
            Rpm = clamped,
            RequestedRpm = rpm,
            MinRpm = min,
            MaxRpm = max,
            Mode = mode.ToString()
        });
    }

    public CommandResult GetFanCeilingRpm()
    {
        var mode = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
        if (mode == SystemPerMode.Unknow)
            mode = SystemPerMode.BalanceMode;
        var (min, max) = FanCeilingRanges.ForMode(mode);
        var on = MethodServices.GetValue<byte>(MethodName.MaxFanSpeedSwitch) == 1;
        var unit = MethodServices.GetValue<byte>(MethodName.MaxFanSpeed);
        int rpm = on && unit != byte.MaxValue ? unit * 100 : 0;
        return new CommandResult(true, "获取成功", new
        {
            Enabled = on,
            Rpm = rpm,
            MinRpm = min,
            MaxRpm = max,
            Mode = mode.ToString()
        });
    }

    public CommandResult GetFanCeilingRange()
    {
        var mode = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
        if (mode == SystemPerMode.Unknow)
            mode = SystemPerMode.BalanceMode;
        var (min, max) = FanCeilingRanges.ForMode(mode);
        return new CommandResult(true, "获取成功", new { MinRpm = min, MaxRpm = max, Mode = mode.ToString() });
    }

    /// <summary>
    /// 进阶：经 Blding 强制写入风扇设定值（非官方 WMI 上限语义）。
    /// AutoFan 软件曲线使用此路径；日常安静请优先用 SetFanCeilingRpm。
    /// </summary>
    public CommandResult SetFanSpeed(byte fanSpeed)
    {
        var maxOn = GetMaxFanSpeedSwitch();
        if (maxOn.Success && maxOn.Data is true)
            SetMaxFanSpeedSwitch(false);

        if (IsInitialized)
        {
            GpuFanSetSpeed(fanSpeed);
            CpuFanSetSpeed(fanSpeed);
            return new CommandResult(true, "Blding 强制设速成功（进阶路径）");
        }

        return new CommandResult(false, "设置失败");
    }

    public new CommandResult RemoveFanSpeed() => RestoreEcAutoFan();

    /// <summary>只清 Blding 强制转速，不动 WMI 上限开关。</summary>
    public void ClearForcedSpeed()
    {
        try
        {
            if (IsInitialized)
                base.RemoveFanSpeed();
        }
        catch
        {
            /* 驱动未就绪时忽略 */
        }
    }

    /// <summary>按当前 EC 模式写回配置里记住的转速上限。未启用模式绑定时才交回 EC 自动。</summary>
    public CommandResult ReapplySavedCeiling()
    {
        var scene = Bridge.Instance.CpuPowerKeepAlive.TransientScene;
        if (string.Equals(scene, "Portable135", StringComparison.Ordinal))
            return SetFanCeilingRpm(2700);
        if (string.Equals(scene, "QuietDaily", StringComparison.Ordinal))
            return SetFanCeilingRpm(3500);

        var fan = Bridge.Instance.Config.Fan;
        if (!fan.ApplyFanOnModeChange)
            return RestoreEcAutoFan();

        var mode = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
        if (mode == SystemPerMode.Unknow)
            mode = SystemPerMode.BalanceMode;
        int rpm = mode switch
        {
            SystemPerMode.QuietMode => fan.QuietModeFanRpm,
            SystemPerMode.PerformanceMode => fan.PerformanceModeFanRpm,
            _ => fan.BalanceModeFanRpm
        };
        return SetFanCeilingRpm(rpm);
    }

    /// <summary>
    /// 交回 EC 自动风扇：Blding 目标转速写 0（若驱动可用）+ 关闭 WMI 上限开关 (FAAP)。
    /// 用于退出程序，或用户明确关闭转速上限。停止软件曲线不要走这里。
    /// </summary>
    public CommandResult RestoreEcAutoFan()
    {
        bool bldingOk = false;
        try
        {
            ClearForcedSpeed();
            bldingOk = IsInitialized;
        }
        catch
        {
            /* 驱动异常仍继续关 FAAP */
        }

        var faap = SetMaxFanSpeedSwitch(false);
        bool ok = faap.Success || bldingOk;
        return new CommandResult(ok, ok ? "已交回 EC 自动风扇" : "交回 EC 自动风扇失败");
    }

    public CommandResult SetMaxFanSpeedSwitch(bool maxFanSpeedSwitch)
    {
        var res = MethodServices.SetValue(MethodName.MaxFanSpeedSwitch, (byte)(maxFanSpeedSwitch ? 1 : 0));
        return new CommandResult(res, res ? "设置成功" : "设置失败", maxFanSpeedSwitch);
    }

    public CommandResult GetMaxFanSpeedSwitch()
    {
        var on = MethodServices.GetValue<byte>(MethodName.MaxFanSpeedSwitch) == 1;
        return new CommandResult(true, "获取成功", on);
    }
}
