<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { backend } from '@/api/backend'
import type { AccelSession } from '@shared/types'

const props = defineProps<{
  gameId: string
  gameName: string
  refreshKey?: number
}>()

const sessions = ref<AccelSession[]>([])
const loading = ref(false)
const failed = ref(false)

async function load(): Promise<void> {
  if (!props.gameId) {
    sessions.value = []
    return
  }
  loading.value = true
  failed.value = false
  try {
    sessions.value = await backend.listHistory(props.gameId, 20)
  } catch {
    failed.value = true
    sessions.value = []
  } finally {
    loading.value = false
  }
}

watch(
  () => [props.gameId, props.refreshKey] as const,
  () => {
    void load()
  },
  { immediate: true }
)

function pad(n: number): string {
  return String(n).padStart(2, '0')
}

function fmtTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return `${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function fmtDuration(sec: number): string {
  const s = Math.max(0, Math.round(sec))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  if (h > 0) return `${h} 小时 ${m} 分`
  if (m > 0) return `${m} 分 ${r} 秒`
  return `${r} 秒`
}

function modeText(mode: string | null): string | null {
  switch (mode) {
    case 'process':
      return '进程模式'
    case 'tun':
      return 'TUN 模式'
    case 'proxy':
      return '系统代理'
    default:
      return null
  }
}

function serverText(choice: string | null): string | null {
  if (choice === 'cn') return '国服'
  if (choice === 'intl') return '国际服'
  return null
}

function effectText(s: AccelSession): string | null {
  if (s.baselineMs == null || s.acceleratedMs == null) return null
  return `直连 ${s.baselineMs} ms → 加速 ${s.acceleratedMs} ms`
}

function effectColor(pct: number | null): string {
  if (pct == null) return 'var(--text-3)'
  if (pct > 0) return 'var(--ok)'
  if (pct < 0) return 'var(--warn)'
  return 'var(--text-3)'
}

const totalCount = computed(() => sessions.value.length)
const totalSec = computed(() => sessions.value.reduce((a, s) => a + (s.durationSec || 0), 0))
const totalText = computed(() => {
  const s = Math.max(0, Math.round(totalSec.value))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  return `${h} 小时 ${m} 分`
})

async function clearAll(): Promise<void> {
  if (!window.confirm(`确定要清空「${props.gameName}」的加速记录吗？`)) return
  try {
    await backend.clearHistory(props.gameId)
    await load()
  } catch {
    failed.value = true
  }
}
</script>

<template>
  <div class="card history">
    <div class="history-head">
      <div class="history-title">加速记录</div>
      <div class="muted small">仅保存在本机</div>
    </div>

    <div v-if="failed" class="muted small history-msg">加载失败</div>
    <div v-else-if="loading && sessions.length === 0" class="muted small history-msg">加载中…</div>
    <div v-else-if="sessions.length === 0" class="muted small history-msg">还没有加速记录</div>

    <div v-else class="history-list">
      <div v-for="s in sessions" :key="s.id" class="history-row">
        <div class="history-time">{{ fmtTime(s.startedAt) }}</div>
        <div class="history-main">
          <div class="history-line">
            <span class="history-dur">{{ fmtDuration(s.durationSec) }}</span>
            <span class="history-node" :title="s.nodeName ?? ''">{{ s.nodeName ?? '—' }}</span>
            <span v-if="serverText(s.serverChoice)" class="tag">{{ serverText(s.serverChoice) }}</span>
            <span v-if="modeText(s.mode)" class="tag">{{ modeText(s.mode) }}</span>
          </div>
          <div class="history-effect">
            <template v-if="effectText(s)">
              <span class="muted small">{{ effectText(s) }}</span>
              <span
                v-if="s.latencyPct != null"
                class="history-pct"
                :style="{ color: effectColor(s.latencyPct) }"
              >
                {{ s.latencyPct > 0 ? `-${s.latencyPct}%` : s.latencyPct < 0 ? `+${Math.abs(s.latencyPct)}%` : '0%' }}
              </span>
            </template>
            <span v-else class="muted small">无效果数据</span>
          </div>
        </div>
      </div>
    </div>

    <div class="history-foot">
      <span class="muted small">共 {{ totalCount }} 次 · 累计 {{ totalText }}</span>
      <button
        class="btn small-btn"
        :disabled="totalCount === 0"
        @click="clearAll"
      >
        <AppIcon name="trash" :size="13" />
        清空本游戏记录
      </button>
    </div>
  </div>
</template>

<style scoped>
.history {
  margin-top: 16px;
  padding: 20px 22px;
}
.history-head {
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.history-title {
  font-weight: 600;
  font-size: 14px;
}
.history-msg {
  margin-top: 12px;
}
.history-list {
  margin-top: 12px;
  max-height: 240px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.history-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.history-time {
  flex: none;
  font-size: 12px;
  color: var(--text-3);
  min-width: 74px;
  padding-top: 1px;
}
.history-main {
  flex: 1;
  min-width: 0;
}
.history-line {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.history-dur {
  font-size: 13px;
  font-weight: 600;
}
.history-node {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 180px;
}
.tag {
  flex: none;
  padding: 1px 8px;
  border-radius: 999px;
  background: var(--brand-soft);
  color: var(--brand);
  font-size: 11px;
  white-space: nowrap;
}
.history-effect {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 3px;
}
.history-pct {
  font-size: 12px;
  font-weight: 600;
}
.history-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}
.small-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  font-size: 12px;
}
.small-btn:disabled {
  opacity: 0.5;
  cursor: default;
}
</style>
