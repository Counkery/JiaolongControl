<script lang="ts" setup>
import {computed, onMounted, ref, watch} from 'vue'
import {Message} from '@arco-design/web-vue'
import SettingToggle from '@/components/common/SettingToggle.vue'
import {useConfigStore} from '@/stores/config'
import {Osd} from '@/utils/bridge'
import type {OsdPosition} from '@/types/config'

const configStore = useConfigStore()
if (!configStore.config) {
  await configStore.fetchConfig()
}
const osdData = computed(() => configStore.config?.Osd)

/** 总开关关闭时：内容与外观不可改，总控两项仍可操作 */
const contentDisabled = computed(() => !osdData.value?.Enabled)
const telemetryDisabled = computed(
  () => contentDisabled.value || !osdData.value?.ShowPerformanceMode,
)

const advancedOpen = ref(false)
const glossaryOpen = ref(false)

// 关闭「性能模式」时顺带关掉遥测，避免开着看不见
watch(
  () => osdData.value?.ShowPerformanceMode,
  (on) => {
    if (on === false && osdData.value?.ShowPerfTelemetry) {
      osdData.value.ShowPerfTelemetry = false
      void configStore.saveConfig()
    }
  },
)

// ===== 显示效果 =====
const positionOptions: Array<{ value: OsdPosition; label: string }> = [
  { value: 'TopCenter', label: '顶部居中' },
  { value: 'BottomCenter', label: '底部居中' },
  { value: 'Custom', label: '自定义' },
]
const currentPosition = computed(() => osdData.value?.Position ?? 'Custom')

function selectPosition(value: OsdPosition) {
  if (contentDisabled.value || !osdData.value || currentPosition.value === value) return
  osdData.value.Position = value
  configStore.debouncedSave()
}

function saveDebounced() {
  if (contentDisabled.value) return
  configStore.debouncedSave()
}

// ===== 自定义位置: 屏幕比例预览区拖拽 (与服务端相同的行程百分比映射) =====
const PILL_W = 52
const PILL_H = 14
const previewEl = ref<HTMLElement | null>(null)
let previewDragging = false

const pillStyle = computed(() => {
  const x = ((osdData.value?.CustomX ?? 50) / 100).toFixed(4)
  const y = ((osdData.value?.CustomY ?? 96) / 100).toFixed(4)
  return {
    left: `calc((100% - ${PILL_W}px) * ${x})`,
    top: `calc((100% - ${PILL_H}px) * ${y})`,
    width: `${PILL_W}px`,
    height: `${PILL_H}px`,
  }
})

function applyPreviewPosition(e: PointerEvent) {
  const el = previewEl.value
  if (!el || !osdData.value || contentDisabled.value) return
  const rect = el.getBoundingClientRect()
  const travelX = Math.max(1, rect.width - PILL_W)
  const travelY = Math.max(1, rect.height - PILL_H)
  const clampPct = (v: number) => Math.round(Math.min(100, Math.max(0, v)))
  osdData.value.CustomX = clampPct(((e.clientX - rect.left - PILL_W / 2) / travelX) * 100)
  osdData.value.CustomY = clampPct(((e.clientY - rect.top - PILL_H / 2) / travelY) * 100)
  configStore.debouncedSave()
}

function onPillPointerDown(e: PointerEvent) {
  if (!osdData.value || contentDisabled.value) return
  previewDragging = true
  if (currentPosition.value !== 'Custom') osdData.value.Position = 'Custom'
  ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
  configStore.debouncedSave()
}

function onPillPointerMove(e: PointerEvent) {
  if (previewDragging) applyPreviewPosition(e)
}

function onPillPointerUp() {
  previewDragging = false
}

// ===== 效果预览: 音量实时控制 + 各状态 OSD 预览 =====
const volume = ref(0)
const muted = ref(false)

async function refreshVolume() {
  try {
    const res = await Osd.GetVolume()
    if (res.Success && res.Data) {
      volume.value = res.Data.Volume
      muted.value = res.Data.Muted
    }
  } catch {
    /* 忽略 */
  }
}

async function onVolumeChange(val: number | [number, number]) {
  if (typeof val !== 'number') return
  try {
    const res = await Osd.SetVolume(val)
    if (!res.Success) Message.error(res.Message)
    await refreshVolume()
  } catch {
    Message.error('设置音量失败')
  }
}

async function onToggleMute() {
  try {
    const res = await Osd.SetMute(!muted.value)
    if (res.Success) {
      muted.value = !muted.value
      await refreshVolume()
    } else {
      Message.error(res.Message)
    }
  } catch {
    Message.error('设置静音失败')
  }
}

