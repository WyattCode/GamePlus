<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import LanShareCard from '@/components/LanShareCard.vue'
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
import { useI18n, type Locale } from '@/i18n'
import type { AutoStartState, EngineSettings, LaunchNodeMode } from '@shared/types'

const app = useAppStore()
const diagnostics = useDiagnosticsStore()
const library = useLibraryStore()
const onboarding = useOnboardingStore()
const toast = useToastStore()
const router = useRouter()
const { t, locale, setLocale } = useI18n()

const themes = computed(
  () =>
    [
      { v: 'light', label: t('settings.theme.light'), icon: 'sun' },
      { v: 'dark', label: t('settings.theme.dark'), icon: 'moon' },
      { v: 'system', label: t('settings.theme.system'), icon: 'globe' }
    ] as const
)

function onLocaleChange(e: Event): void {
  setLocale((e.target as HTMLSelectElement).value as Locale)
}

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
      return t('settings.rules.source.bundled')
    case 'cached':
      return t('settings.rules.source.cached')
    case 'remote':
      return t('settings.rules.source.remote')
    default:
      return t('settings.rules.source.bundled')
  }
})

const rulesLastCheck = computed<string | null>(() => {
  const time = library.rulesStatus?.lastCheck
  if (!time) return null
  const d = new Date(time)
  return Number.isNaN(d.getTime()) ? time : d.toLocaleString()
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
    toast.info(t('toast.saved'))
    await library.refreshRulesStatus()
  } catch (e) {
    toast.error(t('toast.saveFailed', { error: errorMessage(e) }))
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
    toast.error(t('toast.checkFailed', { error: errorMessage(e) }))
  } finally {
    rulesChecking.value = false
  }
}

// --- 启动与托盘 ---
const autoStart = ref<AutoStartState>({ enabled: false, supported: true, reason: null })
const startHidden = ref(false)
const launchNodeMode = ref<LaunchNodeMode>('keep')

onMounted(async () => {
  try {
    const p = await getPrefs()
    startHidden.value = p.startHidden
    launchNodeMode.value = p.launchNodeMode === 'smart' ? 'smart' : 'keep'
  } catch {
    // ignore
  }
  try {
    autoStart.value = await backend.getAutoStart()
  } catch {
    autoStart.value = { enabled: false, supported: false, reason: t('error.backendUnreachable') }
  }
})

async function toggleAutoStart(enabled: boolean): Promise<void> {
  try {
    autoStart.value = await backend.setAutoStart(enabled)
    if (!autoStart.value.supported && autoStart.value.reason) {
      toast.info(autoStart.value.reason)
    }
  } catch (e) {
    toast.error(t('toast.setFailed', { error: errorMessage(e) }))
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
    toast.error(t('toast.saveFailed', { error: errorMessage(e) }))
  }
}

async function onLaunchNodeModeChange(e: Event): Promise<void> {
  const raw = (e.target as HTMLSelectElement).value
  if (raw !== 'keep' && raw !== 'smart') return
  const v = raw
  launchNodeMode.value = v
  try {
    await setPrefs({ launchNodeMode: v })
  } catch (e) {
    toast.error(t('toast.saveFailed', { error: errorMessage(e) }))
  }
}
</script>

