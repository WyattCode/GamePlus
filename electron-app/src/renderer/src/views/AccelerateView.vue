<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AccelHistoryCard from '@/components/AccelHistoryCard.vue'
import AppIcon from '@/components/AppIcon.vue'
import EffectChart from '@/components/EffectChart.vue'
import GameGlyph from '@/components/GameGlyph.vue'
import LaunchTargetDialog from '@/components/LaunchTargetDialog.vue'
import { gameColor } from '@/utils/gameIcon'
import { backend } from '@/api/backend'
import { adviseBeforeAccel, adviseDuringAccel, type EffectAdvice } from '@/utils/effectAdvice'
import { createShortcut } from '@/utils/launchApi'
import { useAccelStore } from '@/stores/accel'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { useLibraryStore } from '@/stores/library'
import { errorMessage, useToastStore } from '@/stores/toast'
import { launchDialogGameId, useAccelerateAndLaunch } from '@/composables/useAccelerateAndLaunch'
import { fmtDelay, fmtDuration, fmtLatency, fmtRate } from '@/utils/format'
import type { BaselineInfo, CaptureMode, CaptureModeKind } from '@shared/types'

const accel = useAccelStore()
const diagnostics = useDiagnosticsStore()
const library = useLibraryStore()
const toast = useToastStore()
const router = useRouter()

const error = ref<string | null>(null)
const busy = ref(false)
const candidatesOpen = ref(true)

// If the selected node id disappears from the list (e.g. after a subscription refresh), clear the
// selection and fall back to smart node rather than silently showing a blank select.
watch(
  () => library.servers,
  () => accel.reconcileSelection()
)

const running = computed(
  () => accel.status.state === 'accelerating' || accel.status.state === 'connecting'
)
const buttonText = computed(() => {
  switch (accel.status.state) {
    case 'connecting':
      return '连接中…'
    case 'accelerating':
      return '停止加速'
    case 'stopping':
      return '停止中…'
    default:
      return '一键加速'
  }
})

const captureHint = computed(() => {
  if (accel.status.state !== 'accelerating') return null
  switch (accel.status.captureMode) {
    case 'process':
      return { text: '进程模式', warn: false }
    case 'tun':
      return { text: 'TUN 模式', warn: false }
    case 'proxy':
      return { text: '系统代理模式（仅加速支持代理的程序）', warn: true }
    default:
      return null
  }
})

const metrics = computed(() => [
  { icon: 'timer', color: 'var(--ok)', label: '延迟', value: fmtLatency(accel.status.latency) },
  { icon: 'warning', color: 'var(--warn)', label: '丢包', value: `${accel.status.loss}%` },
  { icon: 'pulse', color: 'var(--info)', label: '抖动', value: `${accel.status.jitter} ms` },
  { icon: 'clock', color: '#9b5de5', label: '时长', value: fmtDuration(accel.status.durationSec) }
])

// The effect card is visible while accelerating, or when a finished report is still available.
const effect = computed(() => accel.effect)
const effectVisible = computed(() => {
  if (!effect.value) return false
  if (accel.status.state === 'accelerating') return true
  return effect.value.series.length > 0 || effect.value.baseline != null
})
const effectIsLast = computed(() => accel.status.state !== 'accelerating')
const effectColor = computed(() => {
  const pct = effect.value?.latencyPct
  if (pct == null) return 'var(--text-3)'
  if (pct >= 5) return 'var(--ok)'
  if (pct <= -5) return 'var(--warn)'
  return 'var(--text-3)'
})
const effectCompare = computed(() => {
  const e = effect.value
  if (!e || !e.baseline) return null
  const b = e.baseline
  const c = e.current
  const base = `直连（参考） ${b.latency >= 0 ? b.latency + ' ms' : '—'} · 丢包 ${b.loss}%`
  if (!c) return base
  return `${base} → 加速 ${c.latency >= 0 ? c.latency + ' ms' : '—'} · 丢包 ${c.loss}%`
})

// Local date-time of the cached baseline, e.g. "2025/06/01 14:32".
const baselineMeasuredText = computed(() => {
  const iso = effect.value?.baselineMeasuredAt
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return null
  const pad = (n: number): string => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
})

const remeasuring = ref(false)

async function remeasure(): Promise<void> {
  const gameId = accel.selectedGame?.id
  if (!gameId || remeasuring.value) return
  if (running.value) {
    if (!window.confirm('重新测量需要先暂停加速约 3 秒，是否继续？')) return
  }
  remeasuring.value = true
  error.value = null
  try {
    await accel.remeasure()
    if (running.value) {
      // Restart so the new baseline is measured immediately (same flow as switching nodes).
      await accel.stop()
      await accel.start()
    } else {
      toast.info('下次加速时将重新测量直连基线')
    }
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    remeasuring.value = false
  }
}

