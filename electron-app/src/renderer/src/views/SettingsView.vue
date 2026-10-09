<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useAppStore } from '@/stores/app'
import { backend } from '@/api/backend'
import type { EngineSettings, UpdateInfo } from '@shared/types'

const app = useAppStore()
const themes = [
  { v: 'light', label: '浅色', icon: 'sun' },
  { v: 'dark', label: '深色', icon: 'moon' },
  { v: 'system', label: '跟随系统', icon: 'globe' }
] as const

const settings = ref<EngineSettings | null>(null)

onMounted(async () => {
  try {
    settings.value = await backend.getSettings()
  } catch {
    // backend not reachable (e.g. browser preview)
  }
})

async function patch(p: Partial<EngineSettings>): Promise<void> {
  settings.value = await backend.setSettings(p)
}

const update = ref<UpdateInfo | null>(null)
const updateError = ref<string | null>(null)
const updateChecked = ref(false)

onMounted(async () => {
  try {
    update.value = await backend.checkUpdate()
  } catch (e) {
    update.value = null
    updateError.value = e instanceof Error ? e.message : String(e)
  } finally {
    updateChecked.value = true
  }
})

// Prefer the backend's reported version; fall back to the build-time injected package.json version.
const version = computed(() => update.value?.current || __APP_VERSION__)

const updateText = computed<string | null>(() => {
  if (!updateChecked.value) return null
  if (updateError.value) return `无法检查更新：${updateError.value}`
  const u = update.value
  if (!u) return '无法检查更新：未知错误'
  switch (u.status) {
    case 'upToDate':
      return `已是最新版本 (v${u.current})`
    case 'updateAvailable':
      return `发现新版本 ${u.tag ?? u.latest ?? ''}，点击查看`
    case 'notConfigured':
      return '未配置更新源'
    case 'failed':
    default:
      return `无法检查更新：${u.error ?? '未知错误'}`
  }
})
</script>

<template>
  <div class="page">
    <h1 class="page-title">设置</h1>

    <div class="card group">
      <div class="g-title">外观</div>
      <div class="setting">
        <div>
          <div class="s-name">主题</div>
          <div class="muted small">选择浅色 / 深色，或跟随系统</div>
        </div>
        <div class="seg">
          <button
            v-for="t in themes"
            :key="t.v"
            class="seg-btn"
            :class="{ active: app.theme === t.v }"
            @click="app.setTheme(t.v)"
          >
            <AppIcon :name="t.icon" :size="16" /> {{ t.label }}
          </button>
        </div>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">通用</div>
      <div class="setting">
        <div>
          <div class="s-name">最小化到托盘</div>
          <div class="muted small">点击关闭时隐藏到系统托盘而非退出</div>
        </div>
        <label class="switch" :class="{ disabled: !settings }">
          <input
            type="checkbox"
            :checked="settings?.minimizeToTray ?? false"
            :disabled="!settings"
            @change="patch({ minimizeToTray: ($event.target as HTMLInputElement).checked })"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">关于</div>
      <div class="about">
        <div class="logo"><AppIcon name="bolt" :size="22" /></div>
        <div>
          <div class="s-name">Game+ <span class="muted">v{{ version }}</span></div>
          <div class="muted small">Electron + Vue 3 · 游戏加速器</div>
          <div v-if="updateText" class="muted small">
            <a
              v-if="update?.status === 'updateAvailable'"
              :href="update.url ?? '#'"
              target="_blank"
              rel="noopener"
            >
              {{ updateText }}
            </a>
            <span v-else :class="{ warn: update?.status === 'failed' || !!updateError }">
              {{ updateText }}
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page {
  max-width: 760px;
  margin: 0 auto;
  padding: 26px 28px 40px;
}
.page-title {
  margin-bottom: 18px;
}
.group {
  padding: 8px 20px;
  margin-bottom: 16px;
}
.g-title {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 14px 0 8px;
}
.setting {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 0;
  border-top: 1px solid var(--border);
}
.s-name {
  font-weight: 600;
  font-size: 14px;
}
.small {
  font-size: 12px;
  margin-top: 2px;
}
.warn {
  color: var(--warn);
}
.seg {
  display: flex;
  gap: 4px;
  background: var(--surface-2);
  padding: 4px;
  border-radius: 10px;
}
.seg-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 34px;
  padding: 0 14px;
  border-radius: 8px;
  font-size: 13px;
  color: var(--text-2);
  transition: all 0.14s;
}
.seg-btn.active {
  background: var(--surface);
  color: var(--brand-600);
  box-shadow: var(--shadow-sm);
  font-weight: 600;
}
.switch input {
  display: none;
}
.switch.disabled {
  opacity: 0.5;
}
.track {
  display: block;
  width: 44px;
  height: 26px;
  border-radius: 13px;
  background: var(--surface-3);
  position: relative;
  transition: background 0.16s;
  cursor: pointer;
}
.thumb {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: #fff;
  box-shadow: var(--shadow-sm);
  transition: transform 0.16s;
}
.switch input:checked + .track {
  background: var(--brand);
}
.switch input:checked + .track .thumb {
  transform: translateX(18px);
}
.about {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px 0;
  border-top: 1px solid var(--border);
}
.about .logo {
  width: 44px;
  height: 44px;
  border-radius: 12px;
  background: var(--brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
</style>
