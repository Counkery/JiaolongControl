<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { Message, Modal } from '@arco-design/web-vue'
import { AutoFanControl, Fan, PerformanceMode, Power, SystemInfo, SystemPerMode } from '@/utils/bridge'
import { useConfigStore } from '@/stores/config'
import { useSystemInfoStore } from '@/stores/systemInfo'
import useStore from '@/stores'
import QuickLocks from '@/components/common/QuickLocks.vue'
import { chartTheme } from '@/theme/theme'
import { storeToRefs } from 'pinia'
import imgCPU from '@/assets/icon/iconCPU.png'
import imgGPU from '@/assets/icon/gpu2.png'
import imgFan from '@/assets/icon/iconFan.png'
import iconQuiet from '@/assets/icon/iconQuiet.png'
import iconBalanced from '@/assets/icon/iconBalanced.png'
import iconPerformance from '@/assets/icon/iconPerformance.png'
import PerformanceModeComp from './Home/PerformanceMode.vue'
import CoreMonitoringComp from './Home/CoreMonitoring.vue'
import WelcomeBannerComp from './Home/WelcomeBanner.vue'

const router = useRouter()
const uiStore = useStore()

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent, LegendComponent])

const systemInfoStore = useSystemInfoStore()
const configStore = useConfigStore()
const { cpuTemp, gpuTemp, fanSpeed, gpuStats, cpuStats, cpuPackagePower } =
  storeToRefs(systemInfoStore)

const performanceModes = ref([
  { id: SystemPerMode.QuietMode, name: '静音', icon: iconQuiet, active: false },
  { id: SystemPerMode.BalanceMode, name: '均衡', icon: iconBalanced, active: false },
  { id: SystemPerMode.PerformanceMode, name: '高性能', icon: iconPerformance, active: false },
])

const activeMode = computed(
  () => performanceModes.value.find((m) => m.active) || { name: '均衡', icon: iconBalanced },
)

async function fetchPerformanceMode() {
  try {
    const res = await PerformanceMode.Get()
    if (res.Success) {
      performanceModes.value.forEach((e) => {
        e.active = e.id === res.Data
      })
    }
  } catch (e) {
    console.error(e)
  }
}

const activeScene = ref<string | null>(null)
const fanPathText = ref('风扇：读取中')
const scenePromptDismissed = ref(false)

function sceneLabel(scene: string | null) {
  if (scene === 'Portable135') return '便携 135W'
  if (scene === 'QuietDaily') return '安静日常'
  return ''
}

async function refreshScene() {
  try {
    const res = await PerformanceMode.GetQuietDailyActive()
    activeScene.value = res.Success && res.Data?.Active ? res.Data.Scene || 'QuietDaily' : null
  } catch {
    activeScene.value = null
  }
}

async function refreshFanPath() {
  try {
    const [running, ceiling] = await Promise.all([
      AutoFanControl.IsRunning(),
      Fan.GetFanCeilingRpm(),
    ])
    if (running.Success && running.Data) {
      fanPathText.value = '风扇：软件曲线（强制转速）'
      return
    }
    if (ceiling.Success && ceiling.Data?.Enabled) {
      fanPathText.value = `风扇：WMI 上限 ${ceiling.Data.Rpm} RPM`
      return
    }
    fanPathText.value = '风扇：EC 自动'
  } catch {
    fanPathText.value = '风扇：状态未知'
  }
}

function confirmLeavePortable(nextLabel: string) {
  if (activeScene.value !== 'Portable135') return Promise.resolve(true)
  return new Promise<boolean>((resolve) => {
    let settled = false
    const finish = (ok: boolean) => {
      if (settled) return
      settled = true
      resolve(ok)
    }
    Modal.confirm({
      title: '退出便携 135W',
      content: `切换到「${nextLabel}」会恢复更高的 CPU / GPU 功耗，135W 电源可能跳保护。`,
      okText: '仍然切换',
      cancelText: '保持便携',
      onOk: () => finish(true),
      onCancel: () => finish(false),
    })
  })
}