// --- direct baseline hint (网络良好，可不加速) ---

// Stored direct baseline for the selected game, fetched while idle; null while loading/unknown.
const baselineInfo = ref<BaselineInfo | null>(null)
// Game id for which the user dismissed the hint; switching games shows it again.
const baselineDismissedFor = ref<string | null>(null)
// Guards against out-of-order getBaseline responses when switching games quickly.
let baselineSeq = 0

watch(
  [() => accel.selectedGame?.id, running],
  async ([gameId, isRunning]) => {
    baselineInfo.value = null
    if (!gameId || isRunning) return
    const seq = ++baselineSeq
    try {
      const info = await backend.getBaseline(gameId)
      if (seq === baselineSeq) baselineInfo.value = info
    } catch {
      // Advisory only: silently ignore fetch failures.
    }
  },
  { immediate: true }
)

const beforeAdvice = computed<EffectAdvice | null>(() => {
  if (running.value || !baselineInfo.value) return null
  if (baselineDismissedFor.value === accel.selectedGame?.id) return null
  return adviseBeforeAccel(baselineInfo.value.baseline)
})

function dismissBaselineHint(): void {
  baselineDismissedFor.value = accel.selectedGame?.id ?? null
}

// Live "加速效果不明显" hint while accelerating, from the effect report's before/after snapshots.
const duringAdvice = computed<EffectAdvice | null>(() => {
  if (accel.status.state !== 'accelerating') return null
  const e = effect.value
  if (!e) return null
  return adviseDuringAccel(e.baseline, e.current)
})

// --- smart routing (P2) ---

const recommended = computed(() => {
  const r = accel.ranking
  if (!r || !r.recommendedId) return null
  return r.candidates.find((c) => c.id === r.recommendedId) ?? null
})

const topCandidates = computed(() => (accel.ranking?.candidates ?? []).slice(0, 6))

// The node the UI currently considers "in use": the live accel node, else the manual selection, else
// the smart recommendation.
const currentNodeId = computed<string | null>(() => {
  if (running.value) return accel.status.nodeId
  if (!accel.smartNode) return accel.selectedNodeId
  return accel.ranking?.recommendedId ?? null
})

function barColor(score: number, delay: number): string {
  if (delay < 0 || score < 0) return 'var(--text-3)'
  if (score <= 80) return 'var(--ok)'
  if (score <= 150) return 'var(--warn)'
  return 'var(--danger)'
}

function barWidth(score: number, delay: number): string {
  if (delay < 0 || score < 0) return '0%'
  const pct = Math.min(100, Math.max(4, (score / 200) * 100))
  return `${pct}%`
}

