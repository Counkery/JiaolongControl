<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  cpuUsage: number
  gpuUsage: number
  cpuTemp: number
  gpuTemp: number
  cpuFreqMhz: number
  gpuFreqMhz: number
  cpuPackageWatts: number
}>()

const clampPct = (value: number, max: number) =>
  Math.max(0, Math.min(100, max > 0 ? (value / max) * 100 : 0))

const rings = computed(() => [
  {
    key: 'cpu-use',
    label: 'CPU 使用率',
    text: `${Math.round(props.cpuUsage || 0)}%`,
    pct: clampPct(props.cpuUsage || 0, 100),
    color: '#7c3aed',
    scale: '',
  },
  {
    key: 'gpu-use',
    label: 'GPU 使用率',
    text: `${Math.round(props.gpuUsage || 0)}%`,
    pct: clampPct(props.gpuUsage || 0, 100),
    color: '#10b981',
    scale: '',
  },
  {
    key: 'cpu-temp',
    label: 'CPU 温度',
    text: `${Math.round(props.cpuTemp || 0)}℃`,
    pct: clampPct(props.cpuTemp || 0, 100),
    color: '#3b82f6',
    scale: '刻度 100℃',
  },
  {
    key: 'gpu-temp',
    label: 'GPU 温度',
    text: `${Math.round(props.gpuTemp || 0)}℃`,
    pct: clampPct(props.gpuTemp || 0, 100),
    color: '#8b5cf6',
    scale: '刻度 100℃',
  },
])

const cpuFreqText = computed(() =>
  props.cpuFreqMhz > 0 ? `${(props.cpuFreqMhz / 1000).toFixed(2)} GHz` : '—',
)
const gpuFreqText = computed(() => (props.gpuFreqMhz > 0 ? `${props.gpuFreqMhz} MHz` : '—'))
const packageText = computed(() =>
  props.cpuPackageWatts > 0 ? `${props.cpuPackageWatts.toFixed(1)} W` : '—',
)
</script>

<template>
  <div class="col-span-7 min-w-0 glass-card p-4 sm:p-5 flex flex-col overflow-hidden">
    <h2 class="text-[15px] font-medium text-ink/90 mb-2">核心监控</h2>
    <div class="flex-1 min-h-0 grid grid-cols-4 gap-2 items-center">
      <div v-for="ring in rings" :key="ring.key" class="min-w-0 flex flex-col items-center">
        <div
          class="meter w-full max-w-24 aspect-square"
          :style="{
            background: `conic-gradient(${ring.color} ${ring.pct}%, rgba(127,127,127,0.18) 0)`,
          }"
        >
          <div class="meter-hole">
            <span class="text-[13px] font-semibold font-mono leading-none">{{ ring.text }}</span>
          </div>
        </div>
        <span class="text-[11px] text-gray-400 mt-1 text-center leading-tight">{{ ring.label }}</span>
        <span class="text-[10px] text-gray-500 h-3">{{ ring.scale }}</span>
      </div>
    </div>
    <div class="mt-1 grid grid-cols-3 gap-2 min-w-0">
      <div class="min-w-0 rounded-xl bg-ink/[0.03] border border-ink/[0.05] px-2 py-1.5">
        <div class="text-[10px] text-gray-400">CPU 频率</div>
        <div class="text-[12px] font-mono font-semibold text-ink truncate">{{ cpuFreqText }}</div>
      </div>
      <div class="min-w-0 rounded-xl bg-ink/[0.03] border border-ink/[0.05] px-2 py-1.5">
        <div class="text-[10px] text-gray-400">封装功耗</div>
        <div class="text-[12px] font-mono font-semibold text-ink truncate">{{ packageText }}</div>
      </div>
      <div class="min-w-0 rounded-xl bg-ink/[0.03] border border-ink/[0.05] px-2 py-1.5">
        <div class="text-[10px] text-gray-400">GPU 频率</div>
        <div class="text-[12px] font-mono font-semibold text-ink truncate">{{ gpuFreqText }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.meter {
  border-radius: 9999px;
  display: grid;
  place-items: center;
}
.meter-hole {
  width: 74%;
  height: 74%;
  border-radius: 9999px;
  display: grid;
  place-items: center;
  background: var(--color-cyber-card);
}
</style>
