using System.Management;

namespace JiaoLongControl.Server.Core.Native;

/// <summary>
/// 通过 WMI <c>WmiMonitorBrightness</c> / <c>WmiMonitorBrightnessMethods</c> 读写内置屏亮度 (0~100)。
/// 供 OSD 在屏蔽系统亮度浮层后显示自绘胶囊；外接屏或桌面无此接口时返回 null。
/// </summary>
internal static class DisplayBrightness
{
    /// <summary>读取当前亮度百分比；不可用时返回 null。</summary>
    public static int? TryGetPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var v = Convert.ToInt32(obj["CurrentBrightness"]);
                    return Math.Clamp(v, 0, 100);
                }
            }
        }
        catch
        {
            // 无 WMI 亮度接口 (外接屏/桌面)
        }

        return null;
    }

    /// <summary>设置亮度百分比；成功返回 true。</summary>
    public static bool TrySetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    // Timeout 秒数, Brightness 0~100
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)percent });
                    return true;
                }
            }
        }
        catch
        {
            // 忽略写入失败
        }

        return false;
    }
}
