<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import AppIcon from './AppIcon.vue'
import { useAppStore } from '@/stores/app'
import { useAccelStore } from '@/stores/accel'
import { useI18n } from '@/i18n'

const app = useAppStore()
const accel = useAccelStore()
const { t } = useI18n()
const maximized = ref(false)

const inElectron = typeof window !== 'undefined' && !!window.gameplus

let unsubscribeMaximize: (() => void) | null = null

onMounted(async () => {
  if (!inElectron) return
  maximized.value = await window.gameplus.window.isMaximized()
  unsubscribeMaximize = window.gameplus.window.onMaximizeChange((v) => (maximized.value = v))
})

onUnmounted(() => {
  unsubscribeMaximize?.()
  unsubscribeMaximize = null
})

// `window` is not accessible from Vue template expressions, so the window controls must go through
// these setup functions (where the module scope's `window` is in scope).
function minimize(): void {
  window.gameplus.window.minimize()
}
function toggleMaximize(): void {
  window.gameplus.window.toggleMaximize()
}
function closeWindow(): void {
  window.gameplus.window.close()
}

const statusText = computed(() => {
  switch (accel.status.state) {
    case 'connecting':
      return t('status.connecting')
    case 'accelerating':
      return t('status.accelerating')
    case 'stopping':
      return t('status.stopping')
    default:
      return t('status.idle')
  }
})

function cycleTheme(): void {
  app.setTheme(app.effective() === 'dark' ? 'light' : 'dark')
}
</script>

<template>
  <header class="titlebar">
    <div class="brand">
      <div class="logo"><AppIcon name="bolt" :size="17" /></div>
      <span class="name">Game+</span>
      <span class="dot" :class="accel.status.state" />
      <span class="status">{{ statusText }}</span>
    </div>

    <div class="drag-spacer" />

    <div class="controls">
      <button class="tb-btn" :title="t('titlebar.theme.toggle')" @click="cycleTheme">
        <AppIcon :name="app.effective() === 'dark' ? 'sun' : 'moon'" :size="17" />
      </button>
      <template v-if="inElectron">
        <button class="tb-btn" :title="t('titlebar.window.minimize')" @click="minimize">
          <AppIcon name="minimize" :size="16" />
        </button>
        <button class="tb-btn" :title="t('titlebar.window.maximize')" @click="toggleMaximize">
          <AppIcon :name="maximized ? 'restore' : 'maximize'" :size="15" />
        </button>
        <button class="tb-btn close" :title="t('titlebar.window.close')" @click="closeWindow">
          <AppIcon name="close" :size="16" />
        </button>
      </template>
    </div>
  </header>
</template>

<style scoped>
.titlebar {
  height: var(--titlebar-h);
  display: flex;
  align-items: center;
  background: var(--titlebar);
  border-bottom: 1px solid var(--border);
  -webkit-app-region: drag;
  padding-left: 14px;
  flex-shrink: 0;
}
.brand {
  display: flex;
  align-items: center;
  gap: 9px;
}
.logo {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: var(--brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.name {
  font-weight: 700;
  font-size: 15px;
  letter-spacing: -0.01em;
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-left: 8px;
  background: var(--text-3);
}
.dot.accelerating {
  background: var(--ok);
}
.dot.connecting,
.dot.stopping {
  background: var(--warn);
}
.status {
  font-size: 12px;
  color: var(--text-2);
}
.drag-spacer {
  flex: 1;
}
.controls {
  display: flex;
  align-items: center;
  height: 100%;
  -webkit-app-region: no-drag;
}
.tb-btn {
  width: 44px;
  height: var(--titlebar-h);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-2);
  transition: background 0.12s, color 0.12s;
}
.tb-btn:hover {
  background: var(--surface-2);
  color: var(--text);
}
.tb-btn.close:hover {
  background: var(--danger);
  color: #fff;
}
</style>
