<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Message } from '@arco-design/web-vue'
import { Config, SystemInfo } from '@/utils/bridge'
import { useConfigStore } from '@/stores/config'

const configStore = useConfigStore()
const toolDir = ref('')
const resolved = ref('')
const low = ref(60)
const high = ref(80)
const loading = ref(false)

onMounted(async () => {
  if (!configStore.config) await configStore.fetchConfig()
  toolDir.value = configStore.config?.App.BatteryThresholdToolDir ?? ''
  await probe()
})

async function probe() {
  const res = await Config.ResolveBatteryThresholdToolDir()
  if (res.Success && res.Data) resolved.value = String(res.Data)
  else resolved.value = ''
}

async function saveDir() {
  if (!configStore.config) return
  configStore.config.App.BatteryThresholdToolDir = toolDir.value.trim()
  const save = await configStore.saveConfig()
  if (save?.Success) {
    Message.success('路径已保存')
    await probe()
  } else Message.error(save?.Message || '保存失败')
}

async function launch(enable: boolean) {
  loading.value = true
  try {
    if (toolDir.value.trim() && configStore.config) {
      configStore.config.App.BatteryThresholdToolDir = toolDir.value.trim()
      await configStore.saveConfig()
    }
    const res = await Config.LaunchBatteryThreshold(enable, low.value, high.value, false)
    if (res.Success) Message.success(res.Message)
    else Message.error(res.Message)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div
    class="jl-panel space-y-4"
  >
    <div>
      <h2 class="text-[15px] font-semibold text-ink">电池充电阈值（外挂）</h2>
      <p class="text-[12px] text-gray-500 mt-1 leading-relaxed">
        调用
        <button
          type="button"
          class="text-purple-400 hover:underline"
          @click="
            SystemInfo.OpenUrl('https://github.com/Zcodeoooo/JiaoLong16Pro-Battery-threshold')
          "
        >
          Zcodeoooo/JiaoLong16Pro-Battery-threshold
        </button>
        的 NativeEnable/Disable。不内嵌 WinRing0；需管理员与工具目录。
      </p>
    </div>

    <div class="space-y-2">
      <label class="text-[11px] text-gray-400">工具 Windows 目录（含 NativeEnable.cmd）</label>
      <div class="flex gap-2">
        <a-input v-model="toolDir" placeholder="D:\...\JiaoLong16Pro-Battery-threshold\Windows" />
        <button
          class="text-xs px-3 py-1.5 rounded-lg border border-ink/10 hover:bg-ink/5 shrink-0"
          @click="saveDir"
        >
          保存
        </button>
      </div>
      <p class="text-[11px] text-gray-500">
        探测：{{ resolved || '未找到（请填写并保存路径）' }}
      </p>
    </div>

    <div class="flex flex-wrap items-center gap-4 text-[12px]">
      <span class="text-gray-400">低阈值</span>
      <a-input-number v-model="low" :min="5" :max="95" size="small" />
      <span class="text-gray-400">高阈值</span>
      <a-input-number v-model="high" :min="10" :max="100" size="small" />
    </div>

    <div class="flex flex-wrap gap-3">
      <button
        :disabled="loading"
        class="text-xs font-semibold text-white bg-purple-700 disabled:opacity-50 px-4 py-2 rounded-lg"
        @click="launch(true)"
      >
        启用阈值（管理员）
      </button>
      <button
        :disabled="loading"
        class="text-xs font-semibold border border-ink/10 hover:bg-ink/5 px-4 py-2 rounded-lg"
        @click="launch(false)"
      >
        恢复默认充电
      </button>
    </div>
  </div>
</template>
