using YamlDotNet.Serialization;

namespace JiaoLongControl.Server.Core.Models;

public class JiaoLongConfig
{
    public string Version { get; set; } = "";
    public AppSection App { get; set; } = new();
    public CpuSection Cpu { get; set; } = new();
    public GpuSection Gpu { get; set; } = new();
    public FanSection Fan { get; set; } = new();
    public SmuSection Smu { get; set; } = new();
    public OsdSection Osd { get; set; } = new();
}

public class AppSection
{
    [ConfigComment("开机后最小化到托盘")]
    public bool BootMinimized { get; set; }

    [ConfigComment("开机自动启动高级风扇控制系统")]
    public bool BootAdvancedFanControlSystem { get; set; }

    [ConfigComment("开机自动启动高级CPU系统")]
    public bool BootAdvancedCPUSystem { get; set; }

    [ConfigComment("开机自动启动高级GPU系统")]
    public bool BootAdvancedGPUSystem { get; set; }

    [ConfigComment("开机自动设置Ryzen SMU Curve Optimizer All")]
    public bool BootSetRyzenSumCurveOptimizerAll { get; set; }

    [ConfigComment("开机自动应用Ryzen SMU Curve Optimizer时使用分核降压 (代替全核)")]
    public bool BootSetRyzenSmuCurveOptimizerPerCore { get; set; }

    [ConfigComment("开机自动开启键盘渐变")]
    public bool BootKeyboardGradient { get; set; }

    [ConfigComment("启用后 CPU 页面的核心电压偏移替换为分核心降压 (应用时逐核心写入 Curve Optimizer Per Core 数值)")]
    public bool CpuCurveOptimizerPerCore { get; set; }

    [ConfigComment("开机自动应用GPU参数时改用高级超频数值 (频率偏移代替频率锁定)")]
    public bool BootGpuUseAdvancedOffsets { get; set; }

    [ConfigComment("界面主题: light / dark / system (默认跟随系统)")]
    public string Theme { get; set; } = "system";

    [ConfigComment("启用 CPU 功耗/温度墙周期保活 (与监控采集分离, 默认开启, 间隔约 30s)")]
    public bool CpuPowerKeepAliveEnabled { get; set; } = true;

    [ConfigComment("优先使用 PawnIO 驱动路径 (避免 WinRing0); 仅作策略提示, 实际由 Drivers 层决定")]
    public bool PreferPawnIO { get; set; } = true;

    [ConfigComment("切换 EC 性能模式时同步 CPU 档位并下发功耗/温度墙 (静音→Saving, 均衡→Default, 性能→Performance)")]
    public bool ApplyCpuProfileOnModeChange { get; set; } = true;

    [ConfigComment("上次使用的瞬时场景: QuietDaily / Portable135 / 空。仅用于下次启动提示，不自动下发")]
    public string LastPowerScene { get; set; } = "";

    [ConfigComment("电池阈值工具目录 (含 NativeEnable.cmd / NativeDisable.cmd), 空则尝试常见路径")]
    public string BatteryThresholdToolDir { get; set; } = "";

    [ConfigComment("主窗口记忆宽度 (物理像素), 0 = 未记忆, 默认按 1300×820 物理像素启动, 不随 DPI/缩放放大, 跨屏物理大小不变")]
    public double WindowPixelWidth { get; set; }

    [ConfigComment("主窗口记忆高度 (物理像素), 0 = 未记忆, 默认按 1300×820 物理像素启动, 不随 DPI/缩放放大, 跨屏物理大小不变")]
    public double WindowPixelHeight { get; set; }

    [ConfigComment("旧版主窗口宽度 (DIP), 仅用于老配置一次性迁移, 不再写入")]
    public double WindowWidth { get; set; }

    [ConfigComment("旧版主窗口高度 (DIP), 仅用于老配置一次性迁移, 不再写入")]
    public double WindowHeight { get; set; }
}

public class CpuSection
{
    [ConfigComment("当前选中档位: default / performance / saving / custom")]
    public string CpuProfile { get; set; } = "default";

    [ConfigComment("游戏均衡档 (Default): 蛟龙16Pro 2023 / 7945HX 双烤约 75–80W")]
    public CpuProfileData Default { get; set; } = CpuProfilePresets.Game();

    [ConfigComment("生产力档 (Performance): 高负载编译/渲染, 注意散热与噪音")]
    public CpuProfileData Performance { get; set; } = CpuProfilePresets.Productivity();

    [ConfigComment("办公静音档 (Saving): 轻负载低热")]
    public CpuProfileData Saving { get; set; } = CpuProfilePresets.Office();

    [ConfigComment("自定义档位参数 (种子同游戏均衡, 可任意改)")]
    public CpuProfileData Custom { get; set; } = CpuProfilePresets.Game();

