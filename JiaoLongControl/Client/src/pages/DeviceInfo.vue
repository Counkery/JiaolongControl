<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { Message } from '@arco-design/web-vue'
import { SystemInfo, type DeviceHardwareInfo } from '@/utils/bridge'

const loading = ref(true)
const info = ref<DeviceHardwareInfo | null>(null)
let timer: number | null = null

async function refresh(silent = false) {
  if (!silent) loading.value = true
  try {
    const res = await SystemInfo.GetDeviceHardwareInfo()
    if (res.Success && res.Data) {
      info.value = res.Data
    } else if (!silent) {
      Message.error(res.Message || '获取设备信息失败')
    }
  } catch {
    if (!silent) Message.error('获取设备信息失败')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void refresh()
  timer = window.setInterval(() => void refresh(true), 5000)
})

onUnmounted(() => {
  if (timer != null) clearInterval(timer)
})
</script>

<template>
  <div class="jl-page">
    <div class="jl-page-inner space-y-6">
      <div class="flex items-end justify-between gap-4">
        <div>
          <h1 class="jl-page-title">设备信息</h1>
          <p class="jl-page-desc">CPU / GPU / 内存 / 磁盘概览（对照第三方 DeviceInfo）</p>
        </div>
        <button
          class="text-xs font-semibold text-gray-300 border border-ink/10 hover:border-ink/20 px-4 py-2 rounded-lg"
          @click="refresh()"
        >
          刷新
        </button>
      </div>

      <div v-if="loading && !info" class="flex justify-center py-20">
        <a-spin dot />
      </div>

      <template v-else-if="info">
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <section class="jl-panel">
            <h2 class="text-[13px] font-semibold text-gray-300 mb-3">处理器</h2>
            <dl class="space-y-2 text-[12px]">
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500 shrink-0">型号</dt>
                <dd class="text-right text-ink">{{ info.Cpu.Name || '—' }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">核心 / 线程</dt>
                <dd class="font-mono">{{ info.Cpu.Cores }} / {{ info.Cpu.Threads }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">基频</dt>
                <dd class="font-mono">{{ info.Cpu.BaseFreqMhz }} MHz</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">厂商</dt>
                <dd>{{ info.Cpu.Manufacturer || '—' }}</dd>
              </div>
            </dl>
          </section>

          <section class="jl-panel">
            <h2 class="text-[13px] font-semibold text-gray-300 mb-3">图形处理器</h2>
            <div v-if="!info.Gpus.length" class="text-[12px] text-gray-500">未检测到显卡</div>
            <div v-for="(g, i) in info.Gpus" :key="i" class="mb-3 last:mb-0 space-y-2 text-[12px]">
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500 shrink-0">型号</dt>
                <dd class="text-right">{{ g.Name }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">显存</dt>
                <dd class="font-mono">{{ g.MemoryMb > 0 ? `${g.MemoryMb} MB` : '—' }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">驱动</dt>
                <dd class="text-right break-all">{{ g.DriverVersion || '—' }}</dd>
              </div>
            </div>
          </section>

          <section class="jl-panel">
            <h2 class="text-[13px] font-semibold text-gray-300 mb-3">内存</h2>
            <div class="mb-3">
              <div class="flex justify-between text-[12px] mb-1">
                <span class="text-gray-500">已用 {{ info.MemoryUsedGb }} / {{ info.TotalMemoryGb }} GB</span>
                <span class="font-mono text-purple-400">{{ info.MemoryUsedPercent }}%</span>
              </div>
              <a-progress
                :percent="Math.min(100, info.MemoryUsedPercent) / 100"
                :show-text="false"
                size="small"
              />
            </div>
            <div
              v-for="(m, i) in info.MemoryModules"
              :key="i"
              class="text-[12px] border-t border-ink/[0.04] py-2 first:border-0 first:pt-0"
            >
              <div class="flex justify-between">
                <span>{{ m.Manufacturer || '模块' }} {{ m.PartNumber }}</span>
                <span class="font-mono">{{ m.CapacityGb }} GB · {{ m.SpeedMhz }} MHz</span>
              </div>
            </div>
          </section>

          <section class="jl-panel">
            <h2 class="text-[13px] font-semibold text-gray-300 mb-3">系统</h2>
            <dl class="space-y-2 text-[12px]">
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">操作系统</dt>
                <dd class="text-right">{{ info.OsVersion || '—' }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-gray-500">版本</dt>
                <dd class="font-mono">{{ info.OsBuild || '—' }}</dd>
              </div>
            </dl>
          </section>
        </div>

        <section class="jl-panel">
          <h2 class="text-[13px] font-semibold text-gray-300 mb-4">物理磁盘</h2>
          <div v-if="!info.Disks.length" class="text-[12px] text-gray-500">未检测到磁盘</div>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div
              v-for="(d, i) in info.Disks"
              :key="i"
              class="border border-ink/[0.05] rounded-lg p-3 text-[12px] space-y-1"
            >
              <div class="font-medium text-ink">{{ d.Model }}</div>
              <div class="text-gray-500 flex justify-between">
                <span>{{ d.InterfaceType || d.MediaType || '—' }}</span>
                <span class="font-mono">{{ d.SizeGb }} GB</span>
              </div>
            </div>
          </div>
        </section>

        <section class="jl-panel">
          <h2 class="text-[13px] font-semibold text-gray-300 mb-4">逻辑卷</h2>
          <div class="space-y-4">
            <div v-for="(v, i) in info.Volumes" :key="i" class="text-[12px]">
              <div class="flex justify-between mb-1">
                <span class="font-medium"
                  >{{ v.Caption }} <span class="text-gray-500">({{ v.FileSystem }})</span></span
                >
                <span class="font-mono text-gray-400"
                  >{{ (v.SizeGb - v.FreeGb).toFixed(1) }} / {{ v.SizeGb }} GB ·
                  {{ v.UsedPercent }}%</span
                >
              </div>
              <a-progress
                :percent="Math.min(100, v.UsedPercent) / 100"
                :show-text="false"
                size="small"
                :status="v.UsedPercent > 90 ? 'danger' : 'normal'"
              />
            </div>
          </div>
        </section>
      </template>
    </div>
  </div>
</template>

<style scoped>
.no-scrollbar::-webkit-scrollbar {
  display: none;
}
.no-scrollbar {
  -ms-overflow-style: none;
  scrollbar-width: none;
}
</style>
