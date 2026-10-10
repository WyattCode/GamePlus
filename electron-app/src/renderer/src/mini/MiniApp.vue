<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { TrayAction, TrayInfo } from '@shared/types'
import { useI18n } from '@/i18n'
import { formatMiniLatency, type MiniLatencyKind } from './miniFormat'

const { t } = useI18n()

const info = ref<TrayInfo>({ state: 'idle', gameName: null, latency: -1 })
const history = ref<number[]>([])

const stateLabel = computed(() => {
  switch (info.value.state) {
    case 'accelerating':
      return t('mini.status.accelerating')
    case 'connecting':
      return t('mini.status.connecting')
    case 'stopping':
      return t('mini.status.stopping')
    default:
      return t('mini.status.idle')
  }
})

const stateColor = computed(() => {
  switch (info.value.state) {
    case 'accelerating':
      return 'var(--ok)'
    case 'connecting':
    case 'stopping':
      return 'var(--warn)'
    default:
      return 'var(--text-3)'
  }
})

const active = computed(
  () =>
    info.value.state === 'accelerating' ||
    info.value.state === 'connecting' ||
    info.value.state === 'stopping'
)

const latency = computed(() =>
  formatMiniLatency(info.value.state, info.value.latency, t('mini.latency.idle'))
)

const latencyText = computed(() => latency.value.text)

const latencyColor = computed(() => {
  const kind: MiniLatencyKind = latency.value.kind
  switch (kind) {
    case 'ok':
      return 'var(--ok)'
    case 'warn':
      return 'var(--warn)'
    case 'danger':
      return 'var(--danger)'
    default:
      return 'var(--text-3)'
  }
})

const showSpark = computed(() => info.value.state === 'accelerating' && history.value.length >= 2)

const gameName = computed(() => info.value.gameName ?? t('mini.game.none'))

// Tiny inline sparkline of the last 30 latency samples (no chart library).
const sparkPoints = computed(() => {
  const data = history.value
  if (data.length < 2) return ''
  const w = 100
  const h = 24
  const max = Math.max(...data, 1)
  const min = Math.min(...data)
  const span = max - min || 1
  const step = w / (data.length - 1)
  return data
    .map((v, i) => {
      const x = i * step
      const y = h - ((v - min) / span) * h
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
})

let unsubscribe: (() => void) | null = null

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    window.gameplus.mini.hide()
  }
}

function toggle(): void {
  window.gameplus.mini.action('toggle')
}

function openMain(): void {
  window.gameplus.mini.openMain()
}

onMounted(() => {
  unsubscribe = window.gameplus.mini.onInfo((next: TrayInfo) => {
    info.value = next
    if (next.state === 'idle') {
      history.value = []
      return
    }
    if (next.state === 'accelerating' && typeof next.latency === 'number' && next.latency > 0) {
      const list = history.value.slice()
      list.push(next.latency)
      while (list.length > 30) list.shift()
      history.value = list
    }
  })
  window.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  unsubscribe?.()
  unsubscribe = null
  window.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <div class="mini">
    <header class="mini__head">
      <span class="mini__app">Game+</span>
      <span class="mini__pill" :style="{ color: stateColor, borderColor: stateColor }">
        {{ stateLabel }}
      </span>
    </header>

    <div class="mini__game" :title="gameName">{{ gameName }}</div>

    <div class="mini__latency">
      <span class="mini__latency-value" :style="{ color: latencyColor }">{{ latencyText }}</span>
      <svg
        v-if="showSpark"
        class="mini__spark"
        viewBox="0 0 100 24"
        preserveAspectRatio="none"
        aria-hidden="true"
      >
        <polyline
          v-if="sparkPoints"
          :points="sparkPoints"
          fill="none"
          :stroke="latencyColor"
          stroke-width="1.5"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
      </svg>
    </div>

    <div class="mini__actions">
      <button class="mini__btn mini__btn--primary" type="button" @click="toggle">
        {{ active ? t('mini.action.stop') : t('mini.action.start') }}
      </button>
      <button class="mini__btn" type="button" @click="openMain">{{ t('mini.action.openMain') }}</button>
    </div>
  </div>
</template>

<style scoped>
.mini {
  box-sizing: border-box;
  width: 300px;
  height: 190px;
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: var(--bg-1, #fff);
  color: var(--text-1, #1a1a1a);
  border-radius: 12px;
  overflow: hidden;
  font-size: 13px;
  user-select: none;
}

.mini__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.mini__app {
  font-weight: 600;
  font-size: 14px;
}

.mini__pill {
  font-size: 11px;
  line-height: 1;
  padding: 3px 8px;
  border: 1px solid currentColor;
  border-radius: 999px;
}

.mini__game {
  font-size: 12px;
  color: var(--text-2, #666);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.mini__latency {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 0;
}

.mini__latency-value {
  font-size: 30px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  line-height: 1;
}

.mini__spark {
  flex: 1;
  height: 24px;
  min-width: 0;
}

.mini__actions {
  display: flex;
  gap: 8px;
}

.mini__btn {
  flex: 1;
  height: 30px;
  border-radius: 8px;
  border: 1px solid var(--border, #d0d0d0);
  background: transparent;
  color: inherit;
  font-size: 12px;
  cursor: pointer;
}

.mini__btn:hover {
  background: var(--bg-2, rgba(0, 0, 0, 0.04));
}

.mini__btn--primary {
  border-color: transparent;
  background: var(--accent, #2f6fed);
  color: #fff;
}

.mini__btn--primary:hover {
  filter: brightness(1.05);
}
</style>
