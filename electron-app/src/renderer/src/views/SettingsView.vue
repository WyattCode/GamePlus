<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import PrivacyCard from '@/components/PrivacyCard.vue'
import SplitRoutingCard from '@/components/SplitRoutingCard.vue'
import UpdateCard from '@/components/UpdateCard.vue'
import { useAppStore } from '@/stores/app'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { useLibraryStore } from '@/stores/library'
import { useOnboardingStore } from '@/stores/onboarding'
import { useToastStore, errorMessage } from '@/stores/toast'
import { backend } from '@/api/backend'
import { getPrefs, setPrefs } from '@/api/prefs'
import { DEFAULT_RELEASE_CHANNEL } from '@shared/release-channel'
import type { AutoStartState, EngineSettings } from '@shared/types'

const app = useAppStore()
const diagnostics = useDiagnosticsStore()
const library = useLibraryStore()
const onboarding = useOnboardingStore()
const toast = useToastStore()
const router = useRouter()
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

// Prefer the build-time injected package.json version.
const version = computed(() => __APP_VERSION__)

// --- 游戏规则库 ---
const rulesUrl = ref('')
const rulesSaving = ref(false)
const rulesChecking = ref(false)

const rulesSourceLabel = computed<string>(() => {
  switch (library.rulesStatus?.source) {
    case 'bundled':
      return '内置'
    case 'cached':
      return '本地缓存'
    case 'remote':
      return '在线更新'
    default:
      return '内置'
  }
})

const rulesLastCheck = computed<string | null>(() => {
  const t = library.rulesStatus?.lastCheck
  if (!t) return null
  const d = new Date(t)
  return Number.isNaN(d.getTime()) ? t : d.toLocaleString()
})

const rulesDefaultApplied = computed(
  () => rulesUrl.value.trim() === DEFAULT_RELEASE_CHANNEL.rulesUrl
)

function useDefaultRulesSource(): void {
  rulesUrl.value = DEFAULT_RELEASE_CHANNEL.rulesUrl
}

onMounted(async () => {
  try {
    settings.value = settings.value ?? (await backend.getSettings())
    rulesUrl.value = settings.value?.rulesUrl ?? ''
  } catch {
    // backend not reachable (e.g. browser preview)
  }
  try {
    await library.refreshRulesStatus()
  } catch {
    // ignore
  }
})

async function saveRulesUrl(): Promise<void> {
  rulesSaving.value = true
  try {
    settings.value = await backend.setSettings({ rulesUrl: rulesUrl.value.trim() })
    rulesUrl.value = settings.value.rulesUrl
    toast.info('已保存')
    await library.refreshRulesStatus()
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  } finally {
    rulesSaving.value = false
  }
}

async function checkRules(): Promise<void> {
  rulesChecking.value = true
  try {
    const result = await library.updateRules()
    if (result.status === 'failed') toast.error(result.message)
    else toast.info(result.message)
  } catch (e) {
    toast.error('检查失败：' + errorMessage(e))
  } finally {
    rulesChecking.value = false
  }
}

// --- 启动与托盘 ---
const autoStart = ref<AutoStartState>({ enabled: false, supported: true, reason: null })
const startHidden = ref(false)

onMounted(async () => {
  try {
    const p = await getPrefs()
    startHidden.value = p.startHidden
  } catch {
    // ignore
  }
  try {
    autoStart.value = await backend.getAutoStart()
  } catch {
    autoStart.value = { enabled: false, supported: false, reason: '无法连接后台服务' }
  }
})

async function toggleAutoStart(enabled: boolean): Promise<void> {
  try {
    autoStart.value = await backend.setAutoStart(enabled)
    if (!autoStart.value.supported && autoStart.value.reason) {
      toast.info(autoStart.value.reason)
    }
  } catch (e) {
    toast.error('设置失败：' + errorMessage(e))
    try {
      autoStart.value = await backend.getAutoStart()
    } catch {
      // ignore
    }
  }
}

