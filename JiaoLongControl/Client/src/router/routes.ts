import { createRouter, createWebHashHistory, type RouteRecordRaw } from 'vue-router'
import { resolveInitialRoute } from '@/stores'

/**
 * 按页面路由懒加载：主包只保留壳层，各功能页独立 chunk。
 * 路径与侧栏导航、localStorage 持久化键对齐。
 */
const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('@/pages/Main.vue'),
    redirect: () => ({ name: resolveInitialRoute() }),
    children: [
      { path: 'home', name: 'home', component: () => import('@/pages/Home.vue'), meta: { title: '主页' } },
      {
        path: 'device',
        name: 'device',
        component: () => import('@/pages/DeviceInfo.vue'),
        meta: { title: '设备信息' },
      },
      { path: 'cpu', name: 'cpu', component: () => import('@/pages/CPU.vue'), meta: { title: '中央处理器' } },
      { path: 'gpu', name: 'gpu', component: () => import('@/pages/GPU.vue'), meta: { title: '图形处理器' } },
      {
        path: 'ryzen-smu',
        name: 'ryzen-smu',
        component: () => import('@/pages/RyzenSmu.vue'),
        meta: { title: 'Ryzen SMU' },
      },
      {
        path: 'fan-curve',
        name: 'fan-curve',
        component: () => import('@/pages/FanCurveEditor.vue'),
        meta: { title: '温度曲线' },
      },
      { path: 'fan', name: 'fan', component: () => import('@/pages/Fan.vue'), meta: { title: '风扇' } },
      {
        path: 'keyboard',
        name: 'keyboard',
        component: () => import('@/pages/KeyBoard.vue'),
        meta: { title: '键盘' },
      },
      { path: 'osd', name: 'osd', component: () => import('@/pages/Osd.vue'), meta: { title: 'OSD' } },
      {
        path: 'general',
        name: 'general',
        component: () => import('@/pages/General.vue'),
        meta: { title: '常规设置' },
      },
      {
        path: 'settings',
        name: 'settings',
        component: () => import('@/pages/Settings.vue'),
        meta: { title: '设置' },
      },
    ],
  },
]

const router = createRouter({
  history: createWebHashHistory(),
  routes,
})

export default router
