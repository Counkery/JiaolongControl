using JiaoLongControl.Server.Core.Models;
using JiaoLongControl.Server.Core.Utils;
using JiaoLongControl.Server.Interop;
using log4net;

namespace JiaoLongControl.Server.Core.Services;

/// <summary>
/// CPU 功耗/温度墙保活：与监控采集循环分离。
/// 仅在启用时按较长周期核对配置目标并条件重应用，避免把 Apply 塞进 UI/OSD 轮询。
/// </summary>
public sealed class CpuPowerKeepAliveService : IDisposable
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(CpuPowerKeepAliveService));

    /// <summary>默认 30s 核对一次（对照第三方“功耗一段时间失效”问题）。</summary>
    public static int IntervalMs { get; set; } = 30_000;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private volatile bool _running;

    private byte _desiredLong;
    private byte _desiredShort;
    private byte _desiredTempWall;
    private bool _hasTarget;

    public bool IsRunning => _running;

    /// <summary>更新期望功耗目标（来自当前 CPU 档位）。</summary>
    public void UpdateTarget(byte longPower, byte shortPower, byte tempWall)
    {
        lock (_lock)
        {
            _desiredLong = longPower;
            _desiredShort = shortPower;
            _desiredTempWall = tempWall;
            _hasTarget = true;
        }
    }

    public void SyncFromConfig()
    {
        var cpu = Bridge.Instance.Config.Cpu.Active;
        UpdateTarget(cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall);
    }

    public CommandResult Start()
    {
        if (_running)
            return new CommandResult(true, "CPU 功耗保活已在运行");

        SyncFromConfig();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Factory.StartNew(
            () => Loop(token),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        _running = true;
        Logger.Info($"CPU 功耗保活已启动, interval={IntervalMs}ms");
        return new CommandResult(true, "CPU 功耗保活已启动");
    }

    public CommandResult Stop()
    {
        if (!_running)
            return new CommandResult(true, "CPU 功耗保活未运行");

        _cts?.Cancel();
        try { _task?.Wait(3000); }
        catch (AggregateException) { }
        catch (Exception ex) { Logger.Warn($"停止保活异常: {ex.Message}"); }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            _task = null;
        }

        Logger.Info("CPU 功耗保活已停止");
        return new CommandResult(true, "CPU 功耗保活已停止");
    }

    public void Dispose() => Stop();

    private void Loop(CancellationToken token)
    {
        // 启动后稍等，避免与 SelfStart 首次下发撞车
        token.WaitHandle.WaitOne(5000);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!Bridge.Instance.Config.App.CpuPowerKeepAliveEnabled)
                {
                    token.WaitHandle.WaitOne(Math.Clamp(IntervalMs, 5_000, 120_000));
                    continue;
                }

                byte longP, shortP, wall;
                lock (_lock)
                {
                    if (!_hasTarget)
                        SyncFromConfig();
                    longP = _desiredLong;
                    shortP = _desiredShort;
                    wall = _desiredTempWall;
                }

                Reapply(longP, shortP, wall);
                token.WaitHandle.WaitOne(Math.Clamp(IntervalMs, 5_000, 120_000));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"CPU 功耗保活循环异常: {ex.Message}");
                token.WaitHandle.WaitOne(5000);
            }
        }
    }

    /// <summary>
    /// EC 侧多数机型无法可靠读回当前 SPL/SPPT，故采用“条件重应用”：
    /// 每次周期重新写入期望值。写入失败记 Warn，成功仅 Debug，避免刷屏。
    /// </summary>
    private static void Reapply(byte longP, byte shortP, byte wall)
    {
        var cpu = Bridge.Instance.CPU;
        var r1 = cpu.SetCpuLongPower(longP);
        var r2 = cpu.SetCpuShortPower(shortP);
        var r3 = cpu.SetCPUTempWall(wall);

        if (!r1.Success || !r2.Success || !r3.Success)
        {
            Logger.Warn(
                $"CPU 功耗重应用部分失败: long={r1.Success}, short={r2.Success}, wall={r3.Success} " +
                $"(target L={longP} S={shortP} T={wall})");
        }
        else
        {
            Logger.Debug($"CPU 功耗重应用成功: L={longP} S={shortP} T={wall}");
        }
    }
}
