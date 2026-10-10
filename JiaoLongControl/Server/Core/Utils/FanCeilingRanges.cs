using JiaoLongControl.Server.Core.Models;

namespace JiaoLongControl.Server.Core.Utils;

/// <summary>
/// MRID6 / 官方控制台各模式下 WMI MaxFanSpeed（0x15）允许的转速上限范围（RPM）。
/// 单位写入 EC 时为 RPM/100。超出范围会被固件静默忽略。
/// </summary>
public static class FanCeilingRanges
{
    public static (int MinRpm, int MaxRpm) ForMode(SystemPerMode mode) => mode switch
    {
        SystemPerMode.QuietMode => (2200, 3500),
        SystemPerMode.BalanceMode => (3500, 5000),
        SystemPerMode.PerformanceMode => (5000, 5800),
        _ => (2200, 5800)
    };

    public static int ClampRpm(SystemPerMode mode, int rpm)
    {
        var (min, max) = ForMode(mode);
        return Math.Clamp(rpm, min, max);
    }

    public static byte ToUnit(int rpm) => (byte)Math.Clamp(rpm / 100, 0, 68);
}
