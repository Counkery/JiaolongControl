import { defineStore } from 'pinia'
import { CPU, Fan, NvidiaGpu, type FanSpeedInfo, type GpuStats } from '@/utils/bridge'
import { POLL_INTERVAL_SYSTEM_INFO } from '@/constants'

// 轮询重入保护与静态 GPU 信息去重（放模块级，避免污染 state 类型）
let fetching = false
let staticGpuPromise: Promise<void> | null = null

export interface CpuStatsInfo {
  FrequencyMhz: number
  Voltage: number
  Usage: number
  Temperature: number
}

export interface SystemInfoState {
  cpuTemp: number
  cpuPackagePower: number
  cpuStats: CpuStatsInfo | null
  gpuTemp: number
  fanSpeed: FanSpeedInfo
  gpuStats: GpuStats | null
  error: string | null
  loading: boolean
  pollingPaused: boolean
  staticGpuLoaded: boolean
}

export const useSystemInfoStore = defineStore('systemInfo', {
  state: (): SystemInfoState => ({
    cpuTemp: 0,
    cpuPackagePower: 0,
    cpuStats: null,
    gpuTemp: 0,
    fanSpeed: {
      CPUFanSpeed: 0,
      GPUFanSpeed: 0,
    },
    gpuStats: null,
    error: null,
    loading: false,
    pollingPaused: false,
    staticGpuLoaded: false,
  }),

  getters: {
    gpuName: (state) => state.gpuStats?.GpuName || 'N/A',
    gpuDriverVersion: (state) => state.gpuStats?.DriverVersion || 'N/A',
    gpuDriverDate: (state) => state.gpuStats?.DriverDate || 'N/A',
    gpuMemoryTotal: (state) => state.gpuStats?.MemoryTotal || 'N/A',
    gpuBusWidth: (state) => state.gpuStats?.BusWidth || 'N/A',
    gpuUtilization: (state) => parseInt(state.gpuStats?.GpuUtilization || '0', 10),
    gpuMemoryUtilization: (state) => parseInt(state.gpuStats?.MemoryUtilization || '0', 10),
    gpuCoreClock: (state) => parseInt(state.gpuStats?.CoreClock || '0', 10),
    gpuMemoryClock: (state) => parseInt(state.gpuStats?.MemoryClock || '0', 10),
    gpuFanSpeed: (state) => parseInt(state.gpuStats?.FanSpeed || '0', 10),
  },

  actions: {
    setPollingPaused(paused: boolean) {
      this.pollingPaused = paused
      if (!paused) void this.fetchSystemInfo()
    },

    async fetchStaticGpu() {
      if (this.staticGpuLoaded) return
      if (staticGpuPromise) return staticGpuPromise
      staticGpuPromise = this.loadStaticGpu()
      try {
        await staticGpuPromise
      } finally {
        staticGpuPromise = null
      }
    },

    async loadStaticGpu() {
      const [gpuName, gpuDriverVersion, gpuDriverDate, gpuMemoryTotal, gpuBusWidth] =
        await Promise.all([
          NvidiaGpu.GetGpuName(),
          NvidiaGpu.GetGpuDriverVersion(),
          NvidiaGpu.GetGpuDriverDate(),
          NvidiaGpu.GetGpuMemoryTotal(),
          NvidiaGpu.GetGpuBusWidth(),
        ])
      const prev = this.gpuStats
      this.gpuStats = {
        GpuName: gpuName.Success ? gpuName.Data : prev?.GpuName || 'N/A',
        DriverVersion: gpuDriverVersion.Success ? gpuDriverVersion.Data : prev?.DriverVersion || 'N/A',
        DriverDate: gpuDriverDate.Success ? gpuDriverDate.Data : prev?.DriverDate || 'N/A',
        MemoryTotal: gpuMemoryTotal.Success ? gpuMemoryTotal.Data : prev?.MemoryTotal || 'N/A',
        BusWidth: gpuBusWidth.Success ? gpuBusWidth.Data : prev?.BusWidth || 'N/A',
        GpuUtilization: prev?.GpuUtilization || '0',
        MemoryUtilization: prev?.MemoryUtilization || '0',
        CoreClock: prev?.CoreClock || '0',
        MemoryClock: prev?.MemoryClock || '0',
        FanSpeed: prev?.FanSpeed || '0',
        GpuTemperature: prev?.GpuTemperature || '0',
      }
      this.staticGpuLoaded = true
    },

    async fetchSystemInfo() {
      // 硬件调用可能慢于轮询间隔, 不加重入保护会导致请求叠加
      if (this.pollingPaused || fetching) return
      fetching = true
      this.loading = true
      try {
        if (!this.staticGpuLoaded) await this.fetchStaticGpu()
        const [
          cpuTemp,
          cpuUsage,
          cpuFreq,
          cpuVolt,
          cpuPower,
          gTemp,
          fSpeed,
          gpuUtil,
          gpuMemUtil,
          gpuCoreClock,
          gpuMemClock,
          gpuFanSpeed,
        ] = await Promise.all([
          CPU.GetCPUThermometer(),
          CPU.GetCpuUsage(),
          CPU.GetCpuFrequency(),
          CPU.GetCpuVoltage(),
          CPU.GetCpuPackagePower(),
          NvidiaGpu.GetGpuTemperature(),
          Fan.GetFanSpeed(),
          NvidiaGpu.GetGpuUtilization(),
          NvidiaGpu.GetGpuMemoryUtilization(),
          NvidiaGpu.GetGpuCoreClock(),
          NvidiaGpu.GetGpuMemoryClock(),
          NvidiaGpu.GetGpuFanSpeed(),
        ])

        this.cpuStats = {
          FrequencyMhz: cpuFreq.Success ? cpuFreq.Data : 0,
          Voltage: cpuVolt.Success ? cpuVolt.Data : 0,
          Usage: cpuUsage.Success ? cpuUsage.Data : 0,
          Temperature: cpuTemp.Success ? cpuTemp.Data : 0,
        }
        this.cpuTemp = cpuTemp.Success ? cpuTemp.Data : 0
        this.cpuPackagePower = cpuPower.Success ? Number(cpuPower.Data) || 0 : 0

        const prev = this.gpuStats
        this.gpuStats = {
          GpuName: prev?.GpuName || 'N/A',
          DriverVersion: prev?.DriverVersion || 'N/A',
          DriverDate: prev?.DriverDate || 'N/A',
          MemoryTotal: prev?.MemoryTotal || 'N/A',
          BusWidth: prev?.BusWidth || 'N/A',
          GpuUtilization: gpuUtil.Success ? String(gpuUtil.Data) : '0',
          MemoryUtilization: gpuMemUtil.Success ? String(gpuMemUtil.Data) : '0',
          CoreClock: gpuCoreClock.Success ? String(gpuCoreClock.Data) : '0',
          MemoryClock: gpuMemClock.Success ? String(gpuMemClock.Data) : '0',
          FanSpeed: gpuFanSpeed.Success ? String(gpuFanSpeed.Data) : '0',
          GpuTemperature: gTemp.Success ? String(gTemp.Data) : '0',
        }
        this.gpuTemp = gTemp.Success ? gTemp.Data : 0

        if (fSpeed.Success) {
          this.fanSpeed = fSpeed.Data
        }

        this.error = null
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to fetch system info'
      } finally {
        fetching = false
        this.loading = false
      }
    },

    startPolling(interval = POLL_INTERVAL_SYSTEM_INFO) {
      void this.fetchStaticGpu()
      void this.fetchSystemInfo()
      const polling = setInterval(() => this.fetchSystemInfo(), interval)
      return () => clearInterval(polling)
    },
  },
})