async function preview(kind: string, value = 0) {
  try {
    const res = await Osd.ShowTest(kind, value)
    if (!res.Success) Message.error(res.Message)
  } catch {
    Message.error('OSD 预览失败')
  }
}

const previewItems = [
  { kind: 'volume', label: '音量' },
  { kind: 'display', label: '亮度' },
  { kind: 'lock', label: '锁定键' },
  { kind: 'keyboard', label: '背光' },
  { kind: 'perf', label: '性能模式' },
  { kind: 'fnlock', label: 'Fn 锁' },
  { kind: 'touchpad', label: '触摸板' },
  { kind: 'media', label: '媒体' },
]

onMounted(() => {
  refreshVolume()
})
</script>

<template>
  <div v-if="osdData" class="jl-page">
    <div class="jl-page-inner">
      <div>
        <h1 class="jl-page-title">OSD 屏幕显示</h1>
        <p class="jl-page-desc">
          按键时在屏幕弹出置顶胶囊，调完后自动隐去；先开总开关，再选要显示的内容与外观
        </p>
      </div>

      <div class="flex flex-col lg:flex-row gap-6 pt-6">
        <!-- 左列: 总控 + 显示内容 -->
        <div class="flex-1 space-y-6">
          <section class="space-y-3">
            <h2
              class="jl-section-title"
            >
              总控
            </h2>
            <SettingToggle
              config-path="Osd.Enabled"
              description="开启后按音量、锁定等键弹出胶囊指示器，即时生效"
              title="启用 OSD"
            />
            <SettingToggle
              config-path="Osd.SuppressNativeVolumeOsd"
              description="隐藏 Windows 自带音量/亮度浮层，并由本程序接替厂家旧 Caps 等 OSD"
              title="屏蔽系统与厂家旧 OSD"
            />
          </section>

          <section class="space-y-3" :class="contentDisabled ? 'opacity-55' : ''">
            <h2
              class="jl-section-title jl-section-title--blue"
            >
              显示内容
            </h2>

            <p class="text-[11px] text-gray-500 px-0.5">系统按键</p>
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowVolume"
              description="音量增减/静音时显示进度条，停留期间可拖条或点喇叭静音"
              title="音量指示"
            />
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowScreenBrightness"
              description="亮度键变化时显示亮度条，停留期间可拖条调节"
              title="屏幕亮度"
            />
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowLockKeys"
              description="大写/数字/滚动锁定切换时显示短胶囊状态"
              title="锁定键状态"
            />

            <p class="text-[11px] text-gray-500 px-0.5 pt-2">本机 Fn / EC</p>
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowKeyboardBacklight"
              description="调整键盘背光时显示 0～3 档，停留期间可点选分段"
              title="键盘背光档位"
            />
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowPerformanceMode"
              description="切换性能模式时显示当前模式名称"
              title="性能模式切换"
            />
            <SettingToggle
              :disabled="telemetryDisabled"
              config-path="Osd.ShowPerfTelemetry"
              description="模式胶囊额外显示 CPU/GPU 温度与双风扇转速"
              title="模式附带温度/转速"
            />
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowFnLock"
              description="Fn 锁切换时显示开启/关闭"
              title="功能键锁定 (Fn Lock)"
            />
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowTouchpad"
              description="触摸板锁定切换时显示短胶囊状态"
              title="触摸板锁定"
            />

            <p class="text-[11px] text-gray-500 px-0.5 pt-2">媒体</p>
            <SettingToggle
              :disabled="contentDisabled"
              config-path="Osd.ShowMedia"
              description="开始播放或切歌时显示曲名与歌手，可控制上一曲/暂停/下一曲"
              title="媒体播放提示"
            />
          </section>
        </div>

        <!-- 右列: 外观 + 预览 + 进阶/说明 -->
        <div class="w-full lg:w-[380px] shrink-0 space-y-6 lg:sticky lg:top-0 lg:self-start">
          <div
            class="jl-panel space-y-5"
            :class="contentDisabled ? 'opacity-55 pointer-events-none' : ''"
          >
            <h3
              class="jl-section-title"
            >
              外观与位置
            </h3>

            <div>
              <div class="text-[11px] text-gray-400 mb-2">显示位置</div>
              <div
                class="flex items-center gap-1 p-1 rounded-xl bg-ink/[0.04] border border-ink/[0.06]"
              >
                <button
                  v-for="opt in positionOptions"
                  :key="opt.value"
                  type="button"
                  :class="
                    currentPosition === opt.value
                      ? 'bg-cyber-purple text-white'
                      : 'text-muted hover:text-ink'
                  "
                  class="flex-1 px-2 py-1.5 rounded-lg text-xs font-medium transition-all cursor-pointer"
                  @click="selectPosition(opt.value)"
                >
                  {{ opt.label }}
                </button>
              </div>

              <div
                class="relative w-full mt-2.5 rounded-lg border border-ink/[0.08] bg-ink/[0.03] overflow-hidden select-none"
                style="aspect-ratio: 16 / 9"
              >
                <div ref="previewEl" class="absolute inset-x-0 top-0 bottom-[10px]">
                  <div
                    :style="pillStyle"
                    class="absolute rounded-full bg-gradient-to-r from-purple-500 to-blue-400 border border-ink/10 cursor-grab active:cursor-grabbing touch-none"
                    @pointerdown="onPillPointerDown"
                    @pointermove="onPillPointerMove"
                    @pointerup="onPillPointerUp"
                  />
                </div>
                <div
                  class="absolute inset-x-0 bottom-0 h-[10px] bg-ink/[0.06] border-t border-ink/[0.06]"
                />
              </div>
              <p class="text-[10px] text-gray-600 leading-relaxed mt-1.5">
                拖动胶囊即存为自定义位置；选预设时忽略自定义坐标。当前：X
                {{ osdData.CustomX }}% · Y {{ osdData.CustomY }}%
              </p>
            </div>

            <div class="space-y-1.5">
              <div class="flex justify-between items-center text-[11px]">
                <span class="text-gray-400">出入场时长</span>
                <span class="text-ink font-mono">{{ osdData.AnimationMs }} ms</span>
              </div>
              <a-slider
                v-model="osdData.AnimationMs"
                :disabled="contentDisabled"
                :max="3000"
                :min="60"
                :step="20"
                class="slider-purple"
                @change="saveDebounced"
              />
            </div>

            <div class="space-y-1.5">
              <div class="flex justify-between items-center text-[11px]">
                <span class="text-gray-400">显示时长</span>
                <span class="text-ink font-mono">{{ osdData.DurationMs }} ms</span>
              </div>
              <a-slider
                v-model="osdData.DurationMs"
                :disabled="contentDisabled"
                :max="5000"
                :min="500"
                :step="100"
                class="slider-purple"
                @change="saveDebounced"
              />
            </div>

            <div class="space-y-1.5">
              <div class="flex justify-between items-center text-[11px]">
                <span class="text-gray-400">面板不透明度</span>
                <span class="text-ink font-mono">{{ osdData.Opacity }}%</span>
              </div>
              <a-slider
                v-model="osdData.Opacity"
                :disabled="contentDisabled"
                :max="100"
                :min="30"
                class="slider-blue"
                @change="saveDebounced"
              />
            </div>

            <div class="space-y-1.5">
              <div class="flex justify-between items-center text-[11px]">
                <span class="text-gray-400">界面缩放</span>
                <span class="text-ink font-mono">{{ osdData.Scale }}%</span>
              </div>
              <a-slider
                v-model="osdData.Scale"
                :disabled="contentDisabled"
                :max="200"
                :min="100"
                :step="5"
                class="slider-orange"
                @change="saveDebounced"
              />
            </div>
          </div>

          <!-- 效果预览：总开关关闭时仍可点预览（force）验证样式 -->
          <div
            class="jl-panel space-y-4"
          >
            <h3
              class="jl-section-title jl-section-title--blue"
            >
              效果预览
            </h3>

            <div class="space-y-1.5">
              <div class="flex justify-between items-center text-[11px]">
                <span class="text-gray-400">系统音量（拖动即弹出）</span>
                <span class="text-ink font-mono">{{ volume }}%{{ muted ? ' · 静音' : '' }}</span>
              </div>
              <div class="flex items-center gap-3">
                <a-slider
                  v-model="volume"
                  :max="100"
                  :min="0"
                  class="flex-1 slider-blue"
                  @change="onVolumeChange"
                />
                <a-button
                  class="!bg-blue-600/10 !text-blue-400 !border-blue-500/25 hover:!bg-blue-600 hover:!text-white rounded-md px-3 font-semibold transition"
                  size="small"
                  @click="onToggleMute"
                >
                  {{ muted ? '取消静音' : '静音' }}
                </a-button>
              </div>
            </div>

            <div class="grid grid-cols-2 gap-2">
              <a-button
                v-for="item in previewItems"
                :key="item.kind"
                class="!rounded-lg font-semibold !bg-ink/[0.04] !text-ink !border-ink/[0.08] hover:!bg-purple-600/20 hover:!text-purple-300 transition"
                @click="preview(item.kind)"
              >
                预览{{ item.label }}
              </a-button>
            </div>
          </div>

          <!-- 进阶 -->
          <div
            class="jl-panel jl-panel--flush overflow-hidden"
          >
            <button
              type="button"
              class="jl-collapse-trigger"
              @click="advancedOpen = !advancedOpen"
            >
              <span
                class="jl-section-title jl-section-title--rose"
              >
                进阶
              </span>
              <icon-down
                class="text-gray-500 transition-transform"
                :class="advancedOpen ? 'rotate-180' : ''"
              />
            </button>
            <div v-show="advancedOpen" class="px-5 pb-5 space-y-3 border-t border-ink/[0.05] pt-4">
              <div class="space-y-1.5">
                <div class="flex justify-between items-center text-[11px]">
                  <span class="text-gray-400">EC 状态轮询间隔</span>
                  <span class="text-ink font-mono">{{ osdData.PollIntervalMs }} ms</span>
                </div>
                <a-slider
                  v-model="osdData.PollIntervalMs"
                  :disabled="contentDisabled"
                  :max="5000"
                  :min="100"
                  :step="50"
                  class="slider-red"
                  @change="saveDebounced"
                />
                <p class="text-[10px] text-gray-600 leading-relaxed">
                  Fn 热键优先走事件通道即时检测；仅在事件不可用时用此间隔轮询兜底。
                </p>
              </div>
              <p class="text-[10px] text-gray-600 leading-relaxed">
                「屏蔽系统与厂家旧 OSD」会挂起厂家 OSD 进程以避免双弹；关闭本项且关掉「启用
                OSD」后恢复。Fn / EC 热键服务不受影响。
              </p>
            </div>
          </div>

          <!-- 名词解释 -->
          <div
            class="jl-panel jl-panel--flush overflow-hidden"
          >
            <button
              type="button"
              class="jl-collapse-trigger"
              @click="glossaryOpen = !glossaryOpen"
            >
              <span class="text-[13px] font-semibold text-gray-300">名词解释</span>
              <icon-down
                class="text-gray-500 transition-transform"
                :class="glossaryOpen ? 'rotate-180' : ''"
              />
            </button>
            <div
              v-show="glossaryOpen"
              class="px-5 pb-5 text-[11px] text-gray-500 leading-relaxed space-y-2 border-t border-ink/[0.05] pt-4"
            >
              <p>
                <strong>可交互胶囊</strong
                >：音量 / 背光 / 媒体停留时可操作，不抢焦点；锁定类保持点击穿透。
              </p>
              <p>
                <strong>键盘触发</strong>：音量与锁定经全局钩子监听；未接管音量时不拦截系统行为。
              </p>
              <p>
                <strong>自动缩放</strong>：按显示器 DPI 定位渲染，系统缩放变化后自动适配。
              </p>
              <p>
                <strong>主题自适应</strong>：胶囊跟随应用深色 / 浅色主题。
              </p>
            </div>
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
:deep(.slider-purple .arco-slider-bar) {
  background: linear-gradient(90deg, #2dd4bf 0%, var(--color-accent-purple) 100%) !important;
  height: 5px !important;
  border-radius: 99px;
}
:deep(.slider-purple .arco-slider-button) {
  border: 2px solid var(--color-accent-purple) !important;
  box-shadow: none !important;
}

:deep(.slider-blue .arco-slider-bar) {
  background: linear-gradient(90deg, #3b82f6 0%, #1d4ed8 100%) !important;
  height: 5px !important;
  border-radius: 99px;
}
:deep(.slider-blue .arco-slider-button) {
  border: 2px solid #3b82f6 !important;
  box-shadow: 0 0 8px rgba(59, 130, 246, 0.6) !important;
}

:deep(.slider-orange .arco-slider-bar) {
  background: linear-gradient(90deg, #ff7d00 0%, #ff5000 100%) !important;
  height: 5px !important;
  border-radius: 99px;
}
:deep(.slider-orange .arco-slider-button) {
  border: 2px solid #ff7d00 !important;
  box-shadow: 0 0 8px rgba(255, 125, 0, 0.6) !important;
}

:deep(.slider-red .arco-slider-bar) {
  background: linear-gradient(90deg, #f43f5e 0%, #e11d48 100%) !important;
  height: 5px !important;
  border-radius: 99px;
}
:deep(.slider-red .arco-slider-button) {
  border: 2px solid #f43f5e !important;
  box-shadow: 0 0 8px rgba(225, 29, 72, 0.6) !important;
}
</style>
