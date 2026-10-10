using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;

namespace JiaoLongControl.Server.Core.Controllers
{
    public struct CpuStatsInfo
    {
        public int Temperature { get; set; }
        public int Usage { get; set; }
        public int FrequencyMhz { get; set; }
        public double Voltage { get; set; }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class CpuController : IDisposable
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _cpuFreqCounter;

        public CpuController()
        {
            Task.Run(() =>
            {
                try
                {
                    var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    counter.NextValue();
                    _cpuCounter = counter;

                    var freqCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                    freqCounter.NextValue();
                    _cpuFreqCounter = freqCounter;
                }
                catch { }
            });
        }

        /// <summary>圆口 AC 路径是否可用（与 SetCustomMode 门闩一致）。</summary>
        public bool IsAcReadyForCustomMode()
        {
            byte ac = MethodServices.GetValue<byte>(MethodName.SystemAcType);
            return ac != 0 && ac != byte.MaxValue;
        }

        /// <summary>
        /// 下发 SPL/SPPT/温度墙前确保 EC 自定义功耗模式已打开。
        /// 策略：功耗墙以 EC CustomMode 为准；非 AC 不强制开门（避免电池/Type-C 刷失败）。
        /// </summary>
        /// <returns>已处于 Open 或成功开门则为 true；非 AC / 开门失败为 false。</returns>
        public bool EnsureCustomModeOpen()
        {
            if (!IsAcReadyForCustomMode())
                return false;

            try
            {
                var current = MethodServices.GetValue<CPUPower>(MethodName.CPUPower);
                if (current == CPUPower.OpenState)
                    return true;
                return MethodServices.SetValue(MethodName.CPUPower, CPUPower.OpenState);
            }
            catch
            {
                return MethodServices.SetValue(MethodName.CPUPower, CPUPower.OpenState);
            }
        }

