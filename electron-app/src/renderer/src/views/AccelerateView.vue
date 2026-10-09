<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import GameGlyph from '@/components/GameGlyph.vue'
import { gameColor } from '@/utils/gameIcon'
import { useAccelStore } from '@/stores/accel'
import { useLibraryStore } from '@/stores/library'
import { errorMessage } from '@/stores/toast'
import { fmtDelay, fmtDuration, fmtLatency, fmtRate } from '@/utils/format'

const accel = useAccelStore()
const library = useLibraryStore()
const router = useRouter()

const error = ref<string | null>(null)
const busy = ref(false)

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

        <div v-if="accel.nodeUnavailable" class="capture-hint warn">
          <AppIcon name="info" :size="14" />
          之前选择的线路已不存在，已切换为智能选路
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
