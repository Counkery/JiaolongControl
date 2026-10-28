using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Utils;

/// <summary>
/// 外挂调用 Zcodeoooo/JiaoLong16Pro-Battery-threshold 的 NativeEnable/Disable（不内嵌 WinRing0）。
/// </summary>
public static class BatteryThresholdLauncher
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(BatteryThresholdLauncher));

    public static CommandResult ResolveToolDir(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured.Trim());

        // 只搜索通用位置, 不写死任何开发机路径
        candidates.AddRange(new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "JiaoLong16Pro-Battery-threshold", "Windows"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "BatteryThreshold"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "JiaoLong16Pro-Battery-threshold", "Windows"),
        });

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "NativeEnable.cmd")) ||
                    File.Exists(Path.Combine(full, "NativeDisable.cmd")))
                    return new CommandResult(true, "已找到工具目录", full);
            }
            catch { /* ignore */ }
        }

        return new CommandResult(false,
            "未找到电池阈值工具。请克隆 Zcodeoooo/JiaoLong16Pro-Battery-threshold，并在设置中填写 Windows 目录路径。");
    }

    public static CommandResult Launch(bool enable, int low = 60, int high = 80, bool allowInitialDischarge = false)
    {
        try
        {
            var dirRes = ResolveToolDir(Bridge.Instance.Config.App.BatteryThresholdToolDir);
            if (!dirRes.Success || dirRes.Data is not string dir)
                return dirRes;

            string script = enable ? "NativeEnable.cmd" : "NativeDisable.cmd";
            string path = Path.Combine(dir, script);
            if (!File.Exists(path))
                return new CommandResult(false, $"缺少脚本: {path}");

            var args = enable
                ? $"--low {Math.Clamp(low, 5, 95)} --high {Math.Clamp(high, 10, 100)}" +
                  (allowInitialDischarge ? " --allow-initial-discharge" : "")
                : "";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{path}\" {args}\"",
                WorkingDirectory = dir,
                UseShellExecute = true,
                Verb = "runas", // 请求管理员
            };

            Process.Start(psi);
            Logger.Info($"已启动电池阈值脚本: {script} {args}");
            return new CommandResult(true,
                enable
                    ? $"已请求管理员运行 NativeEnable（{low}%–{high}%）。请在弹出窗口完成操作。"
                    : "已请求管理员运行 NativeDisable，恢复默认充电策略。",
                new { Dir = dir, Script = script, Low = low, High = high });
        }
        catch (Exception ex)
        {
            Logger.Error("启动电池阈值工具失败", ex);
            return new CommandResult(false, ex.Message);
        }
    }
}
