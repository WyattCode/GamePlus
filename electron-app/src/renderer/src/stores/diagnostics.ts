import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { DiagnosticReport } from '@shared/types'
import { backend } from '@/api/backend'
import { useAccelStore } from '@/stores/accel'
import { errorMessage, useToastStore } from '@/stores/toast'
import { track } from '@/utils/telemetry'

/** One-click diagnostics dialog state. */
export const useDiagnosticsStore = defineStore('diagnostics', () => {
  const visible = ref(false)
  const running = ref(false)
  const report = ref<DiagnosticReport | null>(null)
  const lastError = ref<string | null>(null)

  const accel = useAccelStore()
  const toast = useToastStore()

  async function run(): Promise<void> {
    running.value = true
    try {
      const r = await backend.runDiagnostics({
        gameId: accel.selectedGame?.id ?? null,
        nodeId: accel.smartNode ? null : accel.selectedNodeId,
        error: lastError.value
      })
      report.value = r
      const items = r.items ?? []
      track({
        type: 'diagnostics',
        failed: items.filter((i) => i.status === 'error').length,
        warned: items.filter((i) => i.status === 'warning').length
      })
    } catch (e) {
      toast.error('诊断失败：' + errorMessage(e))
    } finally {
      running.value = false
    }
  }

  /** Opens the dialog (optionally with the error that triggered it) and runs the checks immediately. */
  function open(error?: string | null): void {
    lastError.value = error ?? null
    visible.value = true
    void run()
  }

  function rerun(): void {
    void run()
  }

  async function fix(action: string): Promise<void> {
    try {
      const res = await backend.fixDiagnostic(action)
      if (res.fixed) toast.info('已修复')
      else toast.info('无需修复')
    } catch (e) {
      toast.error('修复失败：' + errorMessage(e))
    }
    await run()
  }

  function close(): void {
    visible.value = false
  }

  return { visible, running, report, lastError, open, rerun, fix, close }
})
