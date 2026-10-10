using System.Runtime.InteropServices;
using System.Text.Json;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;

namespace JiaoLongControl.Server.Core.Controllers;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class ConfigController
{
    public CommandResult GetConfig()
    {
        try
        {
            return new CommandResult(true, "成功", Bridge.Instance.Config);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, ex.Message);
        }
    }

    public CommandResult SetConfig(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<JiaoLongConfig>(configJson);
            if (config == null)
                return new CommandResult(false, "配置数据无效");
            Bridge.Instance.ApplyConfig(config);
            return new CommandResult(true, "保存成功");
        }
        catch (Exception ex)
        {
            return new CommandResult(false, ex.Message);
        }
    }

    /// <summary>从第三方控制中心 AppSettings.json 导入风扇/功耗/温度墙。</summary>
    public CommandResult ImportLegacyAppSettings(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppSettingsImporter.ImportFromDefaultLegacyPath();
        return AppSettingsImporter.ImportFromFile(path);
    }

    /// <summary>解析电池阈值外挂工具目录。</summary>
    public CommandResult ResolveBatteryThresholdToolDir()
    {
        return BatteryThresholdLauncher.ResolveToolDir(Bridge.Instance.Config.App.BatteryThresholdToolDir);
    }

    /// <summary>
    /// 外挂启动充电阈值脚本（需管理员；不内嵌 WinRing0）。
    /// enable=true 时 low/high 为百分比；allowInitialDischarge 对应原生工具参数。
    /// </summary>
    public CommandResult LaunchBatteryThreshold(bool enable, int low, int high, bool allowInitialDischarge)
    {
        return BatteryThresholdLauncher.Launch(enable, low, high, allowInitialDischarge);
    }
}