async function toggleStartHidden(v: boolean): Promise<void> {
  startHidden.value = v
  try {
    await setPrefs({ startHidden: v })
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  }
}
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
      <div class="setting">
        <div>
          <div class="s-name">新手引导</div>
          <div class="muted small">重新查看首次使用的引导步骤</div>
        </div>
        <button class="btn diag-btn" @click="onboarding.reopen()">重新查看新手引导</button>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">启动与托盘</div>
      <div class="setting">
        <div>
          <div class="s-name">开机自动启动</div>
          <div class="muted small">
            通过 Windows 计划任务以管理员权限启动，登录时不再弹出权限确认。
          </div>
          <div v-if="!autoStart.supported && autoStart.reason" class="muted small">
            {{ autoStart.reason }}
          </div>
        </div>
        <label class="switch" :class="{ disabled: !autoStart.supported }">
          <input
            type="checkbox"
            :checked="autoStart.enabled"
            :disabled="!autoStart.supported"
            @change="toggleAutoStart(($event.target as HTMLInputElement).checked)"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>
      <div class="setting">
        <div>
          <div class="s-name">开机启动时最小化到托盘</div>
          <div class="muted small">开机启动后不显示主窗口，仅在托盘运行</div>
        </div>
        <label class="switch" :class="{ disabled: !autoStart.enabled }">
          <input
            type="checkbox"
            :checked="startHidden"
            :disabled="!autoStart.enabled"
            @change="toggleStartHidden(($event.target as HTMLInputElement).checked)"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>
      <div class="setting">
        <div>
          <div class="s-name">游戏启动时自动加速（总开关）</div>
          <div class="muted small">关闭后所有游戏的自动加速都不会生效</div>
        </div>
        <label class="switch" :class="{ disabled: !settings }">
          <input
            type="checkbox"
            :checked="settings?.autoAccelerate ?? false"
            :disabled="!settings"
            @change="patch({ autoAccelerate: ($event.target as HTMLInputElement).checked })"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>
    </div>

    <SplitRoutingCard />

    <div class="card group">
      <div class="g-title">游戏规则库</div>
      <div class="setting">
        <div>
          <div class="s-name">
            当前版本：v{{ library.rulesStatus?.version ?? '—' }}（{{ rulesSourceLabel }}）
          </div>
          <div class="muted small">{{ library.rulesStatus?.gameCount ?? 0 }} 个游戏</div>
          <div v-if="rulesLastCheck" class="muted small">上次检查：{{ rulesLastCheck }}</div>
          <div v-if="library.rulesStatus?.lastError" class="small danger">
            {{ library.rulesStatus.lastError }}
          </div>
          <div v-else-if="library.rulesStatus?.lastResult" class="muted small">
            {{ library.rulesStatus.lastResult }}
          </div>
        </div>
      </div>
      <div class="setting">
        <div class="rules-input">
          <div class="s-name">规则更新地址</div>
          <input
            v-model="rulesUrl"
            class="input"
            placeholder="https://…/game-rules.json，留空则不联网更新"
          />
        </div>
        <div class="rules-actions">
          <button
            class="btn diag-btn"
            :disabled="rulesDefaultApplied"
            @click="useDefaultRulesSource"
          >
            使用默认发布源
          </button>
          <button class="btn diag-btn" :disabled="rulesSaving" @click="saveRulesUrl">保存</button>
          <button
            class="btn btn-primary diag-btn"
            :disabled="!rulesUrl.trim() || rulesChecking"
            @click="checkRules"
          >
            {{ rulesChecking ? '检查中…' : '立即检查' }}
          </button>
        </div>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">诊断</div>
      <div class="setting">
        <div>
          <div class="s-name">网络诊断</div>
          <div class="muted small">检查驱动、权限、端口、线路和 DNS 等常见问题</div>
        </div>
        <button class="btn btn-primary diag-btn" @click="diagnostics.open()">开始诊断</button>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">高级</div>
      <div class="setting">
        <div>
          <div class="s-name">高级设置</div>
          <div class="muted small">TUN DNS、本地端口、选路策略等进阶选项</div>
        </div>
        <button class="btn diag-btn" @click="router.push('/settings/advanced')">打开</button>
      </div>
    </div>

    <PrivacyCard />

    <UpdateCard />

    <div class="card group">
      <div class="g-title">关于</div>
      <div class="about">
        <div class="logo"><AppIcon name="bolt" :size="22" /></div>
        <div>
          <div class="s-name">Game+ <span class="muted">v{{ version }}</span></div>
          <div class="muted small">Electron + Vue 3 · 游戏加速器</div>
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
.danger {
  color: var(--danger);
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
.rules-input {
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.rules-input .input {
  margin-top: 8px;
  width: 100%;
}
.rules-actions {
  display: flex;
  gap: 8px;
  flex: none;
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
