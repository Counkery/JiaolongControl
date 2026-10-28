using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;

namespace JiaoLongControl.Server.Core.Controllers;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class PowerController
{
    // PERFBOOSTMODE / PERFEPP / CPMINCORES
    private const string GuidBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
    private const string GuidEpp = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";
    private const string GuidCpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const int PowerCfgTimeoutMs = 5000;

    public CommandResult SetCPUMaxFrequency(uint mhz)
    {
        var commands = new[]
        {
            "/overlaysetactive overlay_scheme_none",
            "/attributes SUB_PROCESSOR 75b0ae3f-bce0-45a7-8c89-c9611c25e100 -ATTRIB_HIDE",
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {mhz}",
            $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {mhz}",
            "/setactive SCHEME_CURRENT"
        };
        foreach (var cmd in commands)
        {
            var result = RunPowerCfg(cmd);
            if (!result.Success)
                return result;
        }
        return new CommandResult(true, $"CPU 最大频率已限制为 {mhz} MHz");
    }

    public CommandResult ResetCPUMaxFrequency()
    {
        var commands = new[]
        {
            "/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0",
            "/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0",
            "/setactive SCHEME_CURRENT"
        };
        foreach (var cmd in commands)
        {
            var result = RunPowerCfg(cmd);
            if (!result.Success)
                return result;
        }
        return new CommandResult(true, "CPU 频率限制已取消");
    }

    public CommandResult DisableTurbo()
    {
        try
        {
            var soft = EffectiveSoftSchedule();
            return ApplySoftSchedule(soft, turboEnabled: false);
        }
        catch
        {
            return ApplySoftSchedule(SoftSchedulePresets.Game(), turboEnabled: false);
        }
    }

    /// <summary>
    /// 开启睿频：按有效 SoftSchedule 写入（QuietDaily 瞬时优先于档位），不再强制两边都是 Aggressive(2)。
    /// </summary>
    public CommandResult EnableTurbo()
    {
        try
        {
            var soft = EffectiveSoftSchedule();
            return ApplySoftSchedule(soft, turboEnabled: true);
        }
        catch
        {
            return ApplySoftSchedule(SoftSchedulePresets.Game(), turboEnabled: true);
        }
    }

    private static SoftScheduleData EffectiveSoftSchedule()
    {
        var cpu = Bridge.Instance.Config.Cpu.Active;
        return Bridge.Instance.CpuPowerKeepAlive.GetEffectiveSoftSchedule(cpu.SoftSchedule);
    }

    /// <summary>
    /// 按下发软调度：EPP、核心停放最小比例、睿频模式（AC/DC 分设）。
    /// <paramref name="turboEnabled"/> 为 false 时强制 Boost=0。
    /// </summary>
    public CommandResult ApplySoftSchedule(
        byte acEpp, byte dcEpp,
        byte acCpMinCores, byte dcCpMinCores,
        byte acBoostMode, byte dcBoostMode,
        bool turboEnabled)
    {
        byte acBoost = turboEnabled ? ClampByte(acBoostMode, 0, 2) : (byte)0;
        byte dcBoost = turboEnabled ? ClampByte(dcBoostMode, 0, 2) : (byte)0;
        return ApplySoftScheduleCore(
            ClampByte(acEpp, 0, 100),
            ClampByte(dcEpp, 0, 100),
            ClampByte(acCpMinCores, 0, 100),
            ClampByte(dcCpMinCores, 0, 100),
            acBoost,
            dcBoost);
    }

    /// <summary>供宿主内部调用；不暴露给 COM（WebView 请用七参数重载）。</summary>
    [ComVisible(false)]
    public CommandResult ApplySoftSchedule(SoftScheduleData? schedule, bool turboEnabled)
    {
        schedule ??= SoftSchedulePresets.Game();
        return ApplySoftSchedule(
            schedule.AcEpp, schedule.DcEpp,
            schedule.AcCpMinCores, schedule.DcCpMinCores,
            schedule.AcBoostMode, schedule.DcBoostMode,
            turboEnabled);
    }

