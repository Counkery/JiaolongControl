<script setup lang="ts">
import { SystemPerMode } from '@/utils/bridge'

defineProps<{
  modes: Array<{
    id: SystemPerMode
    name: string
    icon: string
    active: boolean
  }>
  quietDailyLoading?: boolean
  portable135Loading?: boolean
  /** 当前场景文案，如「安静日常」「便携 135W」 */
  activeSceneLabel?: string
}>()

const emit = defineEmits<{
  (e: 'change-mode', id: SystemPerMode): void
  (e: 'quiet-daily'): void
  (e: 'portable-135'): void
}>()
</script>

<template>
  <div class="col-span-5 min-w-0 jl-panel jl-panel--roomy flex flex-col">
    <div class="flex items-center justify-between mb-4 gap-3">
      <div class="flex items-center gap-2 min-w-0">
        <h2 class="text-[15px] font-medium text-ink/90 shrink-0">性能模式</h2>
        <span
          v-if="activeSceneLabel"
          class="text-[10px] font-semibold px-2 py-0.5 rounded-md bg-amber-500/20 text-amber-200 border border-amber-500/30 truncate"
        >
          {{ activeSceneLabel }}
        </span>
      </div>
      <div class="flex items-center gap-2 flex-wrap justify-end">
        <button
          :disabled="quietDailyLoading || portable135Loading"
          class="text-[11px] font-semibold px-3 py-1.5 rounded-lg border border-ink/10 hover:bg-ink/5 disabled:opacity-50 text-gray-300"
          title="均衡 + 转速上限 3500 + 温度墙 93℃"
          @click="emit('quiet-daily')"
        >
          {{ quietDailyLoading ? '应用中…' : '安静日常' }}
        </button>
        <button
          :disabled="quietDailyLoading || portable135Loading"
          class="text-[11px] font-semibold px-3 py-1.5 rounded-lg border border-amber-500/30 hover:bg-amber-500/10 disabled:opacity-50 text-amber-200/90"
          title="适配家里 135W 砖：办公 + CPU 45/55W + GPU 60W + 频率 4500"
          @click="emit('portable-135')"
        >
          {{ portable135Loading ? '应用中…' : '便携 135W' }}
        </button>
      </div>
    </div>
    <div class="flex-1 grid grid-cols-3 gap-3 items-center">
      <button
        v-for="mode in modes"
        :key="mode.id"
        :class="[
          'py-6 rounded-2xl flex flex-col items-center justify-center gap-3 transition-all duration-300',
          mode.active
            ? 'mode-btn-active bg-purple-900/20 border'
            : 'bg-panel-raised border border-ink/[0.06] hover:bg-ink/5',
        ]"
        @click="emit('change-mode', mode.id)"
      >
        <span
          v-if="mode.active"
          class="icon-mask icon-silhouette-accent w-10 h-10 transition-all duration-300 opacity-100 scale-110"
          :style="{ WebkitMaskImage: `url(${mode.icon})`, maskImage: `url(${mode.icon})` }"
        ></span>
        <span
          v-else
          class="icon-mask icon-silhouette w-10 h-10 transition-all duration-300 opacity-65 group-hover:opacity-100"
          :style="{ WebkitMaskImage: `url(${mode.icon})`, maskImage: `url(${mode.icon})` }"
        ></span>
        <span :class="['text-xs', mode.active ? 'text-ink font-medium' : 'text-gray-400']">{{
          mode.name
        }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.mode-btn-active {
  border-color: #a855f7;
  box-shadow: 0 0 15px rgba(138, 43, 226, 0.3);
}

[data-theme='light'] .mode-btn-active {
  border-color: rgba(13, 14, 21, 0.16);
  background-color: rgba(13, 14, 21, 0.05);
  box-shadow: none;
}
</style>
