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
            var res = MethodServices.SetValue(MethodName.SystemPerMode, mode);
            if (res)
            {
                OsdController.Instance?.OnPerformanceModeChanged(mode);
                TryApplyModeFan(mode);
            }

            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        /// <summary>
        /// 模式切换时恢复该模式绑定的手动风扇转速（自动风扇运行中则跳过，避免打架）。
        /// </summary>
        private static void TryApplyModeFan(SystemPerMode mode)
        {
            try
            {
                var fanCfg = Bridge.Instance.Config.Fan;
                if (!fanCfg.ApplyFanOnModeChange)
                    return;

                if (Bridge.Instance.AutoFan.IsRunning().Success &&
                    Bridge.Instance.AutoFan.IsRunning().Data is true)
                    return;

                int rpm = mode switch
                {
                    SystemPerMode.QuietMode => fanCfg.QuietModeFanRpm,
                    SystemPerMode.PerformanceMode => fanCfg.PerformanceModeFanRpm,
                    SystemPerMode.BalanceMode => fanCfg.BalanceModeFanRpm,
                    _ => fanCfg.ManualFanSpeed
                };

                // FanController.SetFanSpeed 使用 EC 字节单位 (约 100 RPM/单位)
                byte unit = (byte)Math.Clamp(rpm / 100, 0, 68);
                var set = Bridge.Instance.Fan.SetFanSpeed(unit);
                if (!set.Success)
                    Logger.Warn($"模式 {mode} 风扇恢复失败: {set.Message}");
                else
                    Logger.Info($"模式 {mode} 已恢复风扇约 {rpm} RPM");
            }
            catch (Exception ex)
            {
                Logger.Warn($"模式风扇绑定异常: {ex.Message}");
            }
        }
    }
}