        public CommandResult SetCpuShortPower(byte sp)
        {
            if (!EnsureCustomModeOpen())
                return new CommandResult(false, "非圆口 AC，跳过短期功耗写入");
            var res = MethodServices.SetValue(MethodName.CPUPower, new byte[2]
            {
                (byte)CPUPower.ShortPower,
                sp
            });
            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        public CommandResult SetCpuLongPower(byte lp)
        {
            if (!EnsureCustomModeOpen())
                return new CommandResult(false, "非圆口 AC，跳过长期功耗写入");
            var res = MethodServices.SetValue(MethodName.CPUPower, new byte[2]
            {
                (byte)CPUPower.LongPower,
                lp
            });
            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        /// <summary>
        /// 供电状态：CustomMode 需圆口 AC（官方/ACPI 另校验适配器约 &gt;200W，Type-C 通常不可用）。
        /// </summary>
        public CommandResult GetAcPowerState()
        {
            try
            {
                byte ac = MethodServices.GetValue<byte>(MethodName.SystemAcType);
                // 与 ResultState 对齐：0=电池/非 AC，非 0 视为在 AC 路径上
                bool onAc = ac != 0 && ac != byte.MaxValue;
                bool canCustom = onAc;
                string hint = !onAc
                    ? "当前非 AC 供电（或 Type-C）。自定义功耗模式需圆口大功率适配器（约 &gt;200W）"
                    : "已检测到 AC 供电；若仍无法开自定义模式，请确认使用圆口适配器且功率充足";
                return new CommandResult(true, hint, new
                {
                    OnAc = onAc,
                    AcRaw = ac,
                    CanUseCustomMode = canCustom,
                    Hint = hint
                });
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public CommandResult SetCustomMode(bool open)
        {
            if (open)
            {
                byte ac = MethodServices.GetValue<byte>(MethodName.SystemAcType);
                if (ac == 0 || ac == byte.MaxValue)
                    return new CommandResult(false,
                        "无法开启自定义功耗模式：请使用圆口 DC 大功率适配器（勿用 Type-C/电池）。固件另要求适配器约 >200W。");

                var res = MethodServices.SetValue(MethodName.CPUPower, CPUPower.OpenState);
                return new CommandResult(res, res ? "已开启自定义功耗模式" : "开启失败（请确认圆口 AC 且功率充足）");
            }

            var close = MethodServices.SetValue(MethodName.CPUPower, CPUPower.CloseState);
            return new CommandResult(close, close ? "已关闭自定义功耗模式" : "关闭失败");
        }

        public CommandResult SetCPUTempWall(byte tw)
        {
            if (!EnsureCustomModeOpen())
                return new CommandResult(false, "非圆口 AC，跳过温度墙写入");
            var res = MethodServices.SetValue(MethodName.CPUPower, new byte[2]
            {
                (byte)CPUPower.CpuTempWallState,
                tw
            });
            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        /// <summary>Success=读成功；Data=是否已开启自定义功耗模式。Unknow 视为读失败。</summary>
        public CommandResult GetCustomMode()
        {
            try
            {
                var res = MethodServices.GetValue<CPUPower>(MethodName.CPUPower);
                if (res == CPUPower.Unknow)
                    return new CommandResult(false, "无法读取自定义功耗模式状态", false);
                bool open = res == CPUPower.OpenState;
                return new CommandResult(true, open ? "已开启" : "已关闭", open);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message, false);
            }
        }

        /// <summary>
        /// UI 已写完硬件后：只同步保活目标并结束 QuietDaily（不 Restore，避免覆盖刚应用的值）。
        /// </summary>
        public CommandResult SyncPowerKeepAlive(byte longPower, byte shortPower, byte tempWall) =>
            SyncPowerKeepAliveCore(longPower, shortPower, tempWall, restoreHardware: false);

        /// <summary>
        /// 重置等路径：结束 QuietDaily 并按 Active 档重下发墙/频率/软调度。
        /// </summary>
        public CommandResult SyncPowerKeepAliveAndRestore(byte longPower, byte shortPower, byte tempWall) =>
            SyncPowerKeepAliveCore(longPower, shortPower, tempWall, restoreHardware: true);

        private CommandResult SyncPowerKeepAliveCore(
            byte longPower, byte shortPower, byte tempWall, bool restoreHardware)
        {
            try
            {
                bool hadQuiet = Bridge.Instance.CpuPowerKeepAlive.HasTransientOverride;
                Bridge.Instance.CpuPowerKeepAlive.UpdateTarget(longPower, shortPower, tempWall, clearTransient: true);
                if (Bridge.Instance.Config.App.CpuPowerKeepAliveEnabled &&
                    !Bridge.Instance.CpuPowerKeepAlive.IsRunning)
                    Bridge.Instance.CpuPowerKeepAlive.Start();
                if (hadQuiet)
                {
                    if (restoreHardware)
                        Bridge.Instance.RestoreActiveCpuProfileHardware();
                    Bridge.Instance.NotifyQuietDailyChanged(active: false);
                }
                return new CommandResult(true, "保活目标已同步");
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public CommandResult GetCpuUsage()
        {
            try
            {
                int usage = _cpuCounter != null ? (int)_cpuCounter.NextValue() : 0;
                return new CommandResult(true, "获取成功", usage);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public CommandResult GetCpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    return new CommandResult(true, "获取成功", new
                    {
                        Name = obj["Name"]?.ToString()?.Trim() ?? "Unknown",
                        Cores = Convert.ToInt32(obj["NumberOfCores"]),
                        Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]),
                        BaseFreqMhz = Convert.ToInt32(obj["MaxClockSpeed"])
                    });
                }
                return new CommandResult(false, "无法获取 CPU 信息");
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public CommandResult GetCpuFrequency()
        {
            try
            {
                float perfPercent = _cpuFreqCounter.NextValue();
                int freqMhz = (int)(perfPercent / 100 * GetBaseFrequency());
                return new CommandResult(true, "获取成功", freqMhz);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public CommandResult GetCpuVoltage()
        {
            try
            {
                var voltage = ReadCoreVoltage();
                if (voltage.HasValue)
                    return new CommandResult(true, "获取成功", Math.Round(voltage.Value, 3));
                using var searcher = new ManagementObjectSearcher("SELECT CurrentVoltage FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    ushort raw = Convert.ToUInt16(obj["CurrentVoltage"]);
                    if (raw > 0)
                    {
                        double volts = (raw & 0xFF) / 10.0;
                        return new CommandResult(true, "获取成功", Math.Round(volts, 3));
                    }
                }
                return new CommandResult(false, "CPU 电压不可用");
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }
        
        private static double? ReadCoreVoltage()
        {
            try
            {
                var v = Interop.Bridge.Instance.RyzenSmu.GetCoreVoltage();
                if (v.HasValue)
                    return v.Value;
            }
            catch
            {

            }

            return ReadCpuVoltageFromLhm();
        }

        private static LibreHardwareMonitor.Hardware.Computer? _lhmComputer;
        private static readonly object _lhmLock = new();

        private static double? ReadCpuVoltageFromLhm()
        {
            try
            {
                if (_lhmComputer == null)
                {
                    lock (_lhmLock)
                    {
                        if (_lhmComputer == null)
                        {
                            var computer = new LibreHardwareMonitor.Hardware.Computer
                            {
                                IsCpuEnabled = true,
                            };
                            computer.Open();
                            _lhmComputer = computer;
                        }
                    }
                }

                var vcoreValues = new List<double>();

                foreach (var hardware in _lhmComputer.Hardware)
                {
                    if (hardware.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                        continue;

                    hardware.Update();

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Voltage &&
                            sensor.Value.HasValue &&
                            (sensor.Name.Contains("Vcore") || sensor.Name.Contains("Core") && sensor.Name.Contains("VID")))
                        {
                            vcoreValues.Add(sensor.Value.Value);
                        }
                    }
                }
                return vcoreValues.Count > 0 ? vcoreValues.Average() : null;
            }
            catch
            {
                return null;
            }
        }

        private uint GetBaseFrequency()
        {
            using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
                return Convert.ToUInt32(obj["MaxClockSpeed"]);
            return 3000;
        }

        public CommandResult GetCPUThermometer()
        {
            var res = MethodServices.GetValue<byte>(MethodName.CPUThermometer);
            return new CommandResult(true, $"读取成功", res);
        }

        public CommandResult GetCpuAllStats()
        {
            try
            {
                var stats = new CpuStatsInfo();
                stats.Temperature = MethodServices.GetValue<byte>(MethodName.CPUThermometer);
                stats.Usage = _cpuCounter != null ? (int)_cpuCounter.NextValue() : 0;
                float perfPercent = _cpuFreqCounter != null ? _cpuFreqCounter.NextValue() : 100f;
                stats.FrequencyMhz = (int)(perfPercent / 100 * GetBaseFrequency());
                var voltage = ReadCoreVoltage();
                if (voltage.HasValue)
                {
                    stats.Voltage = Math.Round(voltage.Value, 3);
                }
                else
                {
                    using var searcher2 = new ManagementObjectSearcher("SELECT CurrentVoltage FROM Win32_Processor");
                    foreach (var obj2 in searcher2.Get())
                    {
                        ushort raw2 = Convert.ToUInt16(obj2["CurrentVoltage"]);
                        if (raw2 > 0)
                        {
                            stats.Voltage = Math.Round((raw2 & 0xFF) / 10.0, 3);
                            break;
                        }
                    }
                }
                
                return new CommandResult(true, "获取成功", stats);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }


        public CommandResult GetPhysicalCoreCount()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT NumberOfCores FROM Win32_Processor");
                int total = 0;
                foreach (var obj in searcher.Get())
                    total += Convert.ToInt32(obj["NumberOfCores"]);
                return new CommandResult(true, "获取成功", total);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, ex.Message);
            }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _cpuFreqCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