    [YamlIgnore]
    public CpuProfileData Active => CpuProfile switch
    {
        "performance" => Performance,
        "saving" => Saving,
        "custom" => Custom,
        _ => Default
    };
}

/// <summary>
/// 蛟龙 16 Pro 2023 (7945HX) 出厂三档预设。前后端重置逻辑应对齐此处数值。
/// </summary>
public static class CpuProfilePresets
{
    /// <summary>A 办公静音：轻负载、低热。</summary>
    public static CpuProfileData Office() => new()
    {
        CpuLongPower = 45,
        CpuShortPower = 60,
        CpuTempWall = 90,
        CpuMaxFrequency = 4500,
        CpuTurbo = true,
        RecommendedCurveOptimizer = -8,
        SoftSchedule = SoftSchedulePresets.Office()
    };

    /// <summary>B 游戏均衡：同机型双烤甜点区约 75–80W，短时冲刺。</summary>
    public static CpuProfileData Game() => new()
    {
        CpuLongPower = 78,
        CpuShortPower = 108,
        CpuTempWall = 95,
        CpuMaxFrequency = 5150,
        CpuTurbo = true,
        RecommendedCurveOptimizer = -12,
        SoftSchedule = SoftSchedulePresets.Game()
    };

    /// <summary>C 生产力：高负载持续，略抬长期功耗与温度墙。</summary>
    public static CpuProfileData Productivity() => new()
    {
        CpuLongPower = 90,
        CpuShortPower = 115,
        CpuTempWall = 96,
        CpuMaxFrequency = 5250,
        CpuTurbo = true,
        RecommendedCurveOptimizer = -10,
        SoftSchedule = SoftSchedulePresets.Productivity()
    };
}

/// <summary>
/// Windows 软调度（powercfg SUB_PROCESSOR）：EPP / 核心停放下限 / 睿频模式，AC/DC 分设。
/// 不替代 EC 功耗墙；避免桌面式 MIN%=100 硬钉核。
/// </summary>
public static class SoftSchedulePresets
{
    /// <summary>办公：敢停放、偏能效，保响应即可。</summary>
    public static SoftScheduleData Office() => new()
    {
        AcEpp = 55,
        DcEpp = 80,
        AcCpMinCores = 5,
        DcCpMinCores = 0,
        AcBoostMode = 1,
        DcBoostMode = 1
    };

    /// <summary>游戏：插电偏性能但仍允许停放，避免强行摊满 32 线程。</summary>
    public static SoftScheduleData Game() => new()
    {
        AcEpp = 25,
        DcEpp = 50,
        AcCpMinCores = 15,
        DcCpMinCores = 5,
        AcBoostMode = 2,
        DcBoostMode = 1
    };

    /// <summary>安静日常：相对游戏略提高 EPP、略降停放下限。</summary>
    public static SoftScheduleData QuietDaily() => new()
    {
        AcEpp = 35,
        DcEpp = 55,
        AcCpMinCores = 10,
        DcCpMinCores = 5,
        AcBoostMode = 2,
        DcBoostMode = 1
    };

    /// <summary>生产力：少停放、EPP 偏性能，方便真多线程铺开。</summary>
    public static SoftScheduleData Productivity() => new()
    {
        AcEpp = 0,
        DcEpp = 40,
        AcCpMinCores = 70,
        DcCpMinCores = 25,
        AcBoostMode = 2,
        DcBoostMode = 1
    };
}

public class SoftScheduleData
{
    [ConfigComment("插电能效优先策略 EPP (0=性能 … 100=能效)")]
    [ConfigRange(0, 100)]
    public byte AcEpp { get; set; } = 25;

    [ConfigComment("电池能效优先策略 EPP (0=性能 … 100=能效)")]
    [ConfigRange(0, 100)]
    public byte DcEpp { get; set; } = 50;

    [ConfigComment("插电核心停放最小核心百分比 (CPMINCORES)")]
    [ConfigRange(0, 100)]
    public byte AcCpMinCores { get; set; } = 15;

    [ConfigComment("电池核心停放最小核心百分比 (CPMINCORES)")]
    [ConfigRange(0, 100)]
    public byte DcCpMinCores { get; set; } = 5;

    [ConfigComment("插电睿频模式 PERFBOOSTMODE (0关/1开/2激进)")]
    [ConfigRange(0, 2)]
    public byte AcBoostMode { get; set; } = 2;

    [ConfigComment("电池睿频模式 PERFBOOSTMODE (0关/1开/2激进)")]
    [ConfigRange(0, 2)]
    public byte DcBoostMode { get; set; } = 1;
}

public class CpuProfileData
{
    [ConfigComment("CPU长期功率限制 (W)")]
    [ConfigRange(5, 120)]
    public byte CpuLongPower { get; set; } = 78;

