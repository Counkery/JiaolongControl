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
                string? priorScene = Bridge.Instance.CpuPowerKeepAlive.TransientScene;
                bool hadTransient = Bridge.Instance.CpuPowerKeepAlive.HasTransientOverride;
                var cleared = Bridge.Instance.CpuPowerKeepAlive.UpdateTarget(
                    longPower, shortPower, tempWall, clearTransient: true);
                Bridge.Instance.RestoreGpuAfterPortableScene(cleared ?? priorScene);
                if (Bridge.Instance.Config.App.CpuPowerKeepAliveEnabled &&
                    !Bridge.Instance.CpuPowerKeepAlive.IsRunning)
                    Bridge.Instance.CpuPowerKeepAlive.Start();
                if (hadTransient)
                {
                    if (restoreHardware)
                        Bridge.Instance.RestoreActiveCpuProfileHardware();
                    Bridge.Instance.NotifyQuietDailyChanged(active: false, scene: priorScene);
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
                RefreshLhmSnapshot();
                if (_cachedCoreFreqMhz is > 200)
                    return new CommandResult(true, "获取成功", (int)Math.Round(_cachedCoreFreqMhz.Value));

                if (_cpuFreqCounter == null)
                    return new CommandResult(false, "CPU 频率计数器未就绪", 0);
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
                var wmiVoltage = ReadWmiVoltage();
                if (wmiVoltage.HasValue)
                    return new CommandResult(true, "获取成功", Math.Round(wmiVoltage.Value, 3));
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

        private static LibreHardwareMonitor.Hardware.Computer? EnsureLhm()
        {
            if (_lhmComputer != null)
                return _lhmComputer;
            lock (_lhmLock)
            {
                if (_lhmComputer != null)
                    return _lhmComputer;
                var computer = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled = true,
                };
                computer.Open();
                _lhmComputer = computer;
                return _lhmComputer;
            }
        }

        /// <summary>CPU Package 功耗（瓦）。LHM 传感器名含 Package。</summary>
        public CommandResult GetCpuPackagePower()
        {
            var watts = ReadPackagePowerFromLhm();
            if (!watts.HasValue)
                return new CommandResult(false, "无法读取 CPU 封装功耗", 0d);
            return new CommandResult(true, "获取成功", Math.Round(watts.Value, 1));
        }

        private static long _lhmStamp;
        private static double? _cachedPackagePower;
        private static double? _cachedVcore;
        private static double? _cachedCoreFreqMhz;
        private static uint? _cachedBaseMhz;

        /// <summary>同一轮监控里功耗和电压共用一次硬件刷新。</summary>
        private static void RefreshLhmSnapshot()
        {
            if (Environment.TickCount64 - _lhmStamp < 800)
                return;
            var computer = EnsureLhm();
            if (computer == null)
                return;
            double? package = null;
            double? largestPower = null;
            var vcoreValues = new List<double>();
            var coreFreqs = new List<double>();
            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                    continue;
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue)
                        continue;
                    if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Power)
                    {
                        if (largestPower == null || sensor.Value.Value > largestPower)
                            largestPower = sensor.Value.Value;
                        if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensor.Name.Contains("PPT", StringComparison.OrdinalIgnoreCase))
                            package ??= sensor.Value.Value;
                    }
                    else if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Voltage &&
                             (sensor.Name.Contains("Vcore") ||
                              sensor.Name.Contains("Core") && sensor.Name.Contains("VID")))
                        vcoreValues.Add(sensor.Value.Value);
                    else if ((sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Clock ||
                              sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Frequency) &&
                             sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                             sensor.Value.Value > 200 && sensor.Value.Value < 10000)
                        coreFreqs.Add(sensor.Value.Value);
                }
            }

            _cachedPackagePower = package ?? largestPower;
            _cachedCoreFreqMhz = coreFreqs.Count > 0 ? coreFreqs.Average() : null;
            _cachedVcore = vcoreValues.Count > 0 ? vcoreValues.Average() : null;
            _lhmStamp = Environment.TickCount64;
        }

        private static double? ReadPackagePowerFromLhm()
        {
            try
            {
                RefreshLhmSnapshot();
                return _cachedPackagePower;
            }
            catch
            {
                return null;
            }
        }

        private static double? ReadCpuVoltageFromLhm()
        {
            try
            {
                RefreshLhmSnapshot();
                return _cachedVcore;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 读取 Win32_Processor.CurrentVoltage (单位 0.1V)。
        /// 该字段有两种编码: bit7 置位时电压在高字节, 否则在低 7 位。
        /// 早先直接取 (raw &amp; 0xFF) 会在 bit7 置位时读出 13.7V 之类的假值。
        /// </summary>
        private static double? ReadWmiVoltage()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentVoltage FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    ushort raw = Convert.ToUInt16(obj["CurrentVoltage"]);
                    if (raw == 0)
                        continue;
                    if ((raw & 0x80) != 0)
                        raw >>= 8;
                    else
                        raw &= 0x7F;
                    if (raw > 0)
                        return raw / 10.0;
                }
            }
            catch
            {
            }

            return null;
        }

        private uint GetBaseFrequency()
        {
            if (_cachedBaseMhz is > 0)
                return _cachedBaseMhz.Value;
            using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                _cachedBaseMhz = Convert.ToUInt32(obj["MaxClockSpeed"]);
                return _cachedBaseMhz.Value;
            }
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
                var freq = GetCpuFrequency();
                stats.FrequencyMhz = freq.Success && freq.Data is int mhz ? mhz : 0;
                var voltage = ReadCoreVoltage();
                if (voltage.HasValue)
                {
                    stats.Voltage = Math.Round(voltage.Value, 3);
                }
                else
                {
                    var wmiVoltage = ReadWmiVoltage();
                    if (wmiVoltage.HasValue)
                        stats.Voltage = Math.Round(wmiVoltage.Value, 3);
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
