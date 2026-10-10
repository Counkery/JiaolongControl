import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'
import Components from 'unplugin-vue-components/vite'
import { ArcoResolver } from 'unplugin-vue-components/resolvers'

// https://vite.dev/config/
export default defineConfig(({ mode }) => ({
  plugins: [
    vue(),
    // devtools 面板仅开发模式启用, 避免注入生产构建
    ...(mode === 'development' ? [vueDevTools()] : []),
    tailwindcss(),
    // Arco Design 按需导入 (组件 + 图标), 替代 main.ts 的全量注册
    Components({
      dirs: [],
      resolvers: [ArcoResolver({ importStyle: 'css', resolveIcons: true })],
      dts: false,
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    outDir: '../../bin/publish/WebRoot',
    rollupOptions: {
      output: {
        // 按依赖族与页面路由拆包，降低首屏主包体积
        manualChunks(id) {
          if (!id.includes('node_modules')) return
          if (id.includes('echarts') || id.includes('vue-echarts') || id.includes('zrender')) {
            return 'vendor-echarts'
          }
          if (id.includes('@arco-design')) {
            return 'vendor-arco'
          }
          if (id.includes('pinia') || id.includes('vue-router') || id.includes('/vue/')) {
            return 'vendor-vue'
          }
        },
      },
    },
    chunkSizeWarningLimit: 600,
  },
}))