<template>
  <div class="page">
    <h1 class="page-title">{{ t('settings.title') }}</h1>

    <div class="card group">
      <div class="g-title">{{ t('settings.language.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.language.name') }}</div>
          <div class="muted small">{{ t('settings.language.desc') }}</div>
        </div>
        <select class="input" :value="locale" @change="onLocaleChange">
          <option value="zh-CN">{{ t('settings.language.zhCN') }}</option>
          <option value="en">{{ t('settings.language.en') }}</option>
        </select>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">{{ t('settings.appearance.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.theme.name') }}</div>
          <div class="muted small">{{ t('settings.theme.desc') }}</div>
        </div>
        <div class="seg">
          <button
            v-for="th in themes"
            :key="th.v"
            class="seg-btn"
            :class="{ active: app.theme === th.v }"
            @click="app.setTheme(th.v)"
          >
            <AppIcon :name="th.icon" :size="16" /> {{ th.label }}
          </button>
        </div>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">{{ t('settings.general.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.minimizeToTray.name') }}</div>
          <div class="muted small">{{ t('settings.minimizeToTray.desc') }}</div>
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
          <div class="s-name">{{ t('settings.onboarding.name') }}</div>
          <div class="muted small">{{ t('settings.onboarding.desc') }}</div>
        </div>
        <button class="btn diag-btn" @click="onboarding.reopen()">
          {{ t('settings.onboarding.button') }}
        </button>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">{{ t('settings.startup.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.autoStart.name') }}</div>
          <div class="muted small">{{ t('settings.autoStart.desc') }}</div>
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
          <div class="s-name">{{ t('settings.startHidden.name') }}</div>
          <div class="muted small">{{ t('settings.startHidden.desc') }}</div>
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
          <div class="s-name">{{ t('settings.autoAccelerate.name') }}</div>
          <div class="muted small">{{ t('settings.autoAccelerate.desc') }}</div>
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
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.launchNode.name') }}</div>
          <div class="muted small">{{ t('settings.launchNode.desc') }}</div>
        </div>
        <select class="input" :value="launchNodeMode" @change="onLaunchNodeModeChange">
          <option value="keep">{{ t('settings.launchNode.keep') }}</option>
          <option value="smart">{{ t('settings.launchNode.smart') }}</option>
        </select>
      </div>
    </div>

    <SplitRoutingCard />

    <LanShareCard />

    <div class="card group">
      <div class="g-title">{{ t('settings.rules.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">
            {{
              t('settings.rules.version', {
                version: library.rulesStatus?.version ?? '—',
                source: rulesSourceLabel
              })
            }}
          </div>
          <div class="muted small">
            {{ t('settings.rules.gameCount', { count: library.rulesStatus?.gameCount ?? 0 }) }}
          </div>
          <div v-if="rulesLastCheck" class="muted small">
            {{ t('settings.rules.lastCheck', { time: rulesLastCheck }) }}
          </div>
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
          <div class="s-name">{{ t('settings.rules.url.name') }}</div>
          <input
            v-model="rulesUrl"
            class="input"
            :placeholder="t('settings.rules.url.placeholder')"
          />
        </div>
        <div class="rules-actions">
          <button
            class="btn diag-btn"
            :disabled="rulesDefaultApplied"
            @click="useDefaultRulesSource"
          >
            {{ t('settings.rules.useDefault') }}
          </button>
          <button class="btn diag-btn" :disabled="rulesSaving" @click="saveRulesUrl">
            {{ t('settings.rules.save') }}
          </button>
          <button
            class="btn btn-primary diag-btn"
            :disabled="!rulesUrl.trim() || rulesChecking"
            @click="checkRules"
          >
            {{ rulesChecking ? t('settings.rules.checking') : t('settings.rules.check') }}
          </button>
        </div>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">{{ t('settings.diagnostics.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.diagnostics.name') }}</div>
          <div class="muted small">{{ t('settings.diagnostics.desc') }}</div>
        </div>
        <button class="btn btn-primary diag-btn" @click="diagnostics.open()">
          {{ t('settings.diagnostics.button') }}
        </button>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">{{ t('settings.advanced.title') }}</div>
      <div class="setting">
        <div>
          <div class="s-name">{{ t('settings.advanced.name') }}</div>
          <div class="muted small">{{ t('settings.advanced.desc') }}</div>
        </div>
        <button class="btn diag-btn" @click="router.push('/settings/advanced')">
          {{ t('settings.advanced.button') }}
        </button>
      </div>
    </div>

    <PrivacyCard />

    <UpdateCard />

    <div class="card group">
      <div class="g-title">{{ t('settings.about.title') }}</div>
      <div class="about">
        <div class="logo"><AppIcon name="bolt" :size="22" /></div>
        <div>
          <div class="s-name">Game+ <span class="muted">v{{ version }}</span></div>
          <div class="muted small">{{ t('settings.about.desc') }}</div>
        </div>
      </div>
      <div class="about-legal muted small">
        <p>{{ t('settings.about.license') }}</p>
        <p>{{ t('settings.about.notAffiliated') }}</p>
        <p>{{ t('settings.about.geolite') }}</p>
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
.about-legal {
  padding: 0 0 12px;
  line-height: 1.6;
}
.about-legal p {
  margin: 4px 0;
}
</style>
