using JiaoLongControl.Server.Core.Controllers;
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

    /// <summary>瞬时软调度（与 QuietDaily / Portable135 一并生效；退出瞬时会话后清空）。</summary>
    private SoftScheduleData? _transientSoft;

    /// <summary>瞬时场景名：QuietDaily / Portable135；无瞬时时为 null。</summary>
    private string? _transientScene;

    public bool IsRunning => _running;

    public bool HasTransientOverride
    {
        get { lock (_lock) return _transientOverride; }
    }

    /// <summary>当前瞬时场景；无覆盖时 null。</summary>
    public string? TransientScene
    {
        get { lock (_lock) return _transientOverride ? _transientScene : null; }
    }

    /// <summary>
    /// 更新期望功耗目标（来自当前 CPU 档位 / UI 应用）。
    /// 默认清除瞬时覆盖，避免 QuietDaily 保活打回旧墙。
    /// </summary>
    /// <returns>被清除的瞬时场景名（若有）。</returns>
    public string? UpdateTarget(byte longPower, byte shortPower, byte tempWall, bool clearTransient = true)
    {
        lock (_lock)
        {
            _desiredLong = longPower;
            _desiredShort = shortPower;
            _desiredTempWall = tempWall;
            _targetEpoch++;
            string? cleared = null;
            if (clearTransient && _transientOverride)
            {
                cleared = _transientScene;
                _transientOverride = false;
                _transientSoft = null;
                _transientScene = null;
            }
            return cleared;
        }
    }

    /// <summary>设置瞬时功耗/软调度目标（不写配置）；softSchedule 必填，避免 Active 与 Soft 分裂。</summary>
    public void SetTransientOverride(
        byte longPower, byte shortPower, byte tempWall, SoftScheduleData softSchedule,
        string scene = "QuietDaily")
    {
        ArgumentNullException.ThrowIfNull(softSchedule);
        lock (_lock)
        {
            _desiredLong = longPower;
            _desiredShort = shortPower;
            _desiredTempWall = tempWall;
            _transientSoft = softSchedule;
            _transientScene = string.IsNullOrWhiteSpace(scene) ? "QuietDaily" : scene;
            _transientOverride = true;
            _targetEpoch++;
        }
    }

    /// <returns>清除前的场景名（若有）。</returns>
    public string? ClearTransientOverride()
    {
        lock (_lock)
        {
            string? cleared = _transientOverride ? _transientScene : null;
            _transientOverride = false;
            _transientSoft = null;
            _transientScene = null;
            return cleared;
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

    /// <returns>force 清除时的原场景名。</returns>
    public string? SyncFromConfig(bool force = false)
    {
        string? cleared = null;
        lock (_lock)
        {
            if (_transientOverride && !force)
                return null;
            if (force && _transientOverride)
            {
                cleared = _transientScene;
                _transientOverride = false;
                _transientSoft = null;
                _transientScene = null;
            }
        }

        var cpu = Bridge.Instance.Config.Cpu.Active;
        UpdateTarget(cpu.CpuLongPower, cpu.CpuShortPower, cpu.CpuTempWall, clearTransient: true);
        return cleared;
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

        var cts = _cts;
        var task = _task;

        cts?.Cancel();

        bool stopped;
        try
        {
            stopped = task?.Wait(3000) ?? true;
        }
        catch (AggregateException)
        {
            stopped = true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"停止保活异常: {ex.Message}");
            stopped = true;
        }

        _running = false;
        _cts = null;
        _task = null;

        // 线程没退出时不要 Dispose CTS: 循环里的 WaitHandle 还在用它, 释放会抛 ObjectDisposedException
        if (stopped)
        {
            try { cts?.Dispose(); } catch { /* ignore */ }
        }
        else
        {
            Logger.Warn("CPU 功耗保活线程未在 3 秒内退出，跳过 CancellationTokenSource 释放");
        }

        Logger.Info("CPU 功耗保活已停止");
        return new CommandResult(true, "CPU 功耗保活已停止");
    }

    public void Dispose() => Stop();

    private void Loop(CancellationToken token)
    {
        // 启动后稍等，避免与 SelfStart 首次下发撞车
        token.WaitHandle.WaitOne(5000);

        int portableTicks = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!Bridge.Instance.Config.App.CpuPowerKeepAliveEnabled)
                {
                    token.WaitHandle.WaitOne(Math.Clamp(IntervalMs, 5_000, 120_000));
                    continue;
                }

                bool portable = string.Equals(TransientScene, "Portable135", StringComparison.Ordinal);
                if (portable)
                {
                    // 热键把 EC 模式拨走之后，事件若迟到，最多约 2 秒拉回办公并压住 GPU/频率。
                    OsdController.Instance?.ReassertPortableMode();
                    if (Bridge.Instance.AutoFan.StopLoop())
                    {
                        Logger.Info("便携 135W 期间软件曲线被重新打开，已停止并恢复转速上限");
                        Bridge.Instance.Fan.SetFanCeilingRpm(2700);
                    }
                    portableTicks++;
                    if (portableTicks == 1 || portableTicks % 15 == 0)
                    {
                        SyncFromConfig();
                        ReapplyLatest();
                        PerformanceModeController.HoldPortable135(Bridge.Instance);
                    }

                    token.WaitHandle.WaitOne(2000);
                    continue;
                }

                portableTicks = 0;
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

    /// <summary>电源策略重写的节流状态（见 ReapplyProcessorPolicy）。</summary>
    private static readonly object _policyLock = new();
    private static string? _lastPolicySignature;
    private static long _lastPolicyAppliedTick;

    /// <summary>即使目标没变，也每 5 分钟兜底重写一次（Windows 切换电源方案会清掉）。</summary>
    private const int PolicyRebaseMs = 5 * 60 * 1000;

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
        // 频率上限和软调度写在当前电源方案里，Windows 一切换电源模式就会丢。
        // 与功耗墙一起周期写回。便携场景的 GPU 60W 由 HoldPortable135 负责，这里不拉回高档。
        ReapplyProcessorPolicy();

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

    /// <summary>把「会影响电源策略/GPU/CO 下发结果」的配置值压成签名，用于判断是否值得重写。</summary>
    private static string BuildPolicySignature(
        Bridge bridge, CpuProfileData profile, SoftScheduleData soft, string? scene)
    {
        var app = bridge.Config.App;
        var gpu = bridge.Config.Gpu;
        var smu = bridge.Config.Smu;
        var fan = bridge.Config.Fan;

        return string.Join('|', new object?[]
        {
            scene ?? "-",
            profile.CpuMaxFrequency, profile.CpuTurbo,
            soft.AcEpp, soft.DcEpp, soft.AcCpMinCores, soft.DcCpMinCores, soft.AcBoostMode, soft.DcBoostMode,
            gpu.PowerLimit, gpu.GpuClock, gpu.MemoryClock,
            gpu.CoreClockOffsetMhz, gpu.MemoryClockOffsetMhz,
            app.BootAdvancedGPUSystem, app.BootGpuUseAdvancedOffsets,
            app.BootSetRyzenSumCurveOptimizerAll, app.BootSetRyzenSmuCurveOptimizerPerCore,
            smu.CurveOptimizerAll, string.Join(",", smu.CurveOptimizerPerCore),
            fan.ApplyFanOnModeChange, fan.QuietModeFanRpm,
            fan.PerformanceModeFanRpm, fan.BalanceModeFanRpm,
        });
    }

    /// <summary>
    /// 电源方案被 Windows 改掉后，把频率上限、软调度、GPU 功耗墙写回去。
    /// 每次下发要起约 11 个 powercfg 子进程，所以按「目标变化 / 5 分钟兜底 / force(睡眠唤醒)」节流，
    /// 不再每个保活周期无条件重写。
    /// </summary>
    internal static void ReapplyProcessorPolicy(bool force = false)
    {
        try
        {
            var bridge = Bridge.Instance;
            var keep = bridge.CpuPowerKeepAlive;
            var profile = bridge.Config.Cpu.Active;
            var soft = keep.GetEffectiveSoftSchedule(profile.SoftSchedule);
            string? scene = keep.TransientScene;

            string signature = BuildPolicySignature(bridge, profile, soft, scene);
            long now = Environment.TickCount64;
            lock (_policyLock)
            {
                bool due = force
                           || _lastPolicySignature != signature
                           || now - _lastPolicyAppliedTick >= PolicyRebaseMs;
                if (!due)
                    return;

                _lastPolicySignature = signature;
                _lastPolicyAppliedTick = now;
            }

            bool turbo = keep.HasTransientOverride || profile.CpuTurbo;
            bridge.Power.ApplySoftSchedule(soft, turbo);

            uint mhz = scene switch
            {
                "Portable135" => 4500,
                "QuietDaily" => 4750,
                _ => profile.CpuMaxFrequency
            };
            if (mhz == 0)
                bridge.Power.ResetCPUMaxFrequency();
            else
                bridge.Power.SetCPUMaxFrequency(mhz);

            if (!string.Equals(scene, "Portable135", StringComparison.Ordinal))
            {
                int gpuWatts = bridge.Config.Gpu.PowerLimit;
                if (gpuWatts > 0)
                    bridge.NvidiaGpu.SetPowerLimit(gpuWatts);
            }

            // 风扇上限：EC 睡眠后会忘掉。软件曲线运行时不要写，避免拆掉强制转速。
            var fanRunning = bridge.AutoFan.IsRunning();
            if (!(fanRunning.Success && fanRunning.Data is true))
                bridge.Fan.ReapplySavedCeiling();

            var app = bridge.Config.App;
            var gpu = bridge.Config.Gpu;
            if (app.BootAdvancedGPUSystem)
            {
                if (app.BootGpuUseAdvancedOffsets)
                {
                    if (gpu.CoreClockOffsetMhz != 0 || gpu.MemoryClockOffsetMhz != 0)
                        bridge.NvidiaGpu.SetGpuOffsets(gpu.CoreClockOffsetMhz, gpu.MemoryClockOffsetMhz);
                }
                else
                {
                    if (gpu.GpuClock > 0)
                        bridge.NvidiaGpu.LockGpuClock(gpu.GpuClock);
                    if (gpu.MemoryClock > 0)
                        bridge.NvidiaGpu.LockMemoryClock(gpu.MemoryClock);
                }
            }

            if (app.BootSetRyzenSumCurveOptimizerAll)
            {
                if (app.BootSetRyzenSmuCurveOptimizerPerCore)
                {
                    var perCore = bridge.Config.Smu.CurveOptimizerPerCore;
                    for (int i = 0; i < perCore.Count; i++)
                        bridge.RyzenSmu.SetCurveOptimizerPerCore((uint)i, perCore[i]);
                }
                else
                {
                    bridge.RyzenSmu.SetCurveOptimizerAll(bridge.Config.Smu.CurveOptimizerAll);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"电源策略重应用失败: {ex.Message}");
        }
    }
}
