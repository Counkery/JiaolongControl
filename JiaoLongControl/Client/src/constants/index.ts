/** 业务常量: 硬件限值与轮询间隔 (与 Server 端 C#/Python 约定保持一致) */

import type { CpuProfileDataType } from '@/types/config'

/** 风扇手动转速区间 (RPM) */
export const FAN_MAX_RPM = 5800
export const FAN_MIN_RPM = 1500

/** 遥测轮询间隔 (ms) */
export const POLL_INTERVAL_SYSTEM_INFO = 5000
export const POLL_INTERVAL_SMU = 3000
export const POLL_INTERVAL_FAN_SPEED = 2000

/**
 * 各档位出厂默认参数 — 与 Server CpuProfilePresets (7945HX / 蛟龙16Pro 2023) 对齐:
 * Saving=办公静音, Default=游戏均衡, Performance=生产力, Custom 种子=游戏均衡
 */
const SOFT_OFFICE = {
  AcEpp: 55,
  DcEpp: 80,
  AcCpMinCores: 5,
  DcCpMinCores: 0,
  AcBoostMode: 1,
  DcBoostMode: 1,
}

const SOFT_GAME = {
  AcEpp: 25,
  DcEpp: 50,
  AcCpMinCores: 15,
  DcCpMinCores: 5,
  AcBoostMode: 2,
  DcBoostMode: 1,
}

const SOFT_PRODUCTIVITY = {
  AcEpp: 0,
  DcEpp: 40,
  AcCpMinCores: 70,
  DcCpMinCores: 25,
  AcBoostMode: 2,
  DcBoostMode: 1,
}

export const CPU_PROFILE_DEFAULTS: Record<
  'Default' | 'Performance' | 'Saving' | 'Custom',
  CpuProfileDataType
> = {
  Saving: {
    CpuLongPower: 45,
    CpuShortPower: 60,
    CpuTempWall: 90,
    CpuMaxFrequency: 4750,
    CpuTurbo: true,
    RecommendedCurveOptimizer: -8,
    SoftSchedule: { ...SOFT_OFFICE },
  },
  Default: {
    CpuLongPower: 78,
    CpuShortPower: 108,
    CpuTempWall: 95,
    CpuMaxFrequency: 5150,
    CpuTurbo: true,
    RecommendedCurveOptimizer: -12,
    SoftSchedule: { ...SOFT_GAME },
  },
  Performance: {
    CpuLongPower: 90,
    CpuShortPower: 115,
    CpuTempWall: 96,
    CpuMaxFrequency: 5250,
    CpuTurbo: true,
    RecommendedCurveOptimizer: -10,
    SoftSchedule: { ...SOFT_PRODUCTIVITY },
  },
  Custom: {
    CpuLongPower: 78,
    CpuShortPower: 108,
    CpuTempWall: 95,
    CpuMaxFrequency: 5150,
    CpuTurbo: true,
    RecommendedCurveOptimizer: -12,
    SoftSchedule: { ...SOFT_GAME },
  },
}
