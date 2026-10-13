<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <div
    class="jl-panel flex flex-col sm:flex-row sm:items-center justify-between gap-4 hover:border-ink/15"
  >
    <div class="space-y-1">
      <h3 class="text-[15px] font-semibold text-ink tracking-wide flex items-center gap-2">
        {{ title }}
      </h3>
      <p v-if="description" class="text-xs text-gray-400 font-normal leading-relaxed max-w-[550px]">
        {{ description }}
      </p>
      <div v-else class="text-xs text-gray-400 font-normal leading-relaxed max-w-[550px]">
        <slot name="description"></slot>
      </div>
    </div>
    <div class="flex items-center gap-3 shrink-0 self-end sm:self-center">
      <slot name="extra"></slot>
    </div>
  </div>
</template>