    [ConfigComment("CPU短期功率限制 (W)")]
    [ConfigRange(5, 150)]
    public byte CpuShortPower { get; set; } = 108;

    [ConfigComment("CPU温度墙 (℃)")]
    [ConfigRange(60, 105)]
    public byte CpuTempWall { get; set; } = 95;

    [ConfigComment("CPU最大频率 (MHz)")]
    [ConfigRange(2000, 6000)]
    public uint CpuMaxFrequency { get; set; } = 5150;

    [ConfigComment("CPU睿频开关")]
    public bool CpuTurbo { get; set; } = true;

    [ConfigComment("推荐 Curve Optimizer 全核偏移 (负=降压); 应用档位时可选写入 SMU")]
    [ConfigRange(-30, 30)]
    public int RecommendedCurveOptimizer { get; set; } = -12;

    [ConfigComment("软调度: EPP / 停放 / Boost，AC·DC 分设")]
    public SoftScheduleData SoftSchedule { get; set; } = SoftSchedulePresets.Game();
}

public class GpuSection
{
    [ConfigComment("GPU核心频率 (MHz)")]
    public int GpuClock { get; set; }

    [ConfigComment("GPU显存频率 (MHz)")]
    public int MemoryClock { get; set; } = 100;

    [ConfigComment("GPU功率限制 (W)")]
    public int PowerLimit { get; set; } = 140;

    [ConfigComment("GPU核心频率偏移 (MHz, 负值为降压)")]
    public int CoreClockOffsetMhz { get; set; }

    [ConfigComment("GPU显存频率偏移 (MHz)")]
    public int MemoryClockOffsetMhz { get; set; }
}

public class FanSection
{
    [ConfigComment("合并CPU/GPU风扇曲线")]
    public bool FanCurveMerge { get; set; }

    [ConfigComment("进阶 Blding 强制设速用 (RPM); 日常请用各模式转速上限")]
    public int ManualFanSpeed { get; set; } = 1500;

    [ConfigComment("切换性能模式时是否按模式重下发 WMI 转速上限 (0x14/0x15)")]
    public bool ApplyFanOnModeChange { get; set; } = true;

    [ConfigComment("安静模式转速上限 (RPM), 合法 2200–3500")]
    [ConfigRange(2200, 3500)]
    public int QuietModeFanRpm { get; set; } = 2700;

    [ConfigComment("均衡模式转速上限 (RPM), 合法 3500–5000; 安静日常推荐 3500")]
    [ConfigRange(3500, 5000)]
    public int BalanceModeFanRpm { get; set; } = 3500;

    [ConfigComment("性能/狂暴模式转速上限 (RPM), 合法 5000–5800")]
    [ConfigRange(5000, 5800)]
    public int PerformanceModeFanRpm { get; set; } = 5800;

    [ConfigComment("软件曲线升速上限 (RPM/s)，减轻突跳")]
    [ConfigRange(50, 2000)]
    public int RampUpRpmPerSec { get; set; } = 600;

    [ConfigComment("软件曲线降速上限 (RPM/s)，减轻温降后仍狂吹")]
    [ConfigRange(50, 2000)]
    public int RampDownRpmPerSec { get; set; } = 250;

    public List<FanPoint> CpuFanCurve { get; set; } = new()
    {
        new() { temp = 60, speed = 1500 }, new() { temp = 65, speed = 2104 },
        new() { temp = 70, speed = 2778 }, new() { temp = 75, speed = 3158 },
        new() { temp = 80, speed = 3365 }, new() { temp = 86, speed = 3607 },
        new() { temp = 91, speed = 3849 }, new() { temp = 94, speed = 4828 },
        new() { temp = 97, speed = 5415 }, new() { temp = 100, speed = 5800 },
    };

    public List<FanPoint> GpuFanCurve { get; set; } = new()
    {
        new() { temp = 60, speed = 3000 }, new() { temp = 65, speed = 4000 },
        new() { temp = 70, speed = 4800 }, new() { temp = 75, speed = 5000 },
        new() { temp = 80, speed = 5400 }, new() { temp = 87, speed = 5800 },
    };
}

public class FanPoint
{
    [ConfigComment("温度 (℃)")]
    public int temp { get; set; }

    [ConfigComment("转速 (RPM)")]
    public int speed { get; set; }
}

public class SmuSection
{
    [ConfigComment("STAPM限制 (W)")]
    public int StapmLimit { get; set; }

    [ConfigComment("STAPM时间 (s)")]
    public int StapmTime { get; set; }

    [ConfigComment("快速功耗限制 (W)")]
    public int FastLimit { get; set; }

    [ConfigComment("慢速功耗限制 (W)")]
    public int SlowLimit { get; set; }

    [ConfigComment("慢速功耗时间 (s)")]
    public int SlowTime { get; set; }

