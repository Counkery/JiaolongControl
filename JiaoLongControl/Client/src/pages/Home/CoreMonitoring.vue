<script setup lang="ts">
import { computed } from 'vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { PieChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { chartTheme } from '@/theme/theme'

use([CanvasRenderer, PieChart, GridComponent, TooltipComponent])

const props = defineProps<{
  cpuUsage: number
  gpuUsage: number
  cpuTemp: number
  gpuTemp: number
  cpuFreqMhz: number
  gpuFreqMhz: number
}>()

const cpuFreqText = computed(() =>
  props.cpuFreqMhz > 0 ? `${(props.cpuFreqMhz / 1000).toFixed(2)} GHz` : '—',
)
const gpuFreqText = computed(() => (props.gpuFreqMhz > 0 ? `${props.gpuFreqMhz} MHz` : '—'))

const getRingOption = (
  value: number,
  colorStart: string,
  colorEnd: string,
  suffix: string = '%',
) => ({
  series: [
    {
      type: 'pie',
      radius: ['75%', '90%'],
      avoidLabelOverlap: false,
      silent: true,
      label: {
        show: true,
        position: 'center',
        formatter: () => `${value}${suffix}`,
        fontSize: 18,
        fontWeight: 'bold',
        color: chartTheme.value.label,
      },
      data: [
        {
          value: value,
          itemStyle: {
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 1,
              y2: 1,
              colorStops: [
                { offset: 0, color: colorStart },
                { offset: 1, color: colorEnd },
              ],
            },
            borderRadius: 10,
          },
        },
        {
          value: 100 - value,
          itemStyle: { color: chartTheme.value.line },
        },
      ],
    },
  ],
})

const cpuUsageOption = computed(() => getRingOption(props.cpuUsage || 0, '#3B82F6', '#8A2BE2'))
const gpuUsageOption = computed(() => getRingOption(props.gpuUsage || 0, '#10B981', '#3B82F6'))
const cpuTempOption = computed(() => getRingOption(props.cpuTemp || 0, '#3B82F6', '#3B82F6', '°C'))
const gpuTempOption = computed(() => getRingOption(props.gpuTemp || 0, '#8A2BE2', '#8A2BE2', '°C'))
</script>

<template>
  <div class="col-span-7 min-w-0 glass-card p-4 sm:p-6 flex flex-col overflow-hidden">
    <h2 class="text-[15px] font-medium text-ink/90 mb-2">核心监控</h2>
    <div class="flex-1 min-h-0 grid grid-cols-4 gap-2 items-center">
      <div class="min-w-0 flex flex-col items-center">
        <div class="w-full max-w-28 aspect-square relative">
          <VChart :option="cpuUsageOption" autoresize />
        </div>
        <span class="text-[11px] text-gray-400 mt-1 text-center">CPU 使用率</span>
      </div>
      <div class="min-w-0 flex flex-col items-center">
        <div class="w-full max-w-28 aspect-square relative">
          <VChart :option="gpuUsageOption" autoresize />
        </div>
        <span class="text-[11px] text-gray-400 mt-1 text-center">GPU 使用率</span>
      </div>
      <div class="min-w-0 flex flex-col items-center">
        <div class="w-full max-w-28 aspect-square relative">
          <VChart :option="cpuTempOption" autoresize />
        </div>
        <span class="text-[11px] text-gray-400 mt-1 text-center">CPU 温度</span>
      </div>
      <div class="min-w-0 flex flex-col items-center">
        <div class="w-full max-w-28 aspect-square relative">
          <VChart :option="gpuTempOption" autoresize />
        </div>
        <span class="text-[11px] text-gray-400 mt-1 text-center">GPU 温度</span>
      </div>
    </div>
    <div class="mt-2 grid grid-cols-2 gap-2 min-w-0">
      <div class="min-w-0 rounded-xl bg-ink/[0.03] border border-ink/[0.05] px-3 py-1.5 flex items-baseline justify-between gap-2">
        <span class="text-[11px] text-gray-400 shrink-0">CPU 频率</span>
        <span class="text-[13px] font-mono font-semibold text-ink truncate">{{ cpuFreqText }}</span>
      </div>
      <div class="min-w-0 rounded-xl bg-ink/[0.03] border border-ink/[0.05] px-3 py-1.5 flex items-baseline justify-between gap-2">
        <span class="text-[11px] text-gray-400 shrink-0">GPU 频率</span>
        <span class="text-[13px] font-mono font-semibold text-ink truncate">{{ gpuFreqText }}</span>
      </div>
    </div>
  </div>
</template>
