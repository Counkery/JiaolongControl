using System.IO;
using System.Text.Json;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Utils;

/// <summary>
/// 从闭源「第三方蛟龙游戏控制中心」AppSettings.json 迁移风扇%/功耗/温度墙等到 JiaoLongConfig。
/// </summary>
public static class AppSettingsImporter
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(AppSettingsImporter));

    private sealed class LegacyAppSettings
    {
        public int FastestModeFanSpeedSaveValue { get; set; }
        public int PlayingModeFanSpeedSaveValue { get; set; }
        public int WorkModeFanSpeedSaveValue { get; set; }
        public int CpuTempWallData { get; set; }
        public int SplData { get; set; }
        public int SpptData { get; set; }
        public int CPUPowerConsumptionValue { get; set; }
        public string? CpuTemperatureWall { get; set; }
    }

    /// <summary>
    /// 导入并写回 Bridge 配置。fanPercent 按约 0–100 映射到 0–5800 RPM。
    /// </summary>
    public static CommandResult ImportFromFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new CommandResult(false, $"文件不存在: {path}");

            var json = File.ReadAllText(path);
            var legacy = JsonSerializer.Deserialize<LegacyAppSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (legacy == null)
                return new CommandResult(false, "无法解析 AppSettings.json");

            var cfg = Bridge.Instance.Config;
            cfg.Fan.QuietModeFanRpm = PercentToRpm(legacy.WorkModeFanSpeedSaveValue);
            cfg.Fan.BalanceModeFanRpm = PercentToRpm(legacy.PlayingModeFanSpeedSaveValue);
            cfg.Fan.PerformanceModeFanRpm = PercentToRpm(legacy.FastestModeFanSpeedSaveValue);
            cfg.Fan.ManualFanSpeed = cfg.Fan.BalanceModeFanRpm;
            cfg.Fan.ApplyFanOnModeChange = true;

            byte wall = (byte)Math.Clamp(
                legacy.CpuTempWallData > 0
                    ? legacy.CpuTempWallData
                    : (byte.TryParse(legacy.CpuTemperatureWall, out var w) ? w : 95),
                60, 105);

            // SPL≈长期, SPPT≈短期；若只有 CPUPowerConsumptionValue 则两边都用它
            byte longP = (byte)Math.Clamp(
                legacy.SplData > 0 ? legacy.SplData : legacy.CPUPowerConsumptionValue, 5, 120);
            byte shortP = (byte)Math.Clamp(
                legacy.SpptData > 0 ? legacy.SpptData : Math.Max(longP, legacy.CPUPowerConsumptionValue), 5, 150);

            cfg.Cpu.Custom.CpuLongPower = longP;
            cfg.Cpu.Custom.CpuShortPower = shortP;
            cfg.Cpu.Custom.CpuTempWall = wall;
            cfg.Cpu.CpuProfile = "custom";

            Bridge.Instance.ApplyConfig(cfg);
            Bridge.Instance.FlushIfDirty();

            Logger.Info(
                $"已从 AppSettings 导入: Quiet={cfg.Fan.QuietModeFanRpm}rpm, " +
                $"Balance={cfg.Fan.BalanceModeFanRpm}rpm, Perf={cfg.Fan.PerformanceModeFanRpm}rpm, " +
                $"L={longP}W S={shortP}W T={wall}℃");

            return new CommandResult(true, "导入成功", new
            {
                cfg.Fan.QuietModeFanRpm,
                cfg.Fan.BalanceModeFanRpm,
                cfg.Fan.PerformanceModeFanRpm,
                LongPower = longP,
                ShortPower = shortP,
                TempWall = wall
            });
        }
        catch (Exception ex)
        {
            Logger.Error("导入 AppSettings 失败: " + ex.Message, ex);
            return new CommandResult(false, ex.Message);
        }
    }

    /// <summary>尝试常见路径：参数 → 同目录旁的 JiaoLong7.3 → 用户传入。</summary>
    public static CommandResult ImportFromDefaultLegacyPath()
    {
        var candidates = new[]
        {
            Path.Combine(@"D:\Application\JiaoLong7.3", "AppSettings.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "JiaoLong7.3", "AppSettings.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "第三方蛟龙游戏控制中心", "AppSettings.json"),
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full))
                return ImportFromFile(full);
        }

        return new CommandResult(false, "未找到第三方 AppSettings.json，请调用 ImportLegacyAppSettings(path)");
    }

    private static int PercentToRpm(int percent)
    {
        // 第三方保存的是大致百分比；映射到本项目手动风扇常用 RPM 区间
        int p = Math.Clamp(percent, 0, 100);
        return (int)Math.Round(p / 100.0 * 5800);
    }
}
