<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Message } from '@arco-design/web-vue'
import { Fan, PerformanceMode, SystemPerMode } from '@/utils/bridge'
import { useConfigStore } from '@/stores/config'
import FanSpeed from '@/components/common/FanSpeed.vue'

const loading = ref(false)
const configStore = useConfigStore()
if (!configStore.config) {
  await configStore.fetchConfig()
}

const FanPageStore = computed(() => configStore.config?.Fan)
const ceilingEnabled = ref(false)
const ceilingRpm = ref(3500)
const minRpm = ref(3500)
const maxRpm = ref(5000)
const modeLabel = ref('BalanceMode')

async function refreshCeiling() {
  try {
    const res = await Fan.GetFanCeilingRpm()
    if (res.Success && res.Data) {
      ceilingEnabled.value = !!res.Data.Enabled
      if (res.Data.Rpm > 0) ceilingRpm.value = res.Data.Rpm
      if (res.Data.MinRpm) minRpm.value = res.Data.MinRpm
      if (res.Data.MaxRpm) maxRpm.value = res.Data.MaxRpm
      if (res.Data.Mode) modeLabel.value = res.Data.Mode
    }
  } catch {
    /* ignore */
  }
}

onMounted(() => {
  void refreshCeiling()
})

watch(
  () => ceilingRpm.value,
  (v) => {
    if (v < minRpm.value) ceilingRpm.value = minRpm.value
    if (v > maxRpm.value) ceilingRpm.value = maxRpm.value
  },
)

async function applyCeiling() {
  if (!FanPageStore.value) return
  loading.value = true
  try {
    const rpm = ceilingEnabled.value ? ceilingRpm.value : 0
    const res = await Fan.SetFanCeilingRpm(rpm)
    if (!res.Success) {
      Message.error(res.Message || '设置失败')
      return
    }
    // 记住当前模式对应上限
    const modeRes = await PerformanceMode.Get()
    if (modeRes.Success && FanPageStore.value) {
      const m = modeRes.Data
      if (m === SystemPerMode.QuietMode) FanPageStore.value.QuietModeFanRpm = ceilingRpm.value
      else if (m === SystemPerMode.PerformanceMode)
        FanPageStore.value.PerformanceModeFanRpm = ceilingRpm.value
      else FanPageStore.value.BalanceModeFanRpm = ceilingRpm.value
      configStore.debouncedSave()
    }
    Message.success(res.Message || '已应用')
    await refreshCeiling()
  } finally {
    loading.value = false
  }
}

async function releaseToEc() {
  ceilingEnabled.value = false
  loading.value = true
  try {
    const res = await Fan.SetFanCeilingRpm(0)
    if (res.Success) Message.success(res.Message)
    else Message.error(res.Message)
    await refreshCeiling()
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div v-if="FanPageStore" class="h-full overflow-y-auto text-ink p-6 no-scrollbar">
    <div class="max-w-[1300px] mx-auto flex flex-col lg:flex-row gap-6">
      <div class="flex-1 space-y-6">
        <div>
          <h1 class="text-2xl font-bold tracking-wide">风扇转速上限</h1>
          <p class="text-[13px] text-gray-500 mt-1">
            官方语义：WMI 设置当前模式允许的最高转速（不是自定义温度曲线）。范围随模式变化。
          </p>
        </div>

        <div
          class="bg-panel/60 backdrop-blur-md border border-ink/[0.05] rounded-xl p-5 shadow-lg flex items-center justify-between gap-4"
        >
          <div>
            <h2 class="text-[13px] font-semibold text-gray-300">启用转速上限</h2>
            <p class="text-[11px] text-gray-500 mt-1">
              当前模式 {{ modeLabel }} · 合法 {{ minRpm }}–{{ maxRpm }} RPM
            </p>
          </div>
          <a-switch v-model="ceilingEnabled" />
        </div>

        <div
          class="bg-panel/60 backdrop-blur-md border border-ink/[0.05] rounded-xl p-6 shadow-lg"
        >
          <div class="flex flex-col gap-6">
            <div class="flex justify-between items-end">
              <div>
                <span class="text-[10px] font-bold text-purple-400 uppercase tracking-widest block"
                  >Fan Ceiling</span
                >
                <h2 class="text-base font-semibold mt-1 text-gray-200">上限转速</h2>
              </div>
              <div class="text-right">
                <span class="text-3xl font-black font-mono text-ink leading-none">{{
                  ceilingRpm
                }}</span>
                <span class="text-gray-500 text-xs ml-1 font-bold font-mono">RPM</span>
              </div>
            </div>

            <a-slider
              v-model="ceilingRpm"
              :min="minRpm"
              :max="maxRpm"
              :step="100"
              :disabled="!ceilingEnabled"
              class="w-full"
            />

            <div class="grid grid-cols-2 gap-4 mt-2">
              <button
                :disabled="loading || !ceilingEnabled"
                class="text-xs font-semibold text-white bg-gradient-to-r from-purple-700 to-indigo-600 hover:from-purple-600 hover:to-indigo-500 disabled:opacity-50 py-2.5 rounded-lg transition-all"
                @click="applyCeiling"
              >
                {{ loading ? '应用中...' : '应用上限' }}
              </button>
              <button
                class="text-xs font-semibold text-gray-300 hover:text-ink border border-ink/10 hover:border-ink/20 bg-ink/[0.02] hover:bg-ink/[0.05] py-2.5 rounded-lg transition-all"
                @click="releaseToEc"
              >
                交回 EC 自动
              </button>
            </div>
          </div>
        </div>
      </div>

      <div class="w-full lg:w-[360px] shrink-0 space-y-6 lg:pt-[80px]">
        <FanSpeed />
        <div
          class="bg-panel/60 backdrop-blur-md border border-ink/[0.05] rounded-xl p-5 shadow-lg space-y-2.5 select-none"
        >
          <h2 class="text-[13px] font-semibold text-gray-300">说明</h2>
          <div class="text-[11px] text-gray-500 leading-relaxed space-y-2">
            <p>
              <strong>安静 2200–3500</strong> /
              <strong>均衡 3500–5000</strong> /
              <strong>高性能 5000–5800</strong>。
            </p>
            <p>
              日常推荐：主页「安静日常」= 均衡 + 上限 3500。EC 仍会在上限下按固件曲线升降速。
            </p>
            <p>
              「温度曲线」页为进阶软件轮询（Blding），与本页官方上限路径不同，请勿同时开启。
            </p>
          </div>
        </div>
      </div>
    </div>
  </div>

  <div v-else class="flex items-center justify-center h-full">
    <a-spin dot />
  </div>
</template>

<style lang="scss" scoped>
.no-scrollbar::-webkit-scrollbar {
  display: none;
}
.no-scrollbar {
  -ms-overflow-style: none;
  scrollbar-width: none;
}
</style>