async function rememberScene(scene: string) {
  if (!configStore.config) return
  configStore.config.App.LastPowerScene = scene
  try {
    await configStore.saveConfig()
  } catch {
    /* 提示用，失败不影响已经下发的硬件 */
  }
}

function setMode(id: SystemPerMode) {
  const mode = performanceModes.value.find((m) => m.id === id)
  void (async () => {
    const ok = await confirmLeavePortable(mode?.name || '其它模式')
    if (!ok) return
    performanceModes.value.forEach((m) => {
      m.active = m.id === id
    })
    try {
      const res = await PerformanceMode.Set(id)
      if (res.Success) {
        Message.success(res.Message || `已切换到${mode?.name || '该模式'}`)
      } else {
        Message.error(res.Message || '切换失败')
        await fetchPerformanceMode()
      }
    } catch {
      Message.error('切换性能模式失败')
      await fetchPerformanceMode()
    }
    await refreshScene()
    await refreshFanPath()
  })()
}

function goRoute(name: string) {
  uiStore.syncFromRoute(name)
  if (router.currentRoute.value.name !== name) {
    void router.push({ name })
  }
}

const quietDailyLoading = ref(false)
const portable135Loading = ref(false)
async function applyQuietDaily() {
  const ok = await confirmLeavePortable('安静日常')
  if (!ok) return
  quietDailyLoading.value = true
  try {
    const res = await PerformanceMode.ApplyQuietDaily()
    if (res.Success) {
      Message.success(res.Message || '已应用安静日常')
      await rememberScene('QuietDaily')
      await fetchPerformanceMode()
      await refreshScene()
      await refreshFanPath()
    } else {
      Message.error(res.Message || '应用失败')
    }
  } catch {
    Message.error('应用安静日常失败')
  } finally {
    quietDailyLoading.value = false
  }
}

async function applyPortable135() {
  portable135Loading.value = true
  try {
    const res = await PerformanceMode.ApplyPortable135()
    if (res.Success) {
      Message.success(res.Message || '已应用便携 135W')
      await rememberScene('Portable135')
      await fetchPerformanceMode()
      await refreshScene()
      await refreshFanPath()
    } else {
      Message.error(res.Message || '应用失败')
    }
  } catch {
    Message.error('应用便携 135W 失败')
  } finally {
    portable135Loading.value = false
  }
}

const freqPresets = [
  { mhz: 4500, label: '4.50 GHz' },
  { mhz: 4750, label: '4.75 GHz' },
  { mhz: 5150, label: '5.15 GHz' },
  { mhz: 0, label: '不限制' },
]
const freqApplying = ref(false)
/** 自定义频率输入 (MHz)；与滑条/预设同步 */
const freqInputMhz = ref<number | undefined>(undefined)

function activeCpuBlock() {
  const cpu = configStore.config?.Cpu
  if (!cpu) return null
  const map: Record<string, 'Default' | 'Performance' | 'Saving' | 'Custom'> = {
    default: 'Default',
    performance: 'Performance',
    saving: 'Saving',
    custom: 'Custom',
  }
  return cpu[map[cpu.CpuProfile] ?? 'Default']
}

const freqMhz = computed(() => activeCpuBlock()?.CpuMaxFrequency ?? 5150)
const freqLabel = computed(() =>
  freqMhz.value <= 0 ? '不限制' : `${(freqMhz.value / 1000).toFixed(2)} GHz`,
)
const liveFreqGhz = computed(() =>
  cpuStats.value?.FrequencyMhz ? (cpuStats.value.FrequencyMhz / 1000).toFixed(2) : '—',
)

watch(
  freqMhz,
  (v) => {
    freqInputMhz.value = v
  },
  { immediate: true },
)

async function applyFreqCap(mhz: number) {
  const block = activeCpuBlock()
  if (!block || freqApplying.value) return
  const clamped = mhz <= 0 ? 0 : Math.round(Math.min(5400, Math.max(400, mhz)))
  freqApplying.value = true
  block.CpuMaxFrequency = clamped
  freqInputMhz.value = clamped
  try {
    const res =
      clamped <= 0 ? await Power.ResetCPUMaxFrequency() : await Power.SetCPUMaxFrequency(clamped)
    if (!res.Success) {
      Message.error(res.Message || '频率上限设置失败')
      return
    }
    await configStore.saveConfig()
    Message.success(clamped <= 0 ? '已取消 CPU 频率上限' : `CPU 频率上限 ${clamped} MHz`)
  } catch {
    Message.error('频率上限设置失败')
  } finally {
    freqApplying.value = false
  }
}