    [ConfigComment("PPT限制 (RSMU, W)")]
    public int PptLimitRsmu { get; set; }

    [ConfigComment("VRM电流 MP1 (A)")]
    public int VrmCurrentMp1 { get; set; }

    [ConfigComment("VRM电流 RSMU (A)")]
    public int VrmCurrentRsmu { get; set; }

    [ConfigComment("TDC限制 MP1 (A)")]
    public int TdcLimitMp1 { get; set; }

    [ConfigComment("TDC限制 RSMU (A)")]
    public int TdcLimitRsmu { get; set; }

    [ConfigComment("EDC限制 MP1 (A)")]
    public int EdcLimitMp1 { get; set; }

    [ConfigComment("EDC限制 RSMU (A)")]
    public int EdcLimitRsmu { get; set; }

    [ConfigComment("温度限制 MP1 (℃)")]
    public int TempLimitMp1 { get; set; }

    [ConfigComment("温度限制 RSMU (℃)")]
    public int TempLimitRsmu { get; set; }

    [ConfigComment("PBO Scalar")]
    public int PboScalar { get; set; }

    [ConfigComment("超频频率 (MHz)")]
    public int OcClk { get; set; }

    [ConfigComment("超频电压 (mV)")]
    public int OcVolt { get; set; }

    [ConfigComment("Curve Optimizer All (负值为降压); 7945HX 同机型常用起点约 -12")]
    public int CurveOptimizerAll { get; set; } = -12;

    [ConfigComment("Curve Optimizer 分核偏移 (负值为降压, 索引=核心号)")]
    public List<int> CurveOptimizerPerCore { get; set; } = new();
}

public class OsdSection
{
    [ConfigComment("启用 OSD 屏幕显示 (音量/锁定键由键盘事件触发)")]
    public bool Enabled { get; set; } = true;

    [ConfigComment("显示项目: 音量指示")]
    public bool ShowVolume { get; set; } = true;

    [ConfigComment("显示项目: 大写/数字/滚动锁定")]
    public bool ShowLockKeys { get; set; } = true;

    [ConfigComment("显示项目: 键盘背光档位 (软件内调整时触发)")]
    public bool ShowKeyboardBacklight { get; set; } = true;

    [ConfigComment("显示项目: 性能模式切换 (软件内切换时触发)")]
    public bool ShowPerformanceMode { get; set; } = true;

    [ConfigComment("模式切换 OSD 附带 CPU/GPU 温度与风扇转速")]
    public bool ShowPerfTelemetry { get; set; } = true;

    [ConfigComment("显示项目: 功能键(Fn)锁定 (轮询EC检测)")]
    public bool ShowFnLock { get; set; } = true;

    [ConfigComment("显示项目: 触摸板锁定 (轮询EC检测)")]
    public bool ShowTouchpad { get; set; } = true;

    [ConfigComment("显示项目: 媒体播放提示 (系统有程序开始播放音乐或切歌时显示曲名与歌手)")]
    public bool ShowMedia { get; set; } = true;

    [ConfigComment("屏蔽 Windows 音量/亮度/媒体浮层 (接管音量键)。启用 OSD 时另会藏起厂家 BLDFnHotkeyUtility 旧 OSD")]
    public bool SuppressNativeVolumeOsd { get; set; } = false;

    [ConfigComment("EC 状态轮询间隔 (ms, 事件通道不可用时的兜底)")]
    [ConfigRange(500, 5000)]
    public int PollIntervalMs { get; set; } = 1000;

    [ConfigComment("出入场动画时长 (ms, 入场展开与出场收圆各占此时长)")]
    [ConfigRange(200, 3000)]
    public int AnimationMs { get; set; } = 480;

    [ConfigComment("OSD 显示时长 (ms, 完全展开后的驻留时间)")]
    [ConfigRange(500, 5000)]
    public int DurationMs { get; set; } = 2000;

    [ConfigComment("显示位置: TopCenter / BottomCenter / Custom")]
    public string Position { get; set; } = "TopCenter";

    [ConfigComment("自定义位置水平百分比 (胶囊在主屏工作区可移动行程的 0~100%, Position=Custom 时生效)")]
    [ConfigRange(0, 100)]
    public int CustomX { get; set; } = 50;

    [ConfigComment("自定义位置垂直百分比 (胶囊在主屏工作区可移动行程的 0~100%, Position=Custom 时生效)")]
    [ConfigRange(0, 100)]
    public int CustomY { get; set; } = 8;

    [ConfigComment("面板不透明度 (%)")]
    [ConfigRange(30, 100)]
    public int Opacity { get; set; } = 90;

    [ConfigComment("界面缩放 (%)")]
    [ConfigRange(100, 200)]
    public int Scale { get; set; } = 150;
}
