<script async setup lang="ts">
import { onMounted, ref } from 'vue'
import { LogoLight, ResultState } from '@/utils/bridge.ts'
import { Message } from '@arco-design/web-vue'
import SettingCardComponent from '@/components/common/SettingCardComponent.vue'

const loading = ref(false)
const logolight = ref(false)

onMounted(async () => {
  // 后端约定: LogoLightController.Get() 的 Success 位即灯的实际开关状态
  logolight.value = (await LogoLight.Get()).Success
})

async function handleLogoLightChange(value: string | number | boolean) {
  const next = Boolean(value)
  loading.value = true
  try {
    const res = await LogoLight.Set(next ? ResultState.ON : ResultState.OFF)
    if (res.Success) {
      logolight.value = next
      Message.success('应用成功')
    } else {
      // 失败不回写: :model-value 是单向绑定, 开关会自动停在原状态
      Message.error(res.Message || '应用失败')
    }
  } catch {
    Message.error('应用失败，请检查日志')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <setting-card-component
    title="Logo A壳徽标灯"
    description="控制笔记本电脑 A 面（顶盖）个性化 Logo 氛围指示灯的开启与关闭"
  >
    <template #extra>
      <a-switch
        :model-value="logolight"
        :loading="loading"
        @change="handleLogoLightChange"
      >
        <template #checked-icon>
          <icon-check />
        </template>
        <template #unchecked-icon>
          <icon-close />
        </template>
      </a-switch>
    </template>
  </setting-card-component>
</template>

<style scoped></style>
