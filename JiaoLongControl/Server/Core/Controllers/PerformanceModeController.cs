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

                TryStopAutoFan(bridge);
                // 只下发硬件上限，不改用户记住的 BalanceModeFanRpm
                var ceil = bridge.Fan.SetFanCeilingRpm(3500);

                var targetLong = (byte)78;
                var targetShort = (byte)108;
                var targetWall = (byte)93;
                var targetFreq = 4750u;
                var soft = SoftSchedulePresets.QuietDaily();

                var r1 = bridge.CPU.SetCpuLongPower(targetLong);
                var r2 = bridge.CPU.SetCpuShortPower(targetShort);
                var r3 = bridge.CPU.SetCPUTempWall(targetWall);
                var freq = bridge.Power.SetCPUMaxFrequency(targetFreq);
                var softRes = bridge.Power.ApplySoftSchedule(soft, turboEnabled: true);

                // Success / Active / 推送对齐：风扇上限与频率也纳入「完整成功」
                bool ok = ceil.Success && r1.Success && r2.Success && r3.Success &&
                          freq.Success && softRes.Success;
                bool heldPortable = false;
                if (ok)
                {
                    // 仅在安静日常完整写成功后，才把便携场景的 GPU 墙拉回配置值。
                    // 失败时不能拉回，否则 135W 砖上会突然回到 140W 并跳保护。
                    if (string.Equals(bridge.CpuPowerKeepAlive.TransientScene, "Portable135",
                            StringComparison.Ordinal))
                        bridge.RestoreGpuAfterPortableScene("Portable135");

                    bridge.CpuPowerKeepAlive.SetTransientOverride(
                        targetLong, targetShort, targetWall, soft, scene: "QuietDaily");
                    if (bridge.Config.App.CpuPowerKeepAliveEnabled && !bridge.CpuPowerKeepAlive.IsRunning)
                        bridge.CpuPowerKeepAlive.Start();
                    bridge.NotifyQuietDailyChanged(true, scene: "QuietDaily");
                }
                else
                {
                    var cleared = bridge.CpuPowerKeepAlive.ClearTransientOverride();
                    if (string.Equals(cleared, "Portable135", StringComparison.Ordinal))
                    {
                        heldPortable = true;
                        HoldPortable135(bridge);
                        bridge.NotifyQuietDailyChanged(true, scene: "Portable135");
                    }
                    else
                    {
                        bridge.RestoreActiveCpuProfileHardware();
                        bridge.NotifyQuietDailyChanged(false, scene: null);
                    }
                }

                string msg = ok
                    ? "已应用安静日常：均衡 + 上限 3500 RPM + 78/108W / 墙 93℃ / 频率 4750 + 软调度（未改游戏档配置）"
                    : heldPortable
                        ? "安静日常未写入，已保持便携 135W，没有拉回高功耗。"
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
                    QuietDailyActive = ok || heldPortable,
                    Scene = ok ? "QuietDaily" : heldPortable ? "Portable135" : null,
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

        /// <summary>
        /// 一键「便携 135W」：办公 Quiet + 低 CPU/GPU 墙，适配家里 135W 砖，避免过流保护。
        /// 预算约 CPU 45W + GPU 60W + 板载余量；不覆写用户 Cpu/Gpu 配置档，退出时 GPU 墙写回配置值。
        /// </summary>
        public CommandResult ApplyPortable135()
        {
            var osd = OsdController.Instance;
            osd?.BeginSuppressModeBindings();
            try
            {
                var bridge = Bridge.Instance;

                var modeOk = MethodServices.SetValue(MethodName.SystemPerMode, SystemPerMode.QuietMode);
                if (!modeOk)
                    return new CommandResult(false, "切换办公模式失败");

                osd?.NoteExpectedPerfMode(SystemPerMode.QuietMode);
                osd?.OnPerformanceModeChanged(
                    SystemPerMode.QuietMode, applyHardwareBindings: false);

                TryStopAutoFan(bridge);
                var ceil = bridge.Fan.SetFanCeilingRpm(2700);

                // 135W 砖：CPU+GPU 合计宜 ≤110W，再留显示/风扇/尖峰余量
                var targetLong = (byte)45;
                var targetShort = (byte)55;
                var targetWall = (byte)90;
                var targetFreq = 4500u;
                const int gpuWatts = 60;
                var soft = SoftSchedulePresets.Office();

                var r1 = bridge.CPU.SetCpuLongPower(targetLong);
                var r2 = bridge.CPU.SetCpuShortPower(targetShort);
                var r3 = bridge.CPU.SetCPUTempWall(targetWall);
                var freq = bridge.Power.SetCPUMaxFrequency(targetFreq);
                var softRes = bridge.Power.ApplySoftSchedule(soft, turboEnabled: true);
                var gpu = bridge.NvidiaGpu.SetPowerLimit(gpuWatts);
                int appliedGpu = gpu.Data is int gw ? gw : 0;
                // 驱动最低墙若仍高于 70W，135W 砖没有余量，不能当成已保护。
                bool gpuHeld = gpu.Success && appliedGpu > 0 && appliedGpu <= 70;

                // 135W 砖上固件可能拒绝 CustomMode（标称需约 >200W）。
                // 保护适配器靠频率上限 + GPU 墙；CPU 功耗墙是尽力而为。
                // 失败时不要把 GPU/CPU 回滚到高功耗档，否则小砖更容易跳保护。
                bool cpuOk = r1.Success && r2.Success && r3.Success;
                bool ok = freq.Success && softRes.Success && gpuHeld;
                if (ok)
                {
                    bridge.CpuPowerKeepAlive.SetTransientOverride(
                        targetLong, targetShort, targetWall, soft, scene: "Portable135");
                    if (bridge.Config.App.CpuPowerKeepAliveEnabled && !bridge.CpuPowerKeepAlive.IsRunning)
                        bridge.CpuPowerKeepAlive.Start();
                }

                bridge.NotifyQuietDailyChanged(ok, scene: ok ? "Portable135" : null);

                string msg = ok
                    ? (cpuOk
                        ? "已应用便携 135W：办公 + 上限 2700 RPM + 45/55W / 墙 90℃ / 频率 4500 + GPU 60W（未改配置档）"
                        : "已限制频率 4500 与 GPU 60W。CPU 功耗墙未写入（小功率适配器可能无法开自定义功耗），请勿再切到高功耗档。")
                    : $"未能压住适配器负载: 上限={ceil.Success}, Freq={freq.Success}, Soft={softRes.Success}, GPU={gpu.Message}, CPU={cpuOk}";
                Logger.Info(msg);
                return new CommandResult(ok, msg, new
                {
                    Mode = "Quiet",
                    FanCeilingRpm = 2700,
                    LongPower = targetLong,
                    ShortPower = targetShort,
                    TempWall = targetWall,
                    MaxFrequencyMhz = targetFreq,
                    GpuPowerLimit = appliedGpu > 0 ? appliedGpu : gpuWatts,
                    SoftSchedule = soft,
                    Scene = ok ? "Portable135" : null,
                    AdapterWatts = 135,
                    PreservedProfiles = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("ApplyPortable135 失败", ex);
                return new CommandResult(false, ex.Message);
            }
            finally
            {
                osd?.EndSuppressModeBindings();
            }
        }

        /// <summary>安静日常失败时保住便携 135W，避免 GPU/CPU 被拉回高功耗档。</summary>
        internal static void HoldPortable135(Bridge bridge)
        {
            try
            {
                bridge.NvidiaGpu.SetPowerLimit(60);
                bridge.Power.SetCPUMaxFrequency(4500);
                bridge.CpuPowerKeepAlive.SetTransientOverride(
                    45, 55, 90, SoftSchedulePresets.Office(), scene: "Portable135");
            }
            catch (Exception ex)
            {
                Logger.Warn($"保持便携 135W 失败: {ex.Message}");
            }
        }

        /// <summary>软件曲线会绕过 WMI 上限直写转速，场景切换前先停掉并交回 EC。</summary>
        private static void TryStopAutoFan(Bridge bridge)
        {
            try
            {
                var running = bridge.AutoFan.IsRunning();
                if (running.Success && running.Data is true)
                    bridge.AutoFan.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn($"停止软件风扇曲线失败: {ex.Message}");
            }
        }

        /// <summary>是否处于安静日常 / 便携 135W 等瞬时会话。</summary>
        public CommandResult GetQuietDailyActive()
        {
            try
            {
                var keep = Bridge.Instance.CpuPowerKeepAlive;
                bool active = keep.HasTransientOverride;
                string? scene = keep.TransientScene;
                var soft = keep.GetTransientSoftSchedule();
                string label = scene switch
                {
                    "Portable135" => "便携 135W 生效中",
                    "QuietDaily" => "安静日常生效中",
                    _ when active => "瞬时场景生效中",
                    _ => "未生效"
                };
                return new CommandResult(true, label, new
                {
                    Active = active,
                    Scene = scene,
                    SoftSchedule = soft
                });
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message, new { Active = false, Scene = (string?)null });
            }
        }
    }
}
