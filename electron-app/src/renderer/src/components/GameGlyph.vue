<script setup lang="ts">
// A game's icon = its unique monogram (white), on the parent tile's per-game colour. Distinct per game.
import { computed } from 'vue'
import { monogram } from '@/utils/gameIcon'

const props = withDefaults(defineProps<{ id: string; name?: string; size?: number }>(), { size: 30 })

const text = computed(() => monogram(props.id, props.name))
const fontSize = computed(() => {
  const n = text.value.length
  const scale = n <= 1 ? 1 : n === 2 ? 0.82 : 0.6
  return Math.round(props.size * scale)
})
</script>

<template>
  <span class="mono" :style="{ fontSize: fontSize + 'px' }">{{ text }}</span>
</template>

<style scoped>
.mono {
  font-weight: 800;
  letter-spacing: -0.02em;
  color: #fff;
  line-height: 1;
  font-family: 'Segoe UI', system-ui, sans-serif;
  user-select: none;
}
</style>
