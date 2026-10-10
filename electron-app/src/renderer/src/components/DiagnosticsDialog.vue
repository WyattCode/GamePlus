<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { useToastStore, errorMessage } from '@/stores/toast'
import { exportDiagnosticBundle } from '@/utils/diagBundle'
import type { DiagnosticItem, DiagnosticStatus } from '@shared/types'

const diag = useDiagnosticsStore()
const toast = useToastStore()

const exporting = ref(false)

const statusIcon: Record<DiagnosticStatus, string> = {
  ok: 'check',
  warning: 'warning',
  error: 'warning',
  skipped: 'info'
}

const statusColor: Record<DiagnosticStatus, string> = {
  ok: 'var(--ok)',
  warning: 'var(--warn)',
  error: 'var(--danger)',
  skipped: 'var(--text-3)'
}

const items = computed<DiagnosticItem[]>(() => diag.report?.items ?? [])

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape' && diag.visible) diag.close()
}

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))

function plainReport(): string {
  const r = diag.report
  const lines: string[] = []
  lines.push('Game+ 诊断报告')
  if (diag.lastError) lines.push(`错误信息：${diag.lastError}`)
  if (r?.errorExplanation) lines.push(`错误说明：${r.errorExplanation}`)
  if (r) {
    lines.push(`结论：${r.summary}`)
    lines.push(`时间：${r.ranAt}`)
    for (const i of r.items) {
      lines.push(`[${i.status}] ${i.title}：${i.detail}${i.suggestion ? `（建议：${i.suggestion}）` : ''}`)
    }
  }
  return lines.join('\n')
}

async function copy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(plainReport())
    toast.info('已复制')
  } catch {
    toast.error('复制失败')
  }
}

async function exportBundle(): Promise<void> {
  if (exporting.value) return
  exporting.value = true
  try {
    const path = await exportDiagnosticBundle()
    if (path) toast.info(`诊断包已保存：${path}`)
    else toast.info('已取消')
  } catch (e) {
    toast.error('导出失败：' + errorMessage(e))
  } finally {
    exporting.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="diag.visible" class="overlay" @click.self="diag.close()">
      <div class="dialog">
        <div class="d-head">
          <h3>一键诊断</h3>
          <button class="x" @click="diag.close()"><AppIcon name="close" :size="16" /></button>
        </div>

        <div class="d-body">
          <div v-if="diag.lastError" class="err-block">
            <div class="err-label">错误信息</div>
            <div class="err-text">{{ diag.lastError }}</div>
            <div v-if="diag.report?.errorExplanation" class="err-explain">
              {{ diag.report.errorExplanation }}
            </div>
          </div>

          <div v-if="diag.running && !diag.report" class="muted small">检测中…</div>

          <template v-if="diag.report">
            <div class="summary">{{ diag.report.summary }}</div>

            <div class="items">
              <div v-for="i in items" :key="i.id" class="item">
                <div class="i-ico" :style="{ color: statusColor[i.status] }">
                  <AppIcon :name="statusIcon[i.status]" :size="18" />
                </div>
                <div class="i-main">
                  <div class="i-title">{{ i.title }}</div>
                  <div class="i-detail">{{ i.detail }}</div>
                  <div v-if="i.suggestion" class="i-suggest muted small">建议：{{ i.suggestion }}</div>
                </div>
                <button v-if="i.fixAction" class="btn btn-primary fix" @click="diag.fix(i.fixAction)">
                  修复
                </button>
              </div>
            </div>
          </template>
        </div>

        <div class="d-foot">
          <div class="hint muted small">
            包含日志、诊断结果与脱敏后的配置（已移除密码、密钥与订阅链接），分享前请自行检查
          </div>
          <div class="foot-actions">
            <button class="btn" @click="copy">复制报告</button>
            <button class="btn" :disabled="exporting" @click="exportBundle">
              {{ exporting ? '导出中…' : '导出诊断包' }}
            </button>
            <button class="btn btn-primary" :disabled="diag.running" @click="diag.rerun()">
              {{ diag.running ? '检测中…' : '重新检测' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 10vh 20px 20px;
  z-index: 200;
}
.dialog {
  width: 560px;
  max-height: 80vh;
  overflow-y: auto;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-lg, 0 20px 60px rgba(0, 0, 0, 0.4));
}
.d-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}
.d-head h3 {
  font-size: 15px;
  font-weight: 600;
}
.x {
  color: var(--text-3);
  font-size: 16px;
  padding: 4px;
}
.d-body {
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.err-block {
  padding: 12px 14px;
  border-radius: var(--radius-sm);
  background: rgba(220, 53, 69, 0.1);
  border: 1px solid var(--danger);
}
.err-label {
  font-size: 12px;
  font-weight: 700;
  color: var(--danger);
  margin-bottom: 4px;
}
.err-text {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  color: var(--text-2);
  word-break: break-word;
}
.err-explain {
  margin-top: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text);
}
.summary {
  font-size: 14px;
  font-weight: 600;
}
.items {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.i-ico {
  flex: none;
  margin-top: 1px;
}
.i-main {
  flex: 1;
  min-width: 0;
}
.i-title {
  font-size: 13px;
  font-weight: 600;
}
.i-detail {
  font-size: 12px;
  color: var(--text-2);
  margin-top: 2px;
  word-break: break-word;
}
.i-suggest {
  margin-top: 4px;
}
.fix {
  flex: none;
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
}
.d-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
}
.hint {
  flex: 1;
  min-width: 0;
  line-height: 1.4;
}
.foot-actions {
  display: flex;
  gap: 10px;
  flex: none;
}
.btn {
  height: 38px;
  padding: 0 20px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  background: var(--surface-2);
  color: var(--text-2);
}
.btn-primary {
  background: var(--brand);
  color: #fff;
}
.btn-primary:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
