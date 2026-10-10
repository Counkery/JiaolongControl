using System.Runtime.InteropServices;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Controllers
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class PerformanceModeController
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(PerformanceModeController));

        public CommandResult Get()
        {
            return new CommandResult(true, "获取成功",
                MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode));
        }

        public CommandResult Set(SystemPerMode mode)
        {
            var previous = MethodServices.GetValue<SystemPerMode>(MethodName.SystemPerMode);
            if (previous == SystemPerMode.Unknow)
            {
                // 读失败：仍尝试写入，但强制走绑定，避免误判 sameMode
                var write = MethodServices.SetValue(MethodName.SystemPerMode, mode);
                if (!write)
                    return new CommandResult(false, "设置失败（且无法读取当前模式）");
                OsdController.Instance?.OnPerformanceModeChanged(mode, applyHardwareBindings: true);
                return new CommandResult(true, "设置成功（先前模式未知）", new
                {
                    Mode = mode.ToString(),
                    SameMode = false,
                    BindingsApplied = true,
                    PreviousUnknown = true
                });
            }

            var res = MethodServices.SetValue(MethodName.SystemPerMode, mode);
            if (!res)
                return new CommandResult(false, "设置失败");

            bool sameMode = previous == mode;
            OsdController.Instance?.OnPerformanceModeChanged(
                mode, applyHardwareBindings: !sameMode);

            return new CommandResult(true, sameMode ? "已是当前模式" : "设置成功", new
            {
                Mode = mode.ToString(),
                SameMode = sameMode,
                BindingsApplied = !sameMode
            });
        }

        /// <summary>
        /// 一键「安静日常」：均衡模式 + WMI 转速上限 3500 + 游戏档功耗但温度墙 93℃ + QuietDaily 软调度。
        /// 不覆写用户 Cpu.Default；切模式时跳过绑定，由本方法一次性下发目标。
        /// </summary>
        public CommandResult ApplyQuietDaily()
        {
            var osd = OsdController.Instance;
            osd?.BeginSuppressModeBindings();
            try
            {
                var bridge = Bridge.Instance;

                // 先写 EC，成功后再抬 Detect 基线，避免 SetValue 失败污染 _lastPerf
                var modeOk = MethodServices.SetValue(MethodName.SystemPerMode, SystemPerMode.BalanceMode);
                if (!modeOk)
                    return new CommandResult(false, "切换均衡模式失败");

                osd?.NoteExpectedPerfMode(SystemPerMode.BalanceMode);
                osd?.OnPerformanceModeChanged(
                    SystemPerMode.BalanceMode, applyHardwareBindings: false);

                bridge.Config.Fan.BalanceModeFanRpm = 3500;
                var ceil = bridge.Fan.SetFanCeilingRpm(3500);

                var targetLong = (byte)78;
                var targetShort = (byte)108;
                var targetWall = (byte)93;
                var targetFreq = 5150u;
                var soft = SoftSchedulePresets.QuietDaily();

                bridge.Config.Cpu.CpuProfile = "default";

                var r1 = bridge.CPU.SetCpuLongPower(targetLong);
                var r2 = bridge.CPU.SetCpuShortPower(targetShort);
                var r3 = bridge.CPU.SetCPUTempWall(targetWall);
                var freq = bridge.Power.SetCPUMaxFrequency(targetFreq);
                var softRes = bridge.Power.ApplySoftSchedule(soft, turboEnabled: true);

                // Success / Active / 推送对齐：风扇上限与频率也纳入「完整成功」
                bool ok = ceil.Success && r1.Success && r2.Success && r3.Success &&
                          freq.Success && softRes.Success;
                if (ok)
                {
                    bridge.CpuPowerKeepAlive.SetTransientOverride(
                        targetLong, targetShort, targetWall, soft);
                    if (bridge.Config.App.CpuPowerKeepAliveEnabled && !bridge.CpuPowerKeepAlive.IsRunning)
                        bridge.CpuPowerKeepAlive.Start();
                }
                else
                {
                    // 半失败：清瞬时并立刻按 Active 档回滚墙/软调度，避免残留 QuietDaily 参数
                    bridge.CpuPowerKeepAlive.ClearTransientOverride();
                    bridge.RestoreActiveCpuProfileHardware();
                }

                bridge.FlushIfDirty();
                // 服务端改了 Fan/CpuProfile：推前端 store，避免旧配置回写
                bridge.NotifyConfigChanged();
                bridge.NotifyQuietDailyChanged(ok);

                string msg = ok
                    ? "已应用安静日常：均衡 + 上限 3500 RPM + 78/108W / 墙 93℃ + 软调度（未改游戏档配置）"
                    : $"部分失败: 上限={ceil.Success}, L={r1.Success}, S={r2.Success}, T={r3.Success}, Freq={freq.Success}, Soft={softRes.Success}";
                Logger.Info(msg);
                return new CommandResult(ok, msg, new
                {
                    Mode = "Balance",
                    FanCeilingRpm = 3500,
                    LongPower = targetLong,
                    ShortPower = targetShort,
                    TempWall = targetWall,
                    SoftSchedule = soft,
                    QuietDailyActive = ok,
                    PreservedDefaultProfile = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("ApplyQuietDaily 失败", ex);
                return new CommandResult(false, ex.Message);
            }
            finally
            {
                osd?.EndSuppressModeBindings();
            }
        }

        /// <summary>是否处于安静日常瞬时会话（保活/软调度覆盖中）。</summary>
        public CommandResult GetQuietDailyActive()
        {
            try
            {
                bool active = Bridge.Instance.CpuPowerKeepAlive.HasTransientOverride;
                var soft = Bridge.Instance.CpuPowerKeepAlive.GetTransientSoftSchedule();
                return new CommandResult(true, active ? "安静日常生效中" : "未生效", new
                {
                    Active = active,
                    SoftSchedule = soft
                });
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message, new { Active = false });
            }
        }
    }
}
