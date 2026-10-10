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

    /// <summary>目标世代：SetTransient/UpdateTarget 递增，避免 Loop 拷贝后被旧值 Reapply 踩踏。</summary>
    private int _targetEpoch;

    /// <summary>安静日常等瞬时目标：为 true 时 Loop 不再 SyncFromConfig 覆盖。</summary>
    private bool _transientOverride;

    /// <summary>瞬时软调度（与 QuietDaily 一并生效；退出瞬时会话后清空）。</summary>
    private SoftScheduleData? _transientSoft;

    public bool IsRunning => _running;

    public bool HasTransientOverride
    {
        get { lock (_lock) return _transientOverride; }
    }

    /// <summary>
    /// 更新期望功耗目标（来自当前 CPU 档位 / UI 应用）。
    /// 默认清除瞬时覆盖，避免 QuietDaily 保活打回旧墙。
    /// </summary>
    public void UpdateTarget(byte longPower, byte shortPower, byte tempWall, bool clearTransient = true)
    {
        lock (_lock)
        {
            _desiredLong = longPower;
            _desiredShort = shortPower;
            _desiredTempWall = tempWall;
            _targetEpoch++;
            if (clearTransient)
            {
                _transientOverride = false;
                _transientSoft = null;
            }
        }
    }

    /// <summary>设置瞬时功耗/软调度目标（不写配置）；softSchedule 必填，避免 Active 与 Soft 分裂。</summary>
    public void SetTransientOverride(
        byte longPower, byte shortPower, byte tempWall, SoftScheduleData softSchedule)
    {
        ArgumentNullException.ThrowIfNull(softSchedule);
        lock (_lock)
        {
            _desiredLong = longPower;
            _desiredShort = shortPower;
            _desiredTempWall = tempWall;
            _transientSoft = softSchedule;
            _transientOverride = true;
            _targetEpoch++;
        }
    }

    public void ClearTransientOverride()
    {
        lock (_lock)
        {
            _transientOverride = false;
            _transientSoft = null;
        }
    }

    /// <summary>QuietDaily 期间返回瞬时软调度；否则 null（调用方用档位 SoftSchedule）。</summary>
    public SoftScheduleData? GetTransientSoftSchedule()
    {
        lock (_lock)
        {
            return _transientOverride ? _transientSoft : null;
        }
    }

    public SoftScheduleData GetEffectiveSoftSchedule(SoftScheduleData? fallback)
    {
        lock (_lock)
        {
            if (_transientOverride && _transientSoft != null)
                return _transientSoft;
        }
        return fallback ?? SoftSchedulePresets.Game();
    }

    public void SyncFromConfig(bool force = false)
    {
        lock (_lock)
        {
            if (_transientOverride && !force)
                return;
            if (force)
            {
                _transientOverride = false;
                _transientSoft = null;
            }
        }

        var cpu = Bridge.Instance.Config.Cpu.Active;
        UpdateTarget(cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall, clearTransient: true);
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

                SyncFromConfig();
                ReapplyLatest();
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
    private static long _lastFailWarnTick;

    /// <summary>
    /// 读取当前目标并下发；若下发过程中世代变化（如 QuietDaily 刚 SetTransient），立刻用新目标再写一次。
    /// </summary>
    private void ReapplyLatest()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte longP, shortP, wall;
            int epoch;
            lock (_lock)
            {
                longP = _desiredLong;
                shortP = _desiredShort;
                wall = _desiredTempWall;
                epoch = _targetEpoch;
            }

            Reapply(longP, shortP, wall);

            lock (_lock)
            {
                if (_targetEpoch == epoch)
                    return;
            }
        }
    }

    private static void Reapply(byte longP, byte shortP, byte wall)
    {
        var cpu = Bridge.Instance.CPU;
        if (!cpu.IsAcReadyForCustomMode())
        {
            Logger.Debug("非圆口 AC，跳过 CPU 功耗保活重应用");
            return;
        }

        var r1 = cpu.SetCpuLongPower(longP);
        var r2 = cpu.SetCpuShortPower(shortP);
        var r3 = cpu.SetCPUTempWall(wall);

        if (!r1.Success || !r2.Success || !r3.Success)
        {
            // 最多约 2 分钟一条 Warn，避免 CustomMode 打不开时刷屏
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastFailWarnTick) > 120_000)
            {
                Interlocked.Exchange(ref _lastFailWarnTick, now);
                Logger.Warn(
                    $"CPU 功耗重应用部分失败: long={r1.Success}, short={r2.Success}, wall={r3.Success} " +
                    $"(target L={longP} S={shortP} T={wall})");
            }
        }
        else
        {
            Logger.Debug($"CPU 功耗重应用成功: L={longP} S={shortP} T={wall}");
        }
    }
}