    private CommandResult ApplySoftScheduleCore(
        byte acEpp, byte dcEpp, byte acCp, byte dcCp, byte acBoost, byte dcBoost)
    {
        var commands = new[]
        {
            // 与限频一致：清 Win11 电源叠加层，避免盖掉 EPP/停放
            "/overlaysetactive overlay_scheme_none",
            $"/attributes SUB_PROCESSOR {GuidBoostMode} -ATTRIB_HIDE",
            $"/attributes SUB_PROCESSOR {GuidEpp} -ATTRIB_HIDE",
            $"/attributes SUB_PROCESSOR {GuidCpMinCores} -ATTRIB_HIDE",
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {acBoost}",
            $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {dcBoost}",
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP {acEpp}",
            $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP {dcEpp}",
            $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES {acCp}",
            $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES {dcCp}",
            "/setactive SCHEME_CURRENT"
        };

        foreach (var cmd in commands)
        {
            var result = RunPowerCfg(cmd);
            if (!result.Success)
                return result;
        }

        return new CommandResult(true,
            $"软调度已应用: EPP AC/DC={acEpp}/{dcEpp}, CPMIN={acCp}/{dcCp}, Boost={acBoost}/{dcBoost}");
    }

    private static byte ClampByte(byte value, byte min, byte max) =>
        value < min ? min : value > max ? max : value;

    public CommandResult GetCPUMaxFrequency()
    {
        var result = RunPowerCfgWithOutput("/query SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX");
        if (!result.Success)
            return new CommandResult(false, result.Message);

        if (!TryParseLastTwoHex((string)result.Data!, out var ac, out var dc))
            return new CommandResult(false, "解析失败");

        return new CommandResult(true, "ok", (ac, dc));
    }

    /// <summary>
    /// Data: Ac/Dc 为 PERFBOOSTMODE 原始值；AcEnabled/DcEnabled 为是否非 0。
    /// </summary>
    public CommandResult GetTurboEnabled()
    {
        var result = RunPowerCfgWithOutput("/query SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE");
        if (!result.Success)
            return new CommandResult(false, result.Message);

        if (result.Data is not string queryOutput ||
            !TryParseLastTwoHex(queryOutput, out var acVal, out var dcVal))
            return new CommandResult(false, "解析失败");

        return new CommandResult(
            true,
            "ok",
            new
            {
                Ac = acVal,
                Dc = dcVal,
                AcEnabled = acVal != 0,
                DcEnabled = dcVal != 0,
                // 兼容旧前端解构 (ac, dc) 布尔语义
                ac = acVal != 0,
                dc = dcVal != 0
            }
        );
    }

    private CommandResult RunPowerCfg(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null)
                return new CommandResult(false, $"[PowerController] 无法启动 powercfg: {arguments}");

            // 必须异步读输出: 先 ReadToEnd 再 WaitForExit 时, powercfg 一旦卡住
            // ReadToEnd 会永久阻塞, 超时判断永远到不了。
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(PowerCfgTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new CommandResult(false, $"[PowerController] powercfg 超时 ({PowerCfgTimeoutMs}ms): {arguments}");
            }

            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 1000);
            string error = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";

            if (process.ExitCode != 0)
                return new CommandResult(false, $"[PowerController] powercfg {arguments} 失败: {error}");
        }
        catch (Exception ex)
        {
            return new CommandResult(false, $"[PowerController] 执行 powercfg 异常: {ex.Message}");
        }

        return new CommandResult(true, "执行成功");
    }

    private CommandResult RunPowerCfgWithOutput(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return new CommandResult(false, $"无法启动 powercfg: {arguments}");

            // 同 RunPowerCfg: 先起异步读取, 再等退出, 超时才能真正生效
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(PowerCfgTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new CommandResult(false, $"powercfg 超时 ({PowerCfgTimeoutMs}ms): {arguments}");
            }

            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 1000);
            string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
            string error = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";

            if (process.ExitCode != 0)
                return new CommandResult(false, $"powercfg {arguments} 失败: {error}");

            return new CommandResult(true, "ok", output);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, $"执行 powercfg 异常: {ex.Message}");
        }
    }

    private bool TryParseLastTwoHex(string output, out uint ac, out uint dc)
    {
        ac = 0;
        dc = 0;
        var matches = Regex.Matches(output, @"0x[0-9a-fA-F]+");
        if (matches.Count < 2)
            return false;
        ac = Convert.ToUInt32(matches[matches.Count - 2].Value, 16);
        dc = Convert.ToUInt32(matches[matches.Count - 1].Value, 16);
        return true;
    }
}