function onFreqSlider(value: number | [number, number]) {
  const mhz = Array.isArray(value) ? value[0] : value
  void applyFreqCap(mhz)
}

function commitFreqInput() {
  const raw = freqInputMhz.value
  if (raw === undefined || raw === null || Number.isNaN(Number(raw))) {
    freqInputMhz.value = freqMhz.value
    return
  }
  void applyFreqCap(Number(raw))
}

const gpuCoreMhz = computed(() => parseInt(gpuStats.value?.CoreClock || '0', 10) || 0)
const cpuUsage = computed(() => systemInfoStore.cpuStats?.Usage ?? 0)
const gpuUsage = computed(() => parseInt(gpuStats.value?.GpuUtilization || '0', 10))
// 无噪音传感器: 以风扇转速查表估算 (标定: 3000 RPM≈25 dBA, 4800 RPM≈40 dBA)
const NOISE_CALIBRATION: Array<[rpm: number, dba: number]> = [
  [1500, 19],
  [3000, 25],
  [4800, 40],
  [5800, 45],
  [6800, 48],
]
const noiseLevel = computed(() => {
  const rpm = Math.max(fanSpeed.value.CPUFanSpeed, fanSpeed.value.GPUFanSpeed)
  const pts = NOISE_CALIBRATION
  if (rpm <= pts[0]![0]) return pts[0]![1]
  for (let i = 1; i < pts.length; i++) {
    const [hiRpm, hiDba] = pts[i]!
    if (rpm <= hiRpm) {
      const [loRpm, loDba] = pts[i - 1]!
      return Math.round(loDba + ((rpm - loRpm) / (hiRpm - loRpm)) * (hiDba - loDba))
    }
  }
  return pts[pts.length - 1]![1]
})

const sysCpuName = ref('Loading...')
const sysGpuName = ref('Loading...')
const sysMemory = ref('Loading...')
const sysOs = ref('Loading...')

// 模拟历史数据
const tempHistory = ref<{ cpu: number | null; gpu: number | null }[]>(
  Array(10).fill({ cpu: null, gpu: null }),
)

const powerHistory = ref<number[]>(Array(24).fill(0))

