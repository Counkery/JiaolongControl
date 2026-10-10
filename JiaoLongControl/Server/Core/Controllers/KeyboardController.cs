using System.Runtime.InteropServices;
using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Services;
using JiaoLongControl.Server.Core.Utils;

namespace JiaoLongControl.Server.Core.Controllers
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class KeyboardController
    {
        public CommandResult GetColor()
        {
            Tuple<int, int, int> tuple = MethodServices.GetValue<Tuple<int, int, int>>(MethodName.RGBKeyboardColor);
            var colorInfo = new ColorInfo
            {
                red = tuple.Item1,
                green = tuple.Item2,
                blue = tuple.Item3
            };
           return new CommandResult(true,"获取成功",colorInfo);
        }

        public CommandResult SetColor(byte r, byte g, byte b)
        {
            var res =  MethodServices.SetValue(MethodName.RGBKeyboardColor, new byte[3] { r, g, b });
            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        public CommandResult GetMode()
        {
            return new CommandResult(true, "获取成功", MethodServices.GetValue<RGBKeyboardMode>(MethodName.RGBKeyboardMode));
        }

        public CommandResult SetMode(RGBKeyboardMode mode)
        {
            // 关灯前先打断渐变循环，且不恢复旧色，保证 Mode_Off 立即生效
            if (mode == RGBKeyboardMode.Mode_Off)
            {
                try
                {
                    Interop.Bridge.Instance.KeyboardGradient.StopImmediate();
                }
                catch
                {
                    // 渐变未初始化时忽略
                }
            }

            var res = MethodServices.SetValue(MethodName.RGBKeyboardMode, mode);
            if (res && mode == RGBKeyboardMode.Mode_Off)
            {
                // 再写一次亮度最低档，兼容部分 EC 仅改 Mode 仍残灯的情况
                MethodServices.SetValue(MethodName.RGBKeyboardBrightness, (byte)RGBKeyboardBrightnessLevel.Level_0);
            }

            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }

        public CommandResult GetLightBrightness()
        {
            return new CommandResult(true, "获取成功", MethodServices.GetValue<RGBKeyboardBrightnessLevel>(MethodName.RGBKeyboardBrightness));
        }

        public CommandResult SetLightBrightness(RGBKeyboardBrightnessLevel br)
        {
            var res =  MethodServices.SetValue(MethodName.RGBKeyboardBrightness, (byte)br);
            if (res) OsdController.Instance?.OnKeyboardBrightnessChanged((byte)br);
            return new CommandResult(res, res ? "设置成功" : "设置失败");
        }
    }
}