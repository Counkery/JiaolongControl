using System.IO;
using JiaoLongControl.Server.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectGraphVisitors;
using Version = System.Version;

namespace JiaoLongControl.Server.Core.Utils;

public static class ConfigSerializer
{
    private const string FileName = "config.yaml";
    private const string BackupExt = ".bak";
    private const string TempExt = ".tmp";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithEmissionPhaseObjectGraphVisitor(args => new CommentsObjectGraphVisitor(args.InnerVisitor))
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    // 窗口尺寸记忆自该版本起统一为物理像素语义 (默认 1300×820 px, 不随 DPI/缩放放大)。
    // 更早版本固化的记忆值 (旧 DIP 字段 WindowWidth/Height, 或按 DIP 默认×缩放倍数
    // 算出的像素值, 如 150% 屏上的 1824×1026) 与真实用户调整无法区分, 升级时清零:
    // 首次启动回到新默认, 之后的手动调整正常记忆。
    private static readonly Version PixelSizeSemanticsVersion = new("10.19.31");

    // 自该版本起写入 7945HX 办公/游戏/生产力三档出厂预设 (Saving/Default/Performance)。
    private static readonly Version CpuPresets7945HxVersion = new("10.19.34");

    // 自该版本起为三档写入 SoftSchedule（EPP / CPMIN / Boost，AC·DC）。
    private static readonly Version SoftScheduleVersion = new("10.19.38");
    private static readonly Version OfficeSweetSpotVersion = new("10.19.53");
    // 自该版本起 OSD 默认：Custom 50%/96%、出入场 200ms、显示 500ms、缩放 100%、轮询 500ms。
    private static readonly Version OsdLayoutDefaultsVersion = new("10.19.61");
    // 10.19.61 曾把 Version 先写成新号导致 CustomY=8 未迁到 96，此版本补迁。
    private static readonly Version OsdCustomY96Version = new("10.19.62");
    // 更快的出入场与轮询默认 (动画编排亦改为贴近原生)。
    private static readonly Version OsdSnappyVersion = new("10.19.65");
    // 出入场改为固定时长纯淡入淡出, 默认更干脆。
    private static readonly Version OsdInstantVersion = new("10.19.67");
    // 静音档：CPU 上限 4.75GHz、风扇上限 3000RPM（仅覆盖旧默认值）。
    private static readonly Version QuietTuneVersion = new("10.19.71");
    // 默认 CPU 档改为静音（saving）；旧配置仍停在 default 的迁过去。
    private static readonly Version QuietDefaultProfileVersion = new("10.19.72");

