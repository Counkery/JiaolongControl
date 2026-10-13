<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import RightSide from '@/components/layout/RightSide.vue'
import TitleBar from '@/components/layout/TitleBar.vue'
import useStore, { HomeCardType, resolveInitialRoute } from '@/stores'

const store = useStore()
const router = useRouter()

function onClickMenuItem(key: number) {
  const item = HomeCardType.find((p) => p.num === key)
  if (!item) return
  store.setPage(key)
  if (router.currentRoute.value.name !== item.route) {
    void router.push({ name: item.route })
  }
}

onMounted(() => {
  const name = resolveInitialRoute()
  if (router.currentRoute.value.name !== name) {
    void router.replace({ name })
  }
})

const settingsItem = computed(
  () =>
    HomeCardType.find((item) => item.group === 'settings') ??
    HomeCardType[HomeCardType.length - 1]!,
)
const dailyNavItems = computed(() => HomeCardType.filter((item) => item.group === 'daily'))
const advancedNavItems = computed(() => HomeCardType.filter((item) => item.group === 'advanced'))

function navButtonClass(num: number) {
  return [
    'w-full flex items-center gap-4 px-5 py-3.5 rounded-[5px] transition-all duration-300',
    store.SwitchPages === num
      ? 'nav-item-active'
      : 'hover:bg-ink/[0.03] text-gray-400 hover:text-gray-1000',
  ]
}
</script>

<template>
  <div class="flex flex-col h-screen text-ink overflow-hidden select-none">
    <TitleBar class="z-50 bg-transparent" />

    <div class="flex flex-1 overflow-hidden">
      <aside class="w-[240px] flex flex-col shadow-10 mt-6">
        <div class="flex-1 h-10 px-4 flex flex-col justify-between overflow-y-auto no-scrollbar">
          <nav class="space-y-4">
            <div class="space-y-1.5">
              <p class="px-5 text-[10px] font-semibold tracking-widest uppercase text-gray-600">
                日常用
              </p>
              <button
                v-for="item in dailyNavItems"
                :key="item.num"
                :class="navButtonClass(item.num)"
                @click="onClickMenuItem(Number(item.num))"
              >
                <div
                  class="w-5 h-5 flex items-center justify-center relative"
                  :class="
                    store.SwitchPages === item.num
                      ? 'drop-shadow-[0_0_6px_rgba(59,130,246,0.9)]'
                      : ''
                  "
                >
                  <span
                    v-if="store.SwitchPages === item.num"
                    class="absolute w-5 h-5 bg-blue-500/40 rounded-full blur-[8px] animate-pulse pointer-events-none"
                  ></span>
                  <span
                    class="icon-mask icon-silhouette relative z-10 w-full h-full transition-all duration-300"
                    :class="
                      store.SwitchPages === item.num
                        ? 'opacity-100'
                        : 'opacity-75 group-hover:opacity-100'
                    "
                    :style="{ WebkitMaskImage: `url(${item.icon})`, maskImage: `url(${item.icon})` }"
                  ></span>
                </div>
                <span class="font-medium text-[13px] tracking-wide">{{ item.title }}</span>
              </button>
            </div>

            <div class="space-y-1.5">
              <p class="px-5 text-[10px] font-semibold tracking-widest uppercase text-gray-600">
                进阶调校
              </p>
              <button
                v-for="item in advancedNavItems"
                :key="item.num"
                :class="navButtonClass(item.num)"
                @click="onClickMenuItem(Number(item.num))"
              >
                <div
                  class="w-5 h-5 flex items-center justify-center relative"
                  :class="
                    store.SwitchPages === item.num
                      ? 'drop-shadow-[0_0_6px_rgba(59,130,246,0.9)]'
                      : ''
                  "
                >
                  <span
                    v-if="store.SwitchPages === item.num"
                    class="absolute w-5 h-5 bg-blue-500/40 rounded-full blur-[8px] animate-pulse pointer-events-none"
                  ></span>
                  <span
                    class="icon-mask icon-silhouette relative z-10 w-full h-full transition-all duration-300"
                    :class="
                      store.SwitchPages === item.num
                        ? 'opacity-100'
                        : 'opacity-75 group-hover:opacity-100'
                    "
                    :style="{ WebkitMaskImage: `url(${item.icon})`, maskImage: `url(${item.icon})` }"
                  ></span>
                </div>
                <span class="font-medium text-[13px] tracking-wide">{{ item.title }}</span>
              </button>
            </div>
          </nav>

          <div class="pb-6">
            <button
              :class="[
                'w-full flex items-center justify-between px-5 py-4 transition-colors',
                store.SwitchPages === settingsItem.num
                  ? 'text-ink'
                  : 'text-gray-400 hover:text-ink',
              ]"
              @click="onClickMenuItem(settingsItem.num)"
            >
              <div class="flex items-center gap-4">
                <div class="w-5 h-5 flex items-center justify-center relative">
                  <span
                    v-if="store.SwitchPages === settingsItem.num"
                    class="absolute w-5 h-5 bg-blue-500/40 rounded-full blur-[8px] animate-pulse pointer-events-none"
                  ></span>
                  <icon-settings
                    :class="[
                      'text-lg relative z-10 transition-all duration-300',
                      store.SwitchPages === settingsItem.num
                        ? 'text-blue-400 drop-shadow-[0_0_6px_rgba(59,130,246,0.9)]'
                        : '',
                    ]"
                  />
                </div>
                <span class="font-medium text-[13px] tracking-wide">设置</span>
              </div>
              <icon-right />
            </button>
          </div>
        </div>
      </aside>

      <main class="flex-1 relative overflow-hidden">
        <div
          class="absolute top-[-20%] right-[-10%] w-[800px] h-[800px] bg-[radial-gradient(circle_at_center,rgba(138,43,226,0.08),transparent_60%)] pointer-events-none"
        ></div>
        <div
          class="absolute bottom-[-20%] left-[-10%] w-[600px] h-[600px] bg-[radial-gradient(circle_at_center,rgba(59,130,246,0.05),transparent_60%)] pointer-events-none"
        ></div>

        <div class="relative h-full z-10">
          <RightSide />
        </div>
      </main>
    </div>
  </div>
</template>
