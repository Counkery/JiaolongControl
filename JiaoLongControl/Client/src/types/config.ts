// 与 C# JiaoLongConfig.cs 保持同步
// 新增字段：在 C# 类中加属性，在此 interface 中加对应字段

export interface FanPoint {
  temp: number
  speed: number
}

export type ThemeMode = 'light' | 'dark' | 'system'

export interface AppSectionType {
  BootMinimized: boolean
  BootAdvancedFanControlSystem: boolean
  BootAdvancedCPUSystem: boolean
  BootAdvancedGPUSystem: boolean
  BootSetRyzenSumCurveOptimizerAll: boolean
  BootSetRyzenSmuCurveOptimizerPerCore: boolean
  BootKeyboardGradient: boolean
  CpuCurveOptimizerPerCore: boolean
  BootGpuUseAdvancedOffsets: boolean
  Theme: ThemeMode
  /** CPU 功耗/温度墙周期保活（与监控采集分离） */
  CpuPowerKeepAliveEnabled: boolean
  /** 优先 PawnIO，避免 WinRing0 */
  PreferPawnIO: boolean
  /** 切换 EC 性能模式时同步 CPU 档位并下发功耗 */
  ApplyCpuProfileOnModeChange: boolean
  /** 上次瞬时场景，启动时只提示、不自动套用 */
  LastPowerScene?: string
  /** 电池阈值外挂工具目录 */
  BatteryThresholdToolDir: string
  /** 主窗口记忆宽度 (物理像素), 0 = 未记忆, 与显示器 DPI/缩放无关 */
  WindowPixelWidth: number
  /** 主窗口记忆高度 (物理像素), 0 = 未记忆, 与显示器 DPI/缩放无关 */
  WindowPixelHeight: number
  /** 旧版主窗口宽度 (DIP), 仅用于老配置迁移, 不再写入 */
  WindowWidth: number
  /** 旧版主窗口高度 (DIP), 仅用于老配置迁移, 不再写入 */
  WindowHeight: number
}

/** Windows 软调度（powercfg）：EPP / 停放 / Boost，AC·DC 分设 */
export interface SoftScheduleDataType {
  AcEpp: number
  DcEpp: number
  AcCpMinCores: number
  DcCpMinCores: number
  /** 0 关 / 1 开 / 2 激进 */
  AcBoostMode: number
  DcBoostMode: number
}

export interface CpuProfileDataType {
  CpuLongPower: number
  CpuShortPower: number
  CpuTempWall: number
  CpuMaxFrequency: number
  CpuTurbo: boolean
  /** 推荐 Curve Optimizer 全核偏移（负=降压） */
  RecommendedCurveOptimizer?: number
  SoftSchedule?: SoftScheduleDataType
}

export interface CpuSectionType {
  CpuProfile: string
  Default: CpuProfileDataType
  Performance: CpuProfileDataType
  Saving: CpuProfileDataType
  Custom: CpuProfileDataType
}

export interface GpuSectionType {
  GpuClock: number
  MemoryClock: number
  PowerLimit: number
  CoreClockOffsetMhz: number
  MemoryClockOffsetMhz: number
}

export interface FanSectionType {
  FanCurveMerge: boolean
  ManualFanSpeed: number
  /** 切换性能模式时恢复绑定风扇 */
  ApplyFanOnModeChange: boolean
  QuietModeFanRpm: number
  BalanceModeFanRpm: number
  PerformanceModeFanRpm: number
  /** 软件曲线升速上限 RPM/s */
  RampUpRpmPerSec?: number
  /** 软件曲线降速上限 RPM/s */
  RampDownRpmPerSec?: number
  CpuFanCurve: FanPoint[]
  GpuFanCurve: FanPoint[]
}

export interface SmuSectionType {
  StapmLimit: number
  StapmTime: number
  FastLimit: number
  SlowLimit: number
  SlowTime: number
  PptLimitRsmu: number
  VrmCurrentMp1: number
  VrmCurrentRsmu: number
  TdcLimitMp1: number
  TdcLimitRsmu: number
  EdcLimitMp1: number
  EdcLimitRsmu: number
  TempLimitMp1: number
  TempLimitRsmu: number
  PboScalar: number
  OcClk: number
  OcVolt: number
  CurveOptimizerAll: number
  CurveOptimizerPerCore: number[]
}

export type OsdPosition = 'TopCenter' | 'BottomCenter' | 'Custom'

export interface OsdSectionType {
  Enabled: boolean
  ShowVolume: boolean
  ShowScreenBrightness: boolean
  ShowLockKeys: boolean
  ShowKeyboardBacklight: boolean
  ShowPerformanceMode: boolean
  ShowPerfTelemetry: boolean
  ShowFnLock: boolean
  ShowTouchpad: boolean
  ShowMedia: boolean
  SuppressNativeVolumeOsd: boolean
  AnimationMs: number
  DurationMs: number
  PollIntervalMs: number
  Position: OsdPosition
  CustomX: number
  CustomY: number
  Opacity: number
  Scale: number
}

export interface JiaoLongConfigType {
  Version: string
  App: AppSectionType
  Cpu: CpuSectionType
  Gpu: GpuSectionType
  Fan: FanSectionType
  Smu: SmuSectionType
  Osd: OsdSectionType
}