const powerPointsString = computed(() => {
  const max = Math.max(40, ...powerHistory.value)
  return powerHistory.value
    .map((w, i) => {
      const x = (i / (powerHistory.value.length - 1)) * 300
      const y = 54 - (w / max) * 46
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
})

async function fetchStaticInfo() {
  try {
    const res = await SystemInfo.GetSystemOverview()
    if (res.Success && res.Data) {
      sysCpuName.value = res.Data.CpuName
      sysGpuName.value = res.Data.GpuName
      sysMemory.value = res.Data.MemoryInfo
      sysOs.value = res.Data.OsVersion
    }
  } catch (e) {
    console.error('Failed to fetch system info', e)
  }
}

let historyTimer: ReturnType<typeof setInterval> | null = null
let fanTimer: ReturnType<typeof setInterval> | null = null

function startTimers() {
  stopTimers()
  historyTimer = setInterval(() => {
    tempHistory.value.push({ cpu: cpuTemp.value, gpu: gpuTemp.value })
    if (tempHistory.value.length > 10) tempHistory.value.shift()
    powerHistory.value.push(cpuPackagePower.value || 0)
    if (powerHistory.value.length > 24) powerHistory.value.shift()
  }, 2000)
  fanTimer = setInterval(() => void refreshFanPath(), 5000)
}

function stopTimers() {
  if (historyTimer) {
    clearInterval(historyTimer)
    historyTimer = null
  }
  if (fanTimer) {
    clearInterval(fanTimer)
    fanTimer = null
  }
}

function handleVisibilityChange() {
  if (document.hidden) {
    stopTimers()
  } else {
    startTimers()
  }
}

function onSceneChanged() {
  void refreshScene()
}

const suggestedScene = computed(() => configStore.config?.App.LastPowerScene || '')
const showScenePrompt = computed(
  () =>
    !scenePromptDismissed.value &&
    !activeScene.value &&
    (suggestedScene.value === 'Portable135' || suggestedScene.value === 'QuietDaily'),
)

async function applySuggestedScene() {
  if (suggestedScene.value === 'Portable135') await applyPortable135()
  else if (suggestedScene.value === 'QuietDaily') await applyQuietDaily()
  scenePromptDismissed.value = true
}

function dismissScenePrompt() {
  scenePromptDismissed.value = true
  if (configStore.config) {
    configStore.config.App.LastPowerScene = ''
    void configStore.saveConfig()
  }
}

onMounted(() => {
  fetchStaticInfo()
  fetchPerformanceMode()
  void refreshScene()
  void refreshFanPath()
  startTimers()
  document.addEventListener('visibilitychange', handleVisibilityChange)
  window.addEventListener('jiaolong-quiet-daily-changed', onSceneChanged)
})

onUnmounted(() => {
  stopTimers()
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  window.removeEventListener('jiaolong-quiet-daily-changed', onSceneChanged)
})

// 2. 温度曲线 - 折线图
const lineChartOption = computed(() => ({
  grid: { top: 30, bottom: 20, left: 35, right: 10 },
  legend: {
    data: ['CPU', 'GPU'],
    icon: 'roundRect',
    itemWidth: 12,
    itemHeight: 4,
    textStyle: { color: chartTheme.value.legend, fontSize: 10 },
    top: 0,
  },
  xAxis: {
    type: 'category',
    data: Array(10).fill(''),
    axisLine: { show: false },
    axisTick: { show: false },
    axisLabel: { color: chartTheme.value.axis, fontSize: 10, margin: 12 },
  },
  yAxis: {
    type: 'value',
    min: 0,
    max: 100,
    interval: 25,
    splitLine: { lineStyle: { color: chartTheme.value.line } },
    axisLabel: { color: chartTheme.value.axis, fontSize: 10, formatter: '{value}°C' },
  },
  series: [
    {
      name: 'CPU',
      data: tempHistory.value.map((i) => i.cpu),
      type: 'line',
      smooth: true,
      symbol: 'circle',
      symbolSize: 6,
      lineStyle: { color: '#3B82F6', width: 3 },
      itemStyle: { color: '#3B82F6' },
    },
    {
      name: 'GPU',
      data: tempHistory.value.map((i) => i.gpu),
      type: 'line',
      smooth: true,
      symbol: 'circle',
      symbolSize: 6,
      lineStyle: { color: '#10B981', width: 3 },
      itemStyle: { color: '#10B981' },
    },
  ],
}))
</script>

<template>
  <div class="jl-page">
    <!-- Row 1: 顶部 Banner -->
    <WelcomeBannerComp
      :cpu-temp="cpuTemp"
      :gpu-temp="gpuTemp"
      :active-mode-name="activeMode.name"
      :active-mode-icon="activeMode.icon"
    />

    <div
      v-if="activeScene"
      class="rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-[12px] text-amber-100"
    >
      当前场景：{{ sceneLabel(activeScene) }}。切到其它性能模式或在 CPU 页点「应用」会退出；若正在用便携电源，退出前会再确认一次。
    </div>
    <div
      v-else-if="showScenePrompt"
      class="rounded-2xl border border-ink/10 bg-ink/[0.03] px-4 py-3 flex flex-wrap items-center justify-between gap-3"
    >
      <p class="text-[12px] text-gray-300">
        上次使用了「{{ sceneLabel(suggestedScene) }}」。适配器瓦数读不到，不会自动套用。
      </p>
      <div class="flex gap-2">
        <button
          class="text-[11px] px-3 py-1.5 rounded-lg bg-purple-600/80 text-white"
          @click="applySuggestedScene"
        >
          套用
        </button>
        <button
          class="text-[11px] px-3 py-1.5 rounded-lg border border-ink/10 text-gray-300"
          @click="dismissScenePrompt"
        >
          不再提示
        </button>
      </div>
    </div>

    <!-- Row 2: 模式 & 监控 -->
    <div class="grid grid-cols-12 gap-3 min-h-[260px] h-[290px] min-w-0">
      <PerformanceModeComp
        :modes="performanceModes"
        :quiet-daily-loading="quietDailyLoading"
        :portable135-loading="portable135Loading"
        :active-scene-label="sceneLabel(activeScene) || undefined"
        @change-mode="setMode"
        @quiet-daily="applyQuietDaily"
        @portable-135="applyPortable135"
      />
      <CoreMonitoringComp
        :cpu-usage="cpuUsage"
        :gpu-usage="gpuUsage"
        :cpu-temp="cpuTemp"
        :gpu-temp="gpuTemp"
        :cpu-freq-mhz="cpuStats?.FrequencyMhz ?? 0"
        :gpu-freq-mhz="gpuCoreMhz"
        :cpu-package-watts="cpuPackagePower"
      />
    </div>

    <QuickLocks variant="compact" />

    <div class="jl-panel space-y-3 min-w-0">
      <div class="flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h2 class="text-[15px] font-medium text-ink/90">CPU 频率</h2>
          <p class="text-[12px] text-gray-300 mt-0.5 font-mono">
            当前 {{ liveFreqGhz }} GHz
            <span class="text-gray-500"> / </span>
            上限 {{ freqLabel }}
          </p>
        </div>
        <span class="text-[11px] text-gray-500">写入当前电源方案，并记到正在使用的 CPU 档</span>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <button
          v-for="p in freqPresets"
          :key="p.mhz"
          class="jl-btn-quiet"
          :disabled="freqApplying"
          @click="applyFreqCap(p.mhz)"
        >
          {{ p.label }}
        </button>
        <div class="flex items-center gap-1.5 ml-auto">
          <a-input-number
            v-model="freqInputMhz"
            :disabled="freqApplying"
            :min="0"
            :max="5400"
            :step="50"
            :precision="0"
            hide-button
            size="small"
            class="!w-[108px]"
            placeholder="MHz"
            @press-enter="commitFreqInput"
            @blur="commitFreqInput"
          />
          <span class="text-[11px] text-gray-500">MHz</span>
          <button
            type="button"
            class="jl-btn-quiet"
            :disabled="freqApplying"
            @click="commitFreqInput"
          >
            应用
          </button>
        </div>
      </div>
      <a-slider
        :model-value="freqMhz"
        :min="0"
        :max="5400"
        :step="50"
        :disabled="freqApplying"
        @change="onFreqSlider"
      />
    </div>

    <!-- Row 3: 系统概览 & 风扇 & 曲线 -->
    <div class="grid grid-cols-12 gap-6 h-[280px]">
      <!-- 系统概览 -->
      <div class="col-span-4 jl-panel jl-panel--roomy flex flex-col">
        <h2 class="text-[15px] font-medium text-ink/90 mb-4">系统概览</h2>
        <div class="flex-1 flex flex-col justify-between">
          <div class="flex items-center gap-4">
            <div
              class="badge-blue w-8 h-8 rounded-full flex items-center justify-center text-blue-500"
            >
              <span
                class="icon-mask icon-tint-blue w-4 h-4"
                :style="{ WebkitMaskImage: `url(${imgCPU})`, maskImage: `url(${imgCPU})` }"
              ></span>
            </div>
            <div>
              <div class="text-xs text-ink/90">CPU</div>
              <div class="text-xs text-gray-500 mt-0.5">{{ sysCpuName }}</div>
            </div>
          </div>
          <div class="flex items-center gap-4">
            <div
              class="badge-green w-8 h-8 rounded-full flex items-center justify-center text-green-500"
            >
              <span
                class="icon-mask icon-tint-green w-4 h-4"
                :style="{ WebkitMaskImage: `url(${imgGPU})`, maskImage: `url(${imgGPU})` }"
              ></span>
            </div>
            <div>
              <div class="text-xs text-ink/90">GPU</div>
              <div class="text-xs text-gray-500 mt-0.5">{{ sysGpuName }}</div>
            </div>
          </div>
          <div class="flex items-center gap-4">
            <div
              class="badge-yellow w-8 h-8 rounded-full flex items-center justify-center text-yellow-500"
            >
              <icon-storage />
            </div>
            <div>
              <div class="text-xs text-ink/90">内存</div>
              <div class="text-xs text-gray-500 mt-0.5">{{ sysMemory }}</div>
            </div>
          </div>
          <div class="flex items-center gap-4">
            <div
              class="badge-red w-8 h-8 rounded-full flex items-center justify-center text-red-500"
            >
              <icon-computer />
            </div>
            <div>
              <div class="text-xs text-ink/90">系统</div>
              <div class="text-xs text-gray-500 mt-0.5">{{ sysOs }}</div>
            </div>
          </div>
        </div>
      </div>

      <!-- 风扇与噪音 -->
      <div class="col-span-4 jl-panel jl-panel--roomy flex flex-col">
        <div class="flex items-center justify-between gap-2 mb-4">
          <h2 class="text-[15px] font-medium text-ink/90">风扇与噪音</h2>
          <div class="flex gap-1.5">
            <button type="button" class="jl-btn-quiet" @click="goRoute('fan')">风扇上限</button>
            <button type="button" class="jl-btn-quiet" @click="goRoute('fan-curve')">软件曲线</button>
          </div>
        </div>

        <div class="flex items-center gap-4 mb-6">
          <div
            class="badge-neutral w-12 h-12 rounded-full flex items-center justify-center overflow-hidden"
          >
            <span
              class="icon-mask icon-tint-blue-bright w-7 h-7 animate-spin"
              style="animation-duration: 3s"
              :style="{ WebkitMaskImage: `url(${imgFan})`, maskImage: `url(${imgFan})` }"
            ></span>
          </div>
          <div>
            <div class="flex items-baseline gap-1">
              <span class="text-3xl font-semibold">{{
                Math.max(fanSpeed.CPUFanSpeed, fanSpeed.GPUFanSpeed)
              }}</span>
              <span class="text-xs text-gray-400">RPM</span>
            </div>
            <div class="text-xs text-gray-500">风扇转速</div>
            <div class="text-[11px] text-gray-400 mt-1">{{ fanPathText }}</div>
          </div>
        </div>

        <div class="text-[11px] text-gray-500 mb-1">CPU 封装功耗</div>
        <div class="h-16 flex items-center justify-center mb-4 overflow-hidden">
          <svg class="w-full h-full" viewBox="0 0 300 60" preserveAspectRatio="none">
            <defs>
              <!-- 水平方向渐变色，从紫色过渡到蓝色，再到浅绿 -->
              <linearGradient id="ecgGrad" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stop-color="#8A2BE2" stop-opacity="0.3" />
                <stop offset="50%" stop-color="#3B82F6" stop-opacity="0.8" />
                <stop offset="100%" stop-color="#10B981" stop-opacity="1" />
              </linearGradient>
              <!-- 霓虹发光滤镜 -->
              <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                <feGaussianBlur stdDeviation="1.2" result="blur" />
                <feMerge>
                  <feMergeNode in="blur" />
                  <feMergeNode in="SourceGraphic" />
                </feMerge>
              </filter>
            </defs>
            <polyline
              fill="none"
              stroke="url(#ecgGrad)"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              filter="url(#glow)"
              :points="powerPointsString"
            />
          </svg>
        </div>

        <div class="mt-auto">
          <div class="flex items-baseline gap-1">
            <span class="text-2xl font-semibold">{{ noiseLevel }}</span>
            <span class="text-xs text-gray-400">dBA</span>
          </div>
          <div class="text-xs text-gray-500">当前噪音 (估算)</div>
        </div>
      </div>

      <!-- 温度走势 -->
      <div class="col-span-4 jl-panel jl-panel--roomy flex flex-col">
        <h2 class="text-[15px] font-medium text-ink/90 mb-2">温度走势</h2>
        <div class="flex-1">
          <VChart :option="lineChartOption" autoresize />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.echarts {
  width: 100%;
  height: 100%;
}
</style>