async function refreshRanking(ping = false): Promise<void> {
  error.value = null
  try {
    await accel.refreshRanking(ping)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function switchNode(id: string): Promise<void> {
  error.value = null
  try {
    await accel.switchNode(id)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

// Fetch the ranking on mount, when the selected game changes, and when the node pool changes (no ping).
watch(
  () => accel.selectedGame?.id,
  () => {
    void refreshRanking(false)
  },
  { immediate: true }
)
watch(
  () => library.servers.length,
  () => {
    void refreshRanking(false)
  }
)

async function toggle(): Promise<void> {
  if (busy.value) return
  error.value = null
  busy.value = true
  try {
    await accel.toggle()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

// --- auto-accelerate (P6b) ---

// The selected game object is a snapshot; look it up in the library so the toggle reflects the
// latest autoAccelerate flag after setGameAuto replaces the game list.
const selectedGame = computed(() => {
  const id = accel.selectedGame?.id
  if (!id) return null
  return library.games.find((g) => g.id === id) ?? accel.selectedGame
})

const hasProcessRules = computed(
  () => (selectedGame.value?.proxyProcesses?.length ?? 0) > 0
)

async function toggleAuto(enabled: boolean): Promise<void> {
  const g = selectedGame.value
  if (!g) return
  try {
    await library.setGameAuto(g.id, enabled)
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

// --- 加速并启动 / launch target / desktop shortcut ---

const { accelerateAndLaunch } = useAccelerateAndLaunch()
const launchBusy = ref(false)

async function onAccelAndLaunch(): Promise<void> {
  if (launchBusy.value) return
  launchBusy.value = true
  try {
    await accelerateAndLaunch()
  } finally {
    launchBusy.value = false
  }
}

async function onCreateShortcut(): Promise<void> {
  const g = selectedGame.value
  if (!g) return
  const res = await createShortcut(g.id, g.name)
  if (res.ok) toast.info('已在桌面创建快捷方式')
  else toast.error(res.error ?? '创建快捷方式失败')
}

function openLaunchDialog(): void {
  if (!selectedGame.value) return
  launchDialogGameId.value = selectedGame.value.id
}

const dialogGame = computed(() => {
  const id = launchDialogGameId.value
  if (!id) return null
  return library.games.find((g) => g.id === id) ?? null
})
// --- per-game server choice (Q1) ---

const hasServerChoice = computed(() => selectedGame.value?.hasServerChoice === true)
const serverChoice = computed<'cn' | 'intl'>(() =>
  selectedGame.value?.serverChoice === 'cn' ? 'cn' : 'intl'
)

async function setServer(server: 'cn' | 'intl'): Promise<void> {
  const g = selectedGame.value
  if (!g || running.value || server === serverChoice.value) return
  error.value = null
  try {
    await library.setGameServer(g.id, server)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

// --- per-game download split (Steam CDN direct) ---

const hasDownloadDomains = computed(() => (selectedGame.value?.downloadDomains?.length ?? 0) > 0)
const downloadViaProxy = computed(() => selectedGame.value?.downloadViaProxy === true)

async function toggleDownloadProxy(enabled: boolean): Promise<void> {
  const g = selectedGame.value
  if (!g || running.value) return
  try {
    await library.setGameDownloadProxy(g.id, enabled)
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

// --- capture mode (per game) ---

const MODE_LABELS: Record<CaptureMode, string> = {
  auto: '自动（推荐）',
  process: '进程模式',
  tun: 'TUN 模式',
  proxy: '系统代理'
}

const MODE_UNAVAILABLE_HINT: Record<CaptureModeKind, string> = {
  process: '该游戏没有进程规则',
  tun: '该游戏没有 IP 规则',
  proxy: '该游戏没有域名规则'
}

// Global default capture mode, read from settings (best-effort; falls back to "auto").
const globalDefaultMode = ref<CaptureMode>('auto')

onMounted(async () => {
  try {
    const s = await backend.getSettings()
    const m = s.advanced.defaultCaptureMode
    if (m === 'auto' || m === 'process' || m === 'tun' || m === 'proxy') {
      globalDefaultMode.value = m
    }
  } catch {
    // backend not reachable (e.g. browser preview)
  }
})

const captureMode = computed<CaptureMode | null>(() => selectedGame.value?.captureMode ?? null)
const availableModes = computed<CaptureModeKind[]>(
  () => (selectedGame.value?.availableModes as CaptureModeKind[] | undefined) ?? []
)

// Per-game compatibility note from the backend (anti-cheat / recommended mode hints); null when none.
const gameCompat = computed(() => selectedGame.value?.compat ?? null)

const modeOptions = computed<
  { value: CaptureMode | 'default'; label: string; disabled: boolean; title: string; recommended: boolean }[]
>(() => {
  const avail = availableModes.value
  const rec = gameCompat.value?.recommendedMode ?? null
  const opts: { value: CaptureMode | 'default'; label: string; disabled: boolean; title: string; recommended: boolean }[] = [
    {
      value: 'default',
      label: `跟随默认 · ${MODE_LABELS[globalDefaultMode.value]}`,
      disabled: false,
      title: '',
      recommended: false
    }
  ]
  const kinds: CaptureMode[] = ['auto', 'process', 'tun', 'proxy']
  for (const k of kinds) {
    const isKind = k !== 'auto'
    const disabled = isKind && !avail.includes(k as CaptureModeKind)
    opts.push({
      value: k,
      label: MODE_LABELS[k],
      disabled,
      title: disabled ? MODE_UNAVAILABLE_HINT[k as CaptureModeKind] : '',
      // 'auto' is already labelled 自动（推荐）, so only badge an explicit non-auto recommendation.
      recommended: rec !== null && rec !== 'auto' && k === rec
    })
  }
  return opts
})

const currentModeValue = computed<CaptureMode | 'default'>(() => captureMode.value ?? 'default')

async function setCaptureMode(mode: CaptureMode | 'default'): Promise<void> {
  const g = selectedGame.value
  if (!g || running.value || busy.value || mode === currentModeValue.value) return
  error.value = null
  try {
    await library.setGameMode(g.id, mode)
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

// --- acceleration history (per-game) ---

// Bumped whenever a session finishes (accelerating/stopping → idle) so the history card reloads.
const historyRefreshKey = ref(0)
watch(
  () => accel.status.state,
  (state, prev) => {
    if (state === 'idle' && (prev === 'accelerating' || prev === 'stopping')) {
      historyRefreshKey.value++
    }
  }
)
</script>

<template>
  <div class="page">
    <h1 class="page-title">加速</h1>

    <!-- Empty state -->
    <div v-if="!accel.selectedGame" class="card empty">
      <div class="empty-ico"><AppIcon name="gamepad" :size="34" /></div>
      <p class="muted">还没有选择游戏</p>
      <button class="btn btn-primary" @click="router.push('/games')">
        <AppIcon name="gamepad" :size="18" /> 前往游戏库
      </button>
    </div>

    <template v-else>
      <!-- Hero -->
      <div class="hero" :style="{ background: gameColor(accel.selectedGame.id) }">
        <div class="glyph">
          <GameGlyph :id="accel.selectedGame.id" :name="accel.selectedGame.name" :size="46" />
        </div>
        <div class="hero-main">
          <div class="hero-name">{{ accel.selectedGame.name }}</div>
          <div class="hero-region">{{ accel.selectedGame.regionText }}</div>
          <div class="hero-pill">
            <AppIcon name="globe" :size="13" />
            加速域名 · {{ accel.selectedGame.ruleCount }}
          </div>
        </div>
        <div class="hero-status">
          <span class="s-dot" :class="accel.status.state" />
          {{ running ? '加速中' : '待命' }}
        </div>
      </div>

      <!-- Start failure banner -->
      <div v-if="error" class="card error-banner">
        <AppIcon name="warning" :size="16" />
        <span class="err-msg">{{ error }}</span>
        <button class="err-diag" @click="diagnostics.open(error)">诊断</button>
        <button class="err-close" title="关闭" @click="error = null">
          <AppIcon name="close" :size="14" />
        </button>
      </div>

      <!-- Node + action -->
      <div class="card panel">
        <!-- Per-game server choice (国服 / 国际服) -->
        <div v-if="hasServerChoice" class="server-row">
          <div class="server-label">服务器</div>
          <div class="seg" :class="{ disabled: running }" :title="running ? '停止加速后可切换' : ''">
            <button
              type="button"
              class="seg-btn"
              :class="{ active: serverChoice === 'intl' }"
              :disabled="running"
              @click="setServer('intl')"
            >
              国际服
            </button>
            <button
              type="button"
              class="seg-btn"
              :class="{ active: serverChoice === 'cn' }"
              :disabled="running"
              @click="setServer('cn')"
            >
              国服
            </button>
          </div>
          <span class="muted small server-hint">按所选服务器挑选线路</span>
        </div>

        <!-- Per-game capture mode -->
        <div class="server-row">
          <div class="server-label">加速模式</div>
          <div class="seg" :class="{ disabled: running }" :title="running ? '停止加速后可切换' : ''">
            <button
              v-for="opt in modeOptions"
              :key="opt.value"
              type="button"
              class="seg-btn"
              :class="{ active: currentModeValue === opt.value }"
              :disabled="running || busy || opt.disabled"
              :title="opt.title"
              @click="setCaptureMode(opt.value)"
            >
              {{ opt.label }}
              <span v-if="opt.recommended" class="mode-rec">推荐</span>
            </button>
          </div>
          <span class="muted small server-hint">手动指定后失败不会自动降级</span>
        </div>

        <!-- Per-game compatibility note (anti-cheat / recommended mode hints) -->
        <div v-if="gameCompat" class="compat-note">
          <div class="compat-head">
            <AppIcon name="info" :size="13" />
            <span class="compat-label">兼容性提示</span>
            <span class="compat-tag" :class="{ unverified: !gameCompat.verified }">
              {{ gameCompat.verified ? '已验证' : '未验证' }}
            </span>
          </div>
          <div class="compat-text">{{ gameCompat.note }}</div>
        </div>

        <!-- Per-game download split: CDN downloads go DIRECT unless the toggle is on -->
        <div v-if="hasDownloadDomains" class="server-row">
          <div class="server-label">游戏下载</div>
          <label class="switch" :class="{ disabled: running }" title="下载也走代理">
            <input
              type="checkbox"
              :checked="downloadViaProxy"
              :disabled="running"
              @change="toggleDownloadProxy(($event.target as HTMLInputElement).checked)"
            />
            <span class="track"><span class="thumb" /></span>
          </label>
          <span class="dl-name">下载也走代理</span>
          <span class="muted small server-hint dl-hint">
            默认：商店/社区/登录走代理，游戏下载（CDN）直连，避免消耗节点流量；开启后下载也经过节点（需重新加速生效）。
          </span>
        </div>

        <div class="row">
          <div>
            <div class="row-title">智能选路</div>
            <div class="muted small">自动挑选延迟最低的线路</div>
          </div>
          <label class="switch">
            <input v-model="accel.smartNode" type="checkbox" />
            <span class="track"><span class="thumb" /></span>
          </label>
        </div>

        <select
          v-if="!accel.smartNode"
          :value="accel.selectedNodeId ?? ''"
          class="input select"
          @change="accel.setSelectedNodeId(($event.target as HTMLSelectElement).value || null)"
        >
          <option :value="''" disabled>选择线路…</option>
          <option v-for="s in library.servers" :key="s.id" :value="s.id">
            [{{ s.type }}] {{ s.remark }} · {{ fmtDelay(s.delay) }}
          </option>
        </select>

        <!-- Smart routing: recommendation (only when smart routing is on) -->
        <div v-if="accel.smartNode && recommended" class="rec">
          <div class="rec-line">
            <AppIcon name="bolt" :size="14" />
            <span class="rec-label">推荐：</span>
            <span class="rec-name">{{ recommended.remark }}</span>
          </div>
          <div class="muted small rec-reason">{{ accel.ranking?.reason }}</div>
        </div>

        <!-- Ranked candidates: shown in both smart and manual mode so the user can compare/switch -->
        <div v-if="topCandidates.length" class="cand">
          <div class="cand-head">
            <button type="button" class="cand-toggle" @click="candidatesOpen = !candidatesOpen">
              <AppIcon name="chevron" :size="14" :class="{ open: candidatesOpen }" />
              <span class="cand-title">候选线路</span>
              <span class="muted small">{{ topCandidates.length }}</span>
            </button>
            <span class="cand-spacer" />
            <button
              type="button"
              class="cand-refresh"
              :disabled="accel.rankLoading"
              @click="refreshRanking(true)"
            >
              <AppIcon name="refresh" :size="13" />
              {{ accel.rankLoading ? '测速中…' : '重新测速' }}
            </button>
          </div>

          <div v-if="candidatesOpen" class="cand-list">
            <div v-for="c in topCandidates" :key="c.id" class="cand-row">
              <span class="chip">{{ c.regionName ?? '未知' }}</span>
              <div class="cand-main">
                <div class="cand-remark" :title="c.remark">{{ c.remark }}</div>
                <div class="cand-bar">
                  <span
                    class="cand-bar-fill"
                    :style="{ width: barWidth(c.score, c.delay), background: barColor(c.score, c.delay) }"
                  />
                </div>
                <div class="muted small">{{ c.note }}</div>
              </div>
              <div class="cand-right">
                <div class="cand-delay" :style="{ color: barColor(c.score, c.delay) }">
                  {{ fmtDelay(c.delay) }}
                </div>
                <span v-if="c.id === currentNodeId" class="badge">当前</span>
                <button v-else class="btn small-btn" @click="switchNode(c.id)">切换</button>
              </div>
            </div>
          </div>
        </div>

        <div v-if="accel.nodeUnavailable" class="capture-hint warn">
          <AppIcon name="info" :size="14" />
          之前选择的线路已不存在，已切换为智能选路
        </div>

        <!-- Auto-accelerate for the selected game -->
        <div class="row auto-row">
          <div>
            <div class="row-title">游戏启动时自动加速</div>
            <div class="muted small">
              {{
                hasProcessRules
                  ? '检测到游戏进程后自动开始，游戏退出后自动停止'
                  : '该游戏没有进程规则，无法自动加速'
              }}
            </div>
          </div>
          <label class="switch" :class="{ disabled: !hasProcessRules }">
            <input
              type="checkbox"
              :checked="selectedGame?.autoAccelerate ?? false"
              :disabled="!hasProcessRules"
              @change="toggleAuto(($event.target as HTMLInputElement).checked)"
            />
            <span class="track"><span class="thumb" /></span>
          </label>
        </div>

        <!-- Direct baseline hint: 网络良好，可不加速 (advisory only, dismissible per game) -->
        <div v-if="beforeAdvice" class="advice-banner">
          <AppIcon name="check" :size="14" />
          <div class="advice-text">
            <div class="advice-title">{{ beforeAdvice.title }}</div>
            <div class="advice-detail">{{ beforeAdvice.detail }}</div>
          </div>
          <button class="advice-close" title="关闭" @click="dismissBaselineHint">
            <AppIcon name="close" :size="12" />
          </button>
        </div>

        <div class="action-row">
          <button class="action" :class="{ stop: running }" :disabled="busy" @click="toggle">
            <AppIcon name="bolt" :size="22" />
            {{ buttonText }}
          </button>
          <button
            class="action secondary"
            :disabled="busy || launchBusy"
            title="加速并启动游戏"
            @click="onAccelAndLaunch"
          >
            <AppIcon name="bolt" :size="18" />
            加速并启动
          </button>
        </div>

        <div class="launch-row">
          <button type="button" class="link-btn" @click="openLaunchDialog">启动方式</button>
          <span class="launch-sep">·</span>
          <button type="button" class="link-btn" @click="onCreateShortcut">创建桌面快捷方式</button>
        </div>

        <div v-if="captureHint" class="capture-hint" :class="{ warn: captureHint.warn }">
          <AppIcon name="info" :size="14" />
          {{ captureHint.text }}
        </div>

        <!-- LAN share chip: shown while the console proxy share is active -->
        <div v-if="accel.status.lanShareEndpoint" class="capture-hint lan-chip">
          <AppIcon name="globe" :size="14" />
          局域网共享中 · {{ accel.status.lanShareEndpoint }}
        </div>
      </div>

      <!-- Metrics -->
      <div class="metrics">
        <div v-for="m in metrics" :key="m.label" class="card metric">
          <div class="metric-head">
            <AppIcon :name="m.icon" :size="15" :style="{ color: m.color }" />
            <span class="muted small">{{ m.label }}</span>
          </div>
          <div class="metric-val">{{ m.value }}</div>
        </div>
      </div>

      <!-- Acceleration effect -->
      <div v-if="effectVisible && effect" class="card effect">
        <div class="effect-head">
          <div class="effect-title">
            加速效果（参考）
            <span v-if="effectIsLast" class="muted small">上次加速</span>
          </div>
          <div class="effect-summary" :style="{ color: effectColor }">{{ effect.summary }}</div>
        </div>
        <div class="muted small effect-note">
          基于应用内对游戏域名的 TLS 握手延迟测得，仅供参考，不等同于游戏内显示的延迟。
        </div>
        <div v-if="effectCompare" class="effect-compare muted small">{{ effectCompare }}</div>
        <div v-if="duringAdvice" class="effect-advice">
          <AppIcon name="warning" :size="14" />
          <div class="advice-text">
            <div class="advice-title">{{ duringAdvice.title }}</div>
            <div class="advice-detail">{{ duringAdvice.detail }}</div>
          </div>
        </div>
        <div v-if="effect.baselineFromCache && baselineMeasuredText" class="muted small effect-baseline">
          直连基线测于 {{ baselineMeasuredText }}
        </div>
        <div v-if="effect.target" class="effect-target muted small">目标 {{ effect.target }}</div>
        <EffectChart :series="effect.series" :baseline="effect.baseline?.latency ?? null" />
        <div class="effect-actions">
          <button class="btn ghost-btn" :disabled="remeasuring" @click="remeasure">
            <AppIcon name="refresh" :size="13" />
            {{ remeasuring ? '处理中…' : '重新测量' }}
          </button>
        </div>
      </div>

      <!-- Per-game acceleration history -->
      <AccelHistoryCard
        :game-id="accel.selectedGame.id"
        :game-name="accel.selectedGame.name"
        :refresh-key="historyRefreshKey"
      />

      <!-- Traffic -->
      <div class="traffic">
        <div class="card tr">
          <div class="tr-ico down"><AppIcon name="download" :size="18" /></div>
          <div>
            <div class="muted small">下载</div>
            <div class="tr-val">{{ fmtRate(accel.status.downloadBps) }}</div>
          </div>
        </div>
        <div class="card tr">
          <div class="tr-ico up"><AppIcon name="upload" :size="18" /></div>
          <div>
            <div class="muted small">上传</div>
            <div class="tr-val">{{ fmtRate(accel.status.uploadBps) }}</div>
          </div>
        </div>
      </div>

      <!-- Launch-target dialog (opened by 启动方式 / 加速并启动 without a target) -->
      <LaunchTargetDialog
        :open="launchDialogGameId !== null"
        :game="dialogGame"
        @close="launchDialogGameId = null"
      />
    </template>
  </div>
</template>

<style scoped>
.page {
  max-width: 880px;
  margin: 0 auto;
  padding: 26px 28px 40px;
}
.page-title {
  margin-bottom: 18px;
}
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
  padding: 56px;
}
.empty-ico {
  width: 72px;
  height: 72px;
  border-radius: 20px;
  background: var(--surface-2);
  color: var(--text-3);
  display: flex;
  align-items: center;
  justify-content: center;
}

.hero {
  border-radius: var(--radius-lg);
  padding: 24px 26px;
  display: flex;
  align-items: center;
  gap: 20px;
  color: #fff;
  min-height: 132px;
  box-shadow: var(--shadow-md);
}
.glyph {
  width: 84px;
  height: 84px;
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.16);
  border: 1px solid rgba(255, 255, 255, 0.25);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 46px;
}
.hero-main {
  flex: 1;
}
.hero-name {
  font-size: 23px;
  font-weight: 700;
}
.hero-region {
  font-size: 13px;
  opacity: 0.85;
  margin-top: 3px;
}
.hero-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-top: 10px;
  padding: 5px 10px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.16);
  font-size: 12px;
}
.hero-status {
  align-self: flex-start;
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 6px 12px;
  border-radius: 12px;
  background: rgba(0, 0, 0, 0.2);
  font-size: 12px;
}
.s-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.6);
}
.s-dot.accelerating {
  background: #7dffb0;
}

.error-banner {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 16px;
  padding: 12px 16px;
  border-color: var(--danger);
  color: var(--danger);
  font-size: 13px;
}
.err-msg {
  flex: 1;
  word-break: break-word;
}
.err-diag {
  flex: none;
  padding: 4px 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--danger);
  color: var(--danger);
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
}
.err-diag:hover {
  background: rgba(220, 53, 69, 0.1);
}
.err-close {
  color: var(--danger);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4px;
}

.panel {
  margin-top: 16px;
  padding: 22px;
}
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.row-title {
  font-weight: 600;
  font-size: 14px;
}
.small {
  font-size: 12px;
}
.select {
  width: 100%;
  margin-top: 14px;
  cursor: pointer;
}
.auto-row {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
}
.action {
  width: 100%;
  height: 54px;
  margin-top: 18px;
  border-radius: var(--radius);
  background: var(--brand);
  color: #fff;
  font-size: 17px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  transition: background 0.15s, transform 0.05s;
}
.action:hover {
  background: var(--brand-600);
}
.action:active {
  transform: translateY(1px);
}
.action:disabled {
  opacity: 0.7;
  cursor: default;
}
.action.stop {
  background: var(--danger);
}
.action-row {
  display: flex;
  gap: 12px;
  margin-top: 18px;
}
.action-row .action {
  margin-top: 0;
  flex: 1;
}
.action.secondary {
  flex: none;
  width: auto;
  padding: 0 24px;
  background: transparent;
  border: 1px solid var(--brand);
  color: var(--brand);
}
.action.secondary:hover {
  background: var(--brand-soft);
}
.launch-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-top: 10px;
}
.link-btn {
  font-size: 12px;
  color: var(--text-3);
  cursor: pointer;
}
.link-btn:hover {
  color: var(--brand);
}
.launch-sep {
  font-size: 12px;
  color: var(--text-3);
}

.capture-hint {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 12px;
  font-size: 12px;
  color: var(--text-3);
}
.capture-hint.warn {
  color: var(--warn);
}
.lan-chip {
  color: var(--brand);
  font-weight: 600;
}

/* advisory hints (网络良好，可不加速 / 加速效果不明显) */
.advice-banner {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 14px;
  padding: 9px 12px;
  border-radius: var(--radius);
  background: rgba(23, 178, 106, 0.12);
  font-size: 12px;
  color: var(--ok);
}
.advice-banner .advice-close {
  color: var(--ok);
}
.effect-advice {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 10px;
  padding: 9px 12px;
  border-radius: var(--radius);
  background: rgba(255, 193, 7, 0.12);
  font-size: 12px;
  color: var(--warn);
}
.advice-text {
  flex: 1;
  min-width: 0;
}
.advice-title {
  font-weight: 600;
}
.advice-detail {
  margin-top: 2px;
  color: var(--text-2);
  word-break: break-word;
}
.advice-close {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2px;
  cursor: pointer;
  opacity: 0.7;
}
.advice-close:hover {
  opacity: 1;
}

/* per-game server choice */
.server-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding-bottom: 14px;
  margin-bottom: 14px;
  border-bottom: 1px solid var(--border);
}
.server-label {
  font-weight: 600;
  font-size: 14px;
  white-space: nowrap;
}
.server-hint {
  margin-left: auto;
}
.seg {
  display: inline-flex;
  padding: 2px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.seg.disabled {
  opacity: 0.6;
}
.seg-btn {
  white-space: nowrap;
  padding: 4px 14px;
  border-radius: calc(var(--radius-sm) - 2px);
  font-size: 13px;
  color: var(--text-2);
  transition: all 0.14s;
}
.seg-btn:hover:not(:disabled) {
  color: var(--text);
}
.seg-btn.active {
  background: var(--brand);
  color: #fff;
  font-weight: 600;
}
.seg-btn:disabled {
  cursor: not-allowed;
}

/* per-game compatibility note */
.compat-note {
  margin: -6px 0 14px;
  padding: 9px 12px;
  border-radius: var(--radius);
  background: var(--surface-2);
}
.compat-head {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--text-2);
}
.compat-label {
  font-weight: 600;
  font-size: 12px;
}
.compat-tag {
  padding: 1px 7px;
  border-radius: 999px;
  font-size: 10px;
  background: var(--brand-soft);
  color: var(--brand-600);
}
.compat-tag.unverified {
  background: rgba(255, 193, 7, 0.15);
  color: var(--warn);
}
.compat-text {
  margin-top: 5px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--text-2);
  word-break: break-word;
}
.mode-rec {
  margin-left: 4px;
  padding: 1px 5px;
  border-radius: 999px;
  font-size: 10px;
  font-weight: 600;
  background: var(--brand-soft);
  color: var(--brand-600);
}
.seg-btn.active .mode-rec {
  background: rgba(255, 255, 255, 0.25);
  color: #fff;
}

/* per-game download split */
.dl-name {
  font-size: 13px;
  color: var(--text-2);
  white-space: nowrap;
}
.dl-hint {
  flex: 1;
  margin-left: 12px;
  text-align: right;
}

/* smart routing */
.rec {
  margin-top: 14px;
  padding: 12px 14px;
  border-radius: var(--radius);
  background: var(--surface-2);
}
.rec-line {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--brand);
}
.rec-label {
  color: var(--text-2);
}
.rec-name {
  font-weight: 600;
  color: var(--text);
}
.rec-reason {
  margin-top: 4px;
}

.cand {
  margin-top: 12px;
}
.cand-head {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 2px;
  color: var(--text-2);
  font-size: 13px;
}
.cand-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--text-2);
  font-size: 13px;
}
.cand-toggle .open {
  transform: rotate(180deg);
}
.cand-title {
  font-weight: 600;
  color: var(--text);
}
.cand-spacer {
  flex: 1;
}
.cand-refresh {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--brand);
  font-size: 12px;
  cursor: pointer;
}
.cand-refresh:disabled {
  opacity: 0.6;
  cursor: default;
}
.cand-list {
  margin-top: 6px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.cand-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.chip {
  flex: none;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 11px;
  white-space: nowrap;
}
.cand-main {
  flex: 1;
  min-width: 0;
}
.cand-remark {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cand-bar {
  height: 3px;
  margin: 4px 0 3px;
  border-radius: 2px;
  background: var(--surface-3);
  overflow: hidden;
}
.cand-bar-fill {
  display: block;
  height: 100%;
  border-radius: 2px;
}
.cand-right {
  flex: none;
  display: flex;
  align-items: center;
  gap: 8px;
}
.cand-delay {
  font-size: 13px;
  font-weight: 600;
  min-width: 52px;
  text-align: right;
}
.badge {
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 11px;
}
.small-btn {
  padding: 4px 10px;
  font-size: 12px;
}

.metrics {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
  margin-top: 16px;
}
.metric {
  padding: 16px 18px;
}
.metric-head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.metric-val {
  font-size: 25px;
  font-weight: 700;
  margin-top: 8px;
}

.effect {
  margin-top: 16px;
  padding: 20px 22px;
}
.effect-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}
.effect-title {
  font-weight: 600;
  font-size: 14px;
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.effect-summary {
  font-size: 14px;
  font-weight: 600;
}
.effect-note {
  margin-top: 6px;
}
.effect-compare {
  margin-top: 8px;
}
.effect-baseline {
  margin-top: 2px;
}
.effect-target {
  margin-top: 2px;
}
.effect-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}
.ghost-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 30px;
  padding: 0 12px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--brand);
  font-size: 12px;
  cursor: pointer;
}
.ghost-btn:hover:not(:disabled) {
  background: var(--brand-soft);
}
.ghost-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.traffic {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-top: 12px;
}
.tr {
  padding: 16px 18px;
  display: flex;
  align-items: center;
  gap: 12px;
}
.tr-ico {
  width: 40px;
  height: 40px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.tr-ico.down {
  background: rgba(23, 178, 106, 0.14);
  color: var(--ok);
}
.tr-ico.up {
  background: var(--brand-soft);
  color: var(--brand);
}
.tr-val {
  font-size: 17px;
  font-weight: 600;
  margin-top: 2px;
}

/* switch */
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