    public static string ConfigDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "config");
    public static string ConfigPath => Path.Combine(ConfigDir, FileName);
    private static string TempPath => ConfigPath + TempExt;
    private static string BackupPath => ConfigPath + BackupExt;

    public static string Serialize<T>(T config)
    {
        return Serializer.Serialize(config);
    }

    public static string? ReadFileContent()
    {
        if (!File.Exists(ConfigPath))
            return null;
        return File.ReadAllText(ConfigPath);
    }

    public static JiaoLongConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new JiaoLongConfig();

        try
        {
            var yaml = File.ReadAllText(ConfigPath);
            return Deserializer.Deserialize<JiaoLongConfig>(yaml) ?? new JiaoLongConfig();
        }
        catch (Exception)
        {
            if (File.Exists(BackupPath))
            {
                try
                {
                    var yaml = File.ReadAllText(BackupPath);
                    return Deserializer.Deserialize<JiaoLongConfig>(yaml) ?? new JiaoLongConfig();
                }
                catch
                {
                    // 原始文件和备份文件均读取失败
                }
            }

            return new JiaoLongConfig();
        }
    }

    public static void Save(JiaoLongConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var yaml = Serialize(config);
        if (File.Exists(ConfigPath))
            File.Copy(ConfigPath, BackupPath, overwrite: true);
        File.WriteAllText(TempPath, yaml);
        File.Move(TempPath, ConfigPath, overwrite: true);
        if (File.Exists(TempPath))
            File.Delete(TempPath);
    }

    public static void Initialize(string version)
    {
        // 如果配置文件不存在
        if (!File.Exists(ConfigPath))
        {
            Save(new JiaoLongConfig { Version = version });
        }

        var existing = Load();
        // 版本字段缺失: 旧配置无法迁移, 删除后按当前版本重建, 并且不要再拿旧对象 Update 覆盖
        if (string.IsNullOrWhiteSpace(existing.Version))
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
            Save(new JiaoLongConfig { Version = version });
            return;
        }
        // 如果配置文件存在但版本不一致
        if (existing.Version != version )
        {
            Update(existing, version);
        }
    }

    public static void Update(JiaoLongConfig LowConfig, string version)
    {
        // 版本号缺失或解析失败按旧版本处理
        if (!Version.TryParse(LowConfig.Version, out var oldVersion) ||
            oldVersion < PixelSizeSemanticsVersion)
        {
            LowConfig.App.WindowPixelWidth = 0;
            LowConfig.App.WindowPixelHeight = 0;
            LowConfig.App.WindowWidth = 0;
            LowConfig.App.WindowHeight = 0;
        }

        // 升级到 10.19.34+：刷新三档出厂预设；Custom 与当前选中档位名保留
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < CpuPresets7945HxVersion)
        {
            LowConfig.Cpu.Saving = CpuProfilePresets.Office();
            LowConfig.Cpu.Default = CpuProfilePresets.Game();
            LowConfig.Cpu.Performance = CpuProfilePresets.Productivity();
            LowConfig.App.ApplyCpuProfileOnModeChange = true;
            if (LowConfig.Smu.CurveOptimizerAll == 0)
                LowConfig.Smu.CurveOptimizerAll = -12;
        }

        // 升级到 10.19.38+：仅刷新软调度，保留用户已调的 SPL/频率等
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < SoftScheduleVersion)
        {
            LowConfig.Cpu.Saving.SoftSchedule = SoftSchedulePresets.Office();
            LowConfig.Cpu.Default.SoftSchedule = SoftSchedulePresets.Game();
            LowConfig.Cpu.Performance.SoftSchedule = SoftSchedulePresets.Productivity();
            LowConfig.Cpu.Custom.SoftSchedule ??= SoftSchedulePresets.Game();
        }

        // 升级到 10.19.53+：办公档若仍是旧默认 4400/45W，收到 4500。其它手调频率不动。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < OfficeSweetSpotVersion)
        {
            var saving = LowConfig.Cpu.Saving;
            if (saving.CpuMaxFrequency == 4400 && saving.CpuLongPower == 45)
                saving.CpuMaxFrequency = 4500;
        }

        // 升级到 10.19.61+：刷新 OSD 布局/时长默认；用户已改过的项保留。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < OsdLayoutDefaultsVersion)
        {
            var osd = LowConfig.Osd;
            bool fromPreset = osd.Position is "TopCenter" or "BottomCenter";
            if (fromPreset)
            {
                osd.Position = "Custom";
                osd.CustomX = 50;
                osd.CustomY = 96;
            }
            else if (osd.CustomY is 8 or 0)
            {
                osd.CustomY = 96;
            }

            if (osd.AnimationMs is 480 or 400)
                osd.AnimationMs = 200;
            if (osd.DurationMs is 2000 or 1500)
                osd.DurationMs = 500;
            if (osd.Scale is 150 or 125)
                osd.Scale = 100;
            if (osd.PollIntervalMs is 1000 or 800)
                osd.PollIntervalMs = 500;
        }

        // 升级到 10.19.62+：旧默认 CustomY=8（顶部附近）一律收到 96%。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < OsdCustomY96Version)
        {
            if (LowConfig.Osd.CustomY == 8)
                LowConfig.Osd.CustomY = 96;
        }

        // 升级到 10.19.65+：出入场/轮询收到更快默认；手调过的保留。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < OsdSnappyVersion)
        {
            var osd = LowConfig.Osd;
            if (osd.AnimationMs is 200 or 480 or 400)
                osd.AnimationMs = 120;
            if (osd.PollIntervalMs is 500 or 1000 or 800)
                osd.PollIntervalMs = 200;
        }

        // 升级到 10.19.67+：出入场收到更干脆默认；手调过的保留。PollIntervalMs 已是 200 则不动。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < OsdInstantVersion)
        {
            var osd = LowConfig.Osd;
            if (osd.AnimationMs is 120 or 200 or 480 or 400)
                osd.AnimationMs = 80;
        }

        // 升级到 10.19.71+：静音档旧默认 4500/4400 → 4750；风扇旧默认 2700 → 3000。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < QuietTuneVersion)
        {
            var saving = LowConfig.Cpu.Saving;
            if (saving.CpuLongPower == 45 && saving.CpuMaxFrequency is 4500 or 4400)
                saving.CpuMaxFrequency = 4750;
            if (LowConfig.Fan.QuietModeFanRpm == 2700)
                LowConfig.Fan.QuietModeFanRpm = 3000;
        }

        // 升级到 10.19.72+：默认档改为静音；仍停在出厂 default 的改为 saving。
        if (!Version.TryParse(LowConfig.Version, out oldVersion) || oldVersion < QuietDefaultProfileVersion)
        {
            if (LowConfig.Cpu.CpuProfile is "default" or null or "")
                LowConfig.Cpu.CpuProfile = "saving";
        }

        // 默认无损迁移
        LowConfig.Version = version;
        Save(LowConfig);
    }

    private class CommentsObjectGraphVisitor : ChainedObjectGraphVisitor
    {
        public CommentsObjectGraphVisitor(IObjectGraphVisitor<IEmitter> nextVisitor)
            : base(nextVisitor)
        {
        }

        public override bool EnterMapping(IPropertyDescriptor key, IObjectDescriptor value, IEmitter context,
            ObjectSerializer serializer)
        {
            var commentAttr = key.GetCustomAttribute<ConfigCommentAttribute>();
            if (commentAttr != null)
            {
                context.Emit(new Comment(commentAttr.Comment, isInline: false));
            }

            var rangeAttr = key.GetCustomAttribute<ConfigRangeAttribute>();
            if (rangeAttr != null)
            {
                context.Emit(new Comment($"范围: {rangeAttr.Min} ~ {rangeAttr.Max}", isInline: false));
            }

            return base.EnterMapping(key, value, context, serializer);
        }
    }
}