<script lang="ts" setup>
import { computed, onMounted } from 'vue'
import { Message } from '@arco-design/web-vue'
import { type CommandResult, Osd } from '@/utils/bridge'
import { useLocksStore } from '@/stores/locks'

const props = withDefaults(
  defineProps<{
    /** compact：主页横排；card：设置页带标题与说明 */
    variant?: 'compact' | 'card'
  }>(),
  { variant: 'card' },
)

type LockKey = 'CapsLock' | 'NumLock' | 'FnLock' | 'TouchpadLock'
const locksStore = useLocksStore()
const locks = computed(() => locksStore.locks)

const lockItems: Array<{ key: LockKey; label: string; short: string; hint: string }> = [
  { key: 'CapsLock', label: '大写键锁定', short: 'Caps', hint: '模拟系统按键切换' },
  { key: 'NumLock', label: '数字键锁定', short: 'Num', hint: '模拟系统按键切换' },
  { key: 'FnLock', label: '功能键锁定', short: 'Fn', hint: '经 EC 写入切换' },
  { key: 'TouchpadLock', label: '触摸板锁定', short: '触控板', hint: '经 EC 写入切换' },
]

async function refreshLocks() {
  try {
    const res = await Osd.GetLockStates()
    if (res.Success && res.Data) {
      locksStore.apply(res.Data)
    }
  } catch {
    /* 忽略 */
  }
}

function lockStateText(key: LockKey) {
  const v = locks.value?.[key]
  if (v === null || v === undefined) return '未知'
  return v ? '已开启' : '已关闭'
}

async function toggleLock(key: LockKey) {
  const cur = locks.value?.[key]
  if (cur === null || cur === undefined) {
    Message.warning('该锁定状态读取失败, 无法切换')
    return
  }
  const setters: Record<LockKey, (v: boolean) => Promise<CommandResult<void>>> = {
    CapsLock: Osd.SetCapsLock,
    NumLock: Osd.SetNumLock,
    FnLock: Osd.SetFnLock,
    TouchpadLock: Osd.SetTouchpadLock,
  }
  try {
    const res = await setters[key](!cur)
    if (!res.Success) Message.error(res.Message)
  } catch {
    Message.error('设置失败')
  }
}

onMounted(() => {
  refreshLocks()
})
</script>

<template>
  <div v-if="props.variant === 'card'" class="jl-panel space-y-4">
    <h3 class="jl-section-title jl-section-title--emerald">锁定控制</h3>
    <div class="grid grid-cols-2 gap-2">
      <button
        v-for="item in lockItems"
        :key="item.key"
        type="button"
        :class="
          locks?.[item.key]
            ? '!bg-emerald-600/20 !text-emerald-400 !border-emerald-500/30 hover:!bg-emerald-600/30'
            : '!bg-ink/[0.04] !text-muted !border-ink/[0.08] hover:!text-ink'
        "
        class="px-3 py-2.5 rounded-lg text-xs font-semibold border transition-all cursor-pointer text-left"
        @click="toggleLock(item.key)"
      >
        <div>{{ item.label }}</div>
        <div class="text-[10px] opacity-70 font-normal mt-0.5">
          {{ item.hint }} · {{ lockStateText(item.key) }}
        </div>
      </button>
    </div>
    <p class="text-[10px] text-gray-600 leading-relaxed">
      点击切换并同步弹出 OSD；Fn / 触摸板经 EC 写入，机型不支持时显示未知
    </p>
  </div>

  <div v-else class="jl-panel space-y-3 min-w-0">
    <div class="flex items-center justify-between gap-3">
      <h2 class="text-[15px] font-medium text-ink/90">快捷锁定</h2>
      <span class="text-[11px] text-gray-500">点击切换 · 状态实时同步</span>
    </div>
    <div class="grid grid-cols-2 sm:grid-cols-4 gap-2">
      <button
        v-for="item in lockItems"
        :key="item.key"
        type="button"
        :class="
          locks?.[item.key]
            ? 'bg-emerald-600/20 text-emerald-400 border-emerald-500/30'
            : 'bg-ink/[0.04] text-muted border-ink/[0.08] hover:text-ink'
        "
        class="px-3 py-2.5 rounded-lg text-xs font-semibold border transition-all cursor-pointer text-center"
        @click="toggleLock(item.key)"
      >
        <div>{{ item.short }}</div>
        <div class="text-[10px] opacity-70 font-normal mt-0.5">{{ lockStateText(item.key) }}</div>
      </button>
    </div>
  </div>
</template>
