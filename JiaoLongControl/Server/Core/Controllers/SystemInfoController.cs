using System.Diagnostics;
using System.Management;
using JiaoLongControl.Server.Core.Utils;

namespace JiaoLongControl.Server.Core.Controllers
{
    public class SystemInfoController
    {
        /// <summary>使用系统默认浏览器打开外部链接（仅允许 http/https）</summary>
        public CommandResult OpenUrl(string url)
        {
            try
            {
                // 参数为 null 时 url.Trim() 会先抛 NullReferenceException, 兜不住类型问题;
                // 先判空再 Trim, 保证返回的是明确的参数错误
                if (string.IsNullOrWhiteSpace(url))
                {
                    return new CommandResult(false, "无效的链接地址");
                }

                url = url.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return new CommandResult(false, "无效的链接地址");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                return new CommandResult(true, "已打开浏览器");
            }
            catch (Exception ex)
            {
                return new CommandResult(false, $"打开浏览器失败: {ex.Message}");
            }
        }

        public CommandResult GetSystemOverview()
        {
            var overview = new SystemOverview();

            try
            {
                // 获取 CPU 信息
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        overview.CpuName = item["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                        break;
                    }
                }

                // 获取 独立显卡 信息 (排除集显)
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var name = item["Name"]?.ToString() ?? "";
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || 
                            name.Contains("AMD Radeon RX", StringComparison.OrdinalIgnoreCase))
                        {
                            overview.GpuName = name;
                            break;
                        }
                        // 如果没有独显，记录第一个找到的显卡
                        if (string.IsNullOrEmpty(overview.GpuName) || overview.GpuName == "Unknown GPU")
                        {
                            overview.GpuName = name;
                        }
                    }
                }

                // 获取 操作系统 信息
                using (var searcher = new ManagementObjectSearcher("select Caption, Version from Win32_OperatingSystem"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var caption = item["Caption"]?.ToString()?.Replace("Microsoft", "").Trim();
                        overview.OsVersion = $"{caption}";
                        break;
                    }
                }

                // 获取 内存 信息
                long totalCapacity = 0;
                string speed = "";
                using (var searcher = new ManagementObjectSearcher("select Capacity, Speed from Win32_PhysicalMemory"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (long.TryParse(item["Capacity"]?.ToString(), out long capacity))
                        {
                            totalCapacity += capacity;
                        }
                        if (string.IsNullOrEmpty(speed) && item["Speed"] != null)
                        {
                            speed = item["Speed"]?.ToString() ?? "";
                        }
                    }
                }
                
                if (totalCapacity > 0)
                {
                    var gb = Math.Round((double)totalCapacity / (1024 * 1024 * 1024));
                    overview.MemoryInfo = $"{gb}GB {(string.IsNullOrEmpty(speed) ? "" : speed + "MHz")}".Trim();
                }

                return new CommandResult(true, "Success", overview);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, $"Failed to get system info: {ex.Message}");
            }
        }

        /// <summary>设备信息专页：CPU / GPU / 内存条 / 磁盘详情。</summary>
        public CommandResult GetDeviceHardwareInfo()
        {
            try
            {
                var info = new DeviceHardwareInfo();

                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, Manufacturer FROM Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        info.Cpu = new DeviceCpuInfo
                        {
                            Name = item["Name"]?.ToString()?.Trim() ?? "Unknown",
                            Manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? "",
                            Cores = SafeInt(item["NumberOfCores"]),
                            Threads = SafeInt(item["NumberOfLogicalProcessors"]),
                            BaseFreqMhz = SafeInt(item["MaxClockSpeed"]),
                        };
                        break;
                    }
                }

                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var name = item["Name"]?.ToString() ?? "";
                        var entry = new DeviceGpuInfo
                        {
                            Name = name,
                            DriverVersion = item["DriverVersion"]?.ToString() ?? "",
                            MemoryMb = item["AdapterRAM"] != null
                                ? (long)Math.Round(Convert.ToDouble(item["AdapterRAM"]) / (1024 * 1024))
                                : 0,
                        };
                        info.Gpus.Add(entry);
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("AMD Radeon RX", StringComparison.OrdinalIgnoreCase))
                        {
                            info.PrimaryGpu = entry;
                        }
                    }

                    if (info.PrimaryGpu == null && info.Gpus.Count > 0)
                        info.PrimaryGpu = info.Gpus[0];
                }

                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Capacity, Speed, Manufacturer, PartNumber, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    foreach (var item in searcher.Get())
                    {
                        long capacity = 0;
                        long.TryParse(item["Capacity"]?.ToString(), out capacity);
                        int speed = SafeInt(item["ConfiguredClockSpeed"]);
                        if (speed <= 0) speed = SafeInt(item["Speed"]);
                        info.MemoryModules.Add(new DeviceMemoryModule
                        {
                            CapacityGb = Math.Round(capacity / (1024.0 * 1024 * 1024), 1),
                            SpeedMhz = speed,
                            Manufacturer = (item["Manufacturer"]?.ToString() ?? "").Trim(),
                            PartNumber = (item["PartNumber"]?.ToString() ?? "").Trim(),
                        });
                        info.TotalMemoryGb += capacity / (1024.0 * 1024 * 1024);
                    }

                    info.TotalMemoryGb = Math.Round(info.TotalMemoryGb, 1);
                }

                // 可用/已用内存
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (ulong.TryParse(item["TotalVisibleMemorySize"]?.ToString(), out var totalKb) &&
                            ulong.TryParse(item["FreePhysicalMemory"]?.ToString(), out var freeKb) &&
                            totalKb > 0)
                        {
                            info.MemoryUsedPercent = Math.Round(100.0 * (totalKb - freeKb) / totalKb, 1);
                            info.MemoryUsedGb = Math.Round((totalKb - freeKb) / (1024.0 * 1024), 1);
                            info.MemoryFreeGb = Math.Round(freeKb / (1024.0 * 1024), 1);
                        }
                        break;
                    }
                }

                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive"))
                {
                    foreach (var item in searcher.Get())
                    {
                        long size = 0;
                        long.TryParse(item["Size"]?.ToString(), out size);
                        info.Disks.Add(new DeviceDiskInfo
                        {
                            Model = (item["Model"]?.ToString() ?? "Unknown").Trim(),
                            SizeGb = Math.Round(size / (1024.0 * 1024 * 1024), 1),
                            MediaType = item["MediaType"]?.ToString() ?? "",
                            InterfaceType = item["InterfaceType"]?.ToString() ?? "",
                        });
                    }
                }

                // 逻辑卷使用率
                using (var searcher = new ManagementObjectSearcher(
                           "SELECT Caption, Size, FreeSpace, FileSystem FROM Win32_LogicalDisk WHERE DriveType=3"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (!ulong.TryParse(item["Size"]?.ToString(), out var size) || size == 0)
                            continue;
                        ulong.TryParse(item["FreeSpace"]?.ToString(), out var free);
                        info.Volumes.Add(new DeviceVolumeInfo
                        {
                            Caption = item["Caption"]?.ToString() ?? "",
                            FileSystem = item["FileSystem"]?.ToString() ?? "",
                            SizeGb = Math.Round(size / (1024.0 * 1024 * 1024), 1),
                            FreeGb = Math.Round(free / (1024.0 * 1024 * 1024), 1),
                            UsedPercent = Math.Round(100.0 * (size - free) / size, 1),
                        });
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem"))
                {
                    foreach (var item in searcher.Get())
                    {
                        info.OsVersion = (item["Caption"]?.ToString() ?? "")
                            .Replace("Microsoft", "", StringComparison.OrdinalIgnoreCase).Trim();
                        info.OsBuild = item["Version"]?.ToString() ?? "";
                        break;
                    }
                }

                return new CommandResult(true, "获取成功", info);
            }
            catch (Exception ex)
            {
                return new CommandResult(false, $"获取设备信息失败: {ex.Message}");
            }
        }

        private static int SafeInt(object? value)
        {
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        public class SystemOverview
        {
            public string CpuName { get; set; } = "Unknown CPU";
            public string GpuName { get; set; } = "Unknown GPU";
            public string OsVersion { get; set; } = "Unknown OS";
            public string MemoryInfo { get; set; } = "Unknown Memory";
        }

        public class DeviceHardwareInfo
        {
            public DeviceCpuInfo Cpu { get; set; } = new();
            public DeviceGpuInfo? PrimaryGpu { get; set; }
            public List<DeviceGpuInfo> Gpus { get; set; } = new();
            public List<DeviceMemoryModule> MemoryModules { get; set; } = new();
            public double TotalMemoryGb { get; set; }
            public double MemoryUsedGb { get; set; }
            public double MemoryFreeGb { get; set; }
            public double MemoryUsedPercent { get; set; }
            public List<DeviceDiskInfo> Disks { get; set; } = new();
            public List<DeviceVolumeInfo> Volumes { get; set; } = new();
            public string OsVersion { get; set; } = "";
            public string OsBuild { get; set; } = "";
        }

        public class DeviceCpuInfo
        {
            public string Name { get; set; } = "";
            public string Manufacturer { get; set; } = "";
            public int Cores { get; set; }
            public int Threads { get; set; }
            public int BaseFreqMhz { get; set; }
        }

        public class DeviceGpuInfo
        {
            public string Name { get; set; } = "";
            public string DriverVersion { get; set; } = "";
            public long MemoryMb { get; set; }
        }

        public class DeviceMemoryModule
        {
            public double CapacityGb { get; set; }
            public int SpeedMhz { get; set; }
            public string Manufacturer { get; set; } = "";
            public string PartNumber { get; set; } = "";
        }

        public class DeviceDiskInfo
        {
            public string Model { get; set; } = "";
            public double SizeGb { get; set; }
            public string MediaType { get; set; } = "";
            public string InterfaceType { get; set; } = "";
        }

        public class DeviceVolumeInfo
        {
            public string Caption { get; set; } = "";
            public string FileSystem { get; set; } = "";
            public double SizeGb { get; set; }
            public double FreeGb { get; set; }
            public double UsedPercent { get; set; }
        }
    }
}
