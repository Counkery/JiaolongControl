<script setup lang="ts">
import { computed, ref } from 'vue'
import SettingToggle from '@/components/common/SettingToggle.vue'
import QuickLocks from '@/components/common/QuickLocks.vue'
import PawnIODriverMode from './Settings/components/PawnIODriverMode.vue'
import ThemeSetting from './Settings/components/ThemeSetting.vue'
import BootAutoStart from './Settings/components/BootAutoStart.vue'
import BatteryThreshold from './Settings/components/BatteryThreshold.vue'
import LogoLight from './Settings/components/LogoLight.vue'
import GPUDirectConnection from './Settings/components/GPUDirectConnection.vue'
import { useConfigStore } from '@/stores/config'
import type { JiaoLongConfigType } from '@/types/config'

const configStore = useConfigStore()
const glossaryOpen = ref(false)

interface ToggleCard {
  title: string
  description: string
  configPath: string
  visibleWhen?: (config: JiaoLongConfigType) => boolean
}

const bootToggleCards: ToggleCard[] = [
  {
    title: '自启动软件风扇曲线（进阶）',
    description:
      '开机启动软件风扇曲线（Blding 轮询）。日常安静请优先用主页「安静日常」或【风扇】页 WMI 转速上限，勿与上限双开。',
    configPath: 'App.BootAdvancedFanControlSystem',
  },
  {
    title: 'CPU 参数自动应用',
    description: '启动时自动载入并应用【中央处理器】页保存的功耗、频率、温度墙等参数',
    configPath: 'App.BootAdvancedCPUSystem',
  },
  {
    title: 'GPU 参数自动应用',
    description: '启动时自动载入并应用【图形处理器】页保存的频率、电压曲线、功耗目标等参数',
    configPath: 'App.BootAdvancedGPUSystem',
  },
  {
    title: 'GPU 自动应用高级超频数值',
    description:
      '启用后，开机自动应用 GPU 时改用高级超频中的核心/显存频率偏移，代替常规频率锁定',
    configPath: 'App.BootGpuUseAdvancedOffsets',
    visibleWhen: (config) => config.App.BootAdvancedGPUSystem,
  },
  {
    title: 'RyzenSMU 降压自动应用',
    description: '启动时自动应用【Ryzen SMU】页保存的 Curve Optimizer 降压设定',
    configPath: 'App.BootSetRyzenSumCurveOptimizerAll',
  },
  {
    title: 'RyzenSMU 分核降压模式',
    description: '启用后，启动时逐核心应用分核 Curve Optimizer，代替全核偏移',
    configPath: 'App.BootSetRyzenSmuCurveOptimizerPerCore',
    visibleWhen: (config) => config.App.BootSetRyzenSumCurveOptimizerAll,
  },
  {
    title: '自启动键盘渐变',
    description: '开机及睡眠唤醒时自动开启键盘渐变（以启动时颜色为锚点）',
    configPath: 'App.BootKeyboardGradient',
  },
]

const advancedToggleCards: ToggleCard[] = [
  {
    title: '风扇曲线合并',
    description: '在【软件风扇曲线】页将所有风扇合并为一条曲线，统一调节转速',
    configPath: 'Fan.FanCurveMerge',
  },
  {
    title: 'CPU 分核心降压',
    description:
      '【中央处理器】页核心电压偏移改为分核：应用时逐核写入（数值在【Ryzen SMU】分核矩阵中设置）',
    configPath: 'App.CpuCurveOptimizerPerCore',
  },
]

const visibleBootCards = computed(() => {
  const config = configStore.config
  return bootToggleCards.filter((card) => !card.visibleWhen || !config || card.visibleWhen(config))
})
</script>

<template>
  <div class="p-6 h-full overflow-y-auto space-y-6 text-ink no-scrollbar pb-20">
    <div class="max-w-[1000px] mx-auto">
      <h1 class="text-2xl font-bold tracking-wider">设置</h1>
      <p class="text-[13px] text-gray-500 mt-1.5">
        设备快捷控制、外观主题、开机策略与进阶选项
      </p>
    </div>

    <div class="max-w-[1000px] mx-auto space-y-6">
      <section class="space-y-3">
        <h2
          class="text-xs font-black text-emerald-400 uppercase tracking-widest border-l-4 border-emerald-500 pl-2.5"
        >
          设备快捷
        </h2>
        <QuickLocks variant="card" />
        <LogoLight />
        <GPUDirectConnection />
      </section>

      <section class="space-y-3">
        <h2
          class="text-xs font-black text-purple-400 uppercase tracking-widest border-l-4 border-purple-600 pl-2.5"
        >
          外观与启动
        </h2>
        <ThemeSetting />
        <BootAutoStart />
      </section>

      <section class="space-y-3">
        <h2
          class="text-xs font-black text-blue-400 uppercase tracking-widest border-l-4 border-blue-500 pl-2.5"
        >
          开机自动应用
        </h2>
        <TransitionGroup class="grid grid-cols-1 gap-3" name="card-pop" tag="div">
          <SettingToggle
            v-for="card in visibleBootCards"
            :key="card.configPath"
            :title="card.title"
            :description="card.description"
            :config-path="card.configPath"
          />
        </TransitionGroup>
      </section>

      <section class="space-y-3">
        <h2
          class="text-xs font-black text-amber-400 uppercase tracking-widest border-l-4 border-amber-500 pl-2.5"
        >
          进阶选项
        </h2>
        <SettingToggle
          v-for="card in advancedToggleCards"
          :key="card.configPath"
          :title="card.title"
          :description="card.description"
          :config-path="card.configPath"
        />
        <PawnIODriverMode />
        <BatteryThreshold />
      </section>

      <div
        class="bg-panel/60 backdrop-blur-md border border-ink/[0.05] rounded-xl shadow-lg overflow-hidden"
      >
        <button
          type="button"
          class="w-full flex items-center justify-between px-5 py-3.5 text-left hover:bg-ink/[0.03] transition"
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
            <strong>大写/数字键锁定</strong>：系统级键盘状态，模拟按键切换，与指示灯同步。
          </p>
          <p>
            <strong>功能键锁定 (Fn Lock)</strong>：切换 F1–F12 与媒体键默认行为，经 EC 写入。
          </p>
          <p>
            <strong>触摸板锁定</strong>：启用或禁用触摸板，经 EC 写入。
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.card-pop-enter-active,
.card-pop-leave-active {
  transition: all 0.25s ease;
}
.card-pop-enter-from,
.card-pop-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
