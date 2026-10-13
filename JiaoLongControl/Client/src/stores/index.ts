import { defineStore } from 'pinia'
import HOME from '@/assets/icon/HOME.png'
import CPU from '@/assets/icon/CPU.png'
import Fan from '@/assets/icon/Fan.png'
import Keyboard from '@/assets/icon/Keyboard.png'
import Settings from '@/assets/icon/Settings.png'
import AMD from '@/assets/icon/AMD.png'
import GPU from '@/assets/icon/GPU.png'
import FanCurve from '@/assets/icon/FanCurve.png'
import OSD from '@/assets/icon/Monitoring.png'

export type NavGroup = 'daily' | 'advanced' | 'settings'

/** 侧栏项：route 名对应懒加载路由；group 决定日常用 / 进阶调校 / 底部设置 */
export const HomeCardType = [
  { title: '主页', icon: HOME, route: 'home', group: 'daily' as NavGroup },
  { title: '键盘', icon: Keyboard, route: 'keyboard', group: 'daily' as NavGroup },
  { title: 'OSD', icon: OSD, route: 'osd', group: 'daily' as NavGroup },
  { title: '设备信息', icon: OSD, route: 'device', group: 'advanced' as NavGroup },
  { title: '中央处理器', icon: CPU, route: 'cpu', group: 'advanced' as NavGroup },
  { title: '图形处理器', icon: GPU, route: 'gpu', group: 'advanced' as NavGroup },
  { title: 'Ryzen SMU', icon: AMD, route: 'ryzen-smu', group: 'advanced' as NavGroup },
  { title: '风扇', icon: Fan, route: 'fan', group: 'advanced' as NavGroup },
  {
    title: '软件风扇曲线',
    icon: FanCurve,
    route: 'fan-curve',
    group: 'advanced' as NavGroup,
  },
  { title: '设置', icon: Settings, route: 'settings', group: 'settings' as NavGroup },
].map((item, index) => ({
  ...item,
  num: index + 1,
}))

const PAGE_STORAGE_KEY = 'jl-ui-page'
const ROUTE_STORAGE_KEY = 'jl-ui-route'

/** 旧「常规设置」入口并入设置页 */
const ROUTE_ALIASES: Record<string, string> = {
  general: 'settings',
}

export function resolveInitialRoute(): string {
  try {
    const savedRoute = localStorage.getItem(ROUTE_STORAGE_KEY)
    if (savedRoute) {
      const mapped = ROUTE_ALIASES[savedRoute] ?? savedRoute
      if (HomeCardType.some((p) => p.route === mapped)) {
        return mapped
      }
    }
    const savedNum = Number(localStorage.getItem(PAGE_STORAGE_KEY))
    if (Number.isInteger(savedNum) && savedNum >= 1 && savedNum <= HomeCardType.length) {
      return HomeCardType[savedNum - 1]!.route
    }
  } catch {
    /* localStorage 不可用 */
  }
  return 'home'
}

const useStore = defineStore('store', {
  state: () => ({
    SwitchPages: HomeCardType.find((p) => p.route === resolveInitialRoute())?.num ?? 1,
  }),
  getters: {
    currentRoute: (state) =>
      HomeCardType.find((p) => p.num === state.SwitchPages)?.route ?? 'home',
  },
  actions: {
    /** 仅更新侧栏状态与持久化；实际跳转由调用方 router.push */
    setPage(page: number) {
      const item = HomeCardType.find((p) => p.num === page)
      if (!item) return
      this.SwitchPages = page
      try {
        localStorage.setItem(PAGE_STORAGE_KEY, String(page))
        localStorage.setItem(ROUTE_STORAGE_KEY, item.route)
      } catch {
        /* 忽略持久化失败 */
      }
    },
    syncFromRoute(routeName: string | symbol | null | undefined) {
      if (typeof routeName !== 'string') return
      const mapped = ROUTE_ALIASES[routeName] ?? routeName
      const item = HomeCardType.find((p) => p.route === mapped)
      if (!item || item.num === this.SwitchPages) return
      this.SwitchPages = item.num
      try {
        localStorage.setItem(PAGE_STORAGE_KEY, String(item.num))
        localStorage.setItem(ROUTE_STORAGE_KEY, item.route)
      } catch {
        /* ignore */
      }
    },
  },
})

export default useStore
