<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import EffectChart from '@/components/EffectChart.vue'
import GameGlyph from '@/components/GameGlyph.vue'
import { gameColor } from '@/utils/gameIcon'
import { useAccelStore } from '@/stores/accel'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { useLibraryStore } from '@/stores/library'
import { errorMessage, useToastStore } from '@/stores/toast'
import { fmtDelay, fmtDuration, fmtLatency, fmtRate } from '@/utils/format'

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
  const base = `直连 ${b.latency >= 0 ? b.latency + ' ms' : '—'} · 丢包 ${b.loss}%`
  if (!c) return base
  return `${base} → 加速 ${c.latency >= 0 ? c.latency + ' ms' : '—'} · 丢包 ${c.loss}%`
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

        <button class="action" :class="{ stop: running }" :disabled="busy" @click="toggle">
          <AppIcon name="bolt" :size="22" />
          {{ buttonText }}
        </button>

        <div v-if="captureHint" class="capture-hint" :class="{ warn: captureHint.warn }">
          <AppIcon name="info" :size="14" />
          {{ captureHint.text }}
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
            加速效果
            <span v-if="effectIsLast" class="muted small">上次加速</span>
          </div>
          <div class="effect-summary" :style="{ color: effectColor }">{{ effect.summary }}</div>
        </div>
        <div v-if="effectCompare" class="effect-compare muted small">{{ effectCompare }}</div>
        <div v-if="effect.target" class="effect-target muted small">目标 {{ effect.target }}</div>
        <EffectChart :series="effect.series" :baseline="effect.baseline?.latency ?? null" />
      </div>

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
.effect-compare {
  margin-top: 8px;
}
.effect-target {
  margin-top: 2px;
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
