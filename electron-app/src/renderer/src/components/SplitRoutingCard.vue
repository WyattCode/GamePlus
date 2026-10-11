<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { backend, isValidProcessName } from '@/api/backend'
import { useAccelStore } from '@/stores/accel'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { ExtraApp, ExtraAppMode } from '@shared/types'

const accel = useAccelStore()
const toast = useToastStore()

const apps = ref<ExtraApp[]>([])
const loaded = ref(false)
const saving = ref(false)

const name = ref('')
const process = ref('')
const mode = ref<ExtraAppMode>('proxy')

onMounted(async () => {
  try {
    const s = await backend.getSettings()
    apps.value = s.extraApps ?? []
  } catch {
    // backend not reachable (e.g. browser preview)
  } finally {
    loaded.value = true
  }
})

// Per-app routing works in TUN mode (and process mode); system-proxy cannot.
const captureMode = computed(() => accel.status.captureMode)
const running = computed(
  () => accel.status.state === 'accelerating' || accel.status.state === 'connecting'
)
const modeWarning = computed<string | null>(() => {
  if (!running.value) return null
  if (captureMode.value === 'proxy') return '当前为系统代理模式，分流不生效'
  return null
})
const chipWarning = computed<string | null>(() => {
  return modeWarning.value
})

async function persist(next: ExtraApp[]): Promise<void> {
  saving.value = true
  try {
    const s = await backend.setSettings({ extraApps: next })
    apps.value = s.extraApps ?? []
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  } finally {
    saving.value = false
  }
}

async function add(): Promise<void> {
  const p = process.value.trim()
  if (!isValidProcessName(p)) {
    toast.error(`无效的进程名：${p}（应为 xxx.exe）`)
    return
  }
  if (apps.value.some((a) => a.process.toLowerCase() === p.toLowerCase())) {
    toast.error(`进程 ${p} 重复`)
    return
  }
  const n = name.value.trim() || p
  const next: ExtraApp[] = [...apps.value, { name: n, process: p, mode: mode.value, enabled: true }]
  await persist(next)
  name.value = ''
  process.value = ''
  mode.value = 'proxy'
}

async function remove(index: number): Promise<void> {
  const next = apps.value.filter((_, i) => i !== index)
  await persist(next)
}

async function toggleEnabled(index: number, enabled: boolean): Promise<void> {
  const next = apps.value.map((a, i) => (i === index ? { ...a, enabled } : a))
  await persist(next)
}

async function setMode(index: number, m: ExtraAppMode): Promise<void> {
  const next = apps.value.map((a, i) => (i === index ? { ...a, mode: m } : a))
  await persist(next)
}
</script>

<template>
  <div class="card group">
    <div class="g-title">分应用分流</div>
    <div class="setting">
      <div>
        <div class="s-name">额外应用</div>
        <div class="muted small">
          为语音、直播等应用单独指定走加速通道或保持直连。在 TUN 模式（以及进程模式）下按进程生效；系统代理模式不支持按应用分流。修改将在下次开始加速时生效。
        </div>
        <div v-if="chipWarning" class="chip warn-chip">{{ chipWarning }}</div>
      </div>
    </div>

    <div v-for="(a, i) in apps" :key="a.process + i" class="setting">
      <div class="app-row">
        <label class="switch" :class="{ disabled: saving }">
          <input
            type="checkbox"
            :checked="a.enabled"
            :disabled="saving"
            @change="toggleEnabled(i, ($event.target as HTMLInputElement).checked)"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
        <div class="app-info">
          <div class="s-name">{{ a.name }}</div>
          <div class="muted small mono">{{ a.process }}</div>
        </div>
      </div>
      <div class="app-actions">
        <div class="seg">
          <button
            class="seg-btn"
            :class="{ active: a.mode === 'proxy' }"
            :disabled="saving"
            @click="setMode(i, 'proxy')"
          >
            加速
          </button>
          <button
            class="seg-btn"
            :class="{ active: a.mode === 'bypass' }"
            :disabled="saving"
            @click="setMode(i, 'bypass')"
          >
            直连
          </button>
        </div>
        <button class="btn diag-btn" :disabled="saving" @click="remove(i)">
          <AppIcon name="trash" :size="14" /> 删除
        </button>
      </div>
    </div>

    <div class="setting">
      <div class="add-row">
        <input v-model="name" class="input" placeholder="名称（可选）" />
        <input v-model="process" class="input mono" placeholder="Discord.exe" />
        <select v-model="mode" class="input">
          <option value="proxy">加速</option>
          <option value="bypass">直连</option>
        </select>
      </div>
      <button class="btn btn-primary diag-btn" :disabled="saving || !loaded" @click="add">
        添加
      </button>
    </div>
  </div>
</template>

<style scoped>
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
.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.chip {
  display: inline-block;
  margin-top: 8px;
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 12px;
}
.warn-chip {
  background: var(--surface-2);
  color: var(--warn);
}
.app-row {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}
.app-info {
  min-width: 0;
}
.app-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: none;
}
.add-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.add-row .input {
  height: 36px;
}
.add-row .input:first-child {
  max-width: 160px;
}
.diag-btn {
  flex: none;
  height: 36px;
  padding: 0 18px;
  font-size: 13px;
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
</style>
