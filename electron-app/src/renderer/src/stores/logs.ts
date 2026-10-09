import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { LogEntry } from '@shared/types'
import { backend } from '@/api/backend'

const MAX_ENTRIES = 500

/**
 * Global log buffer. Subscribes once at app start (see App.vue) so entries emitted before the user opens
 * the Logs page are not lost, and seeds itself from the backend's recent-log snapshot.
 */
export const useLogsStore = defineStore('logs', () => {
  const entries = ref<LogEntry[]>([])
  let off: (() => void) | null = null

  function append(l: LogEntry): void {
    entries.value.push(l)
    if (entries.value.length > MAX_ENTRIES) entries.value.splice(0, entries.value.length - MAX_ENTRIES)
  }

  async function init(): Promise<void> {
    if (off) return
    off = backend.onLog(append)
    try {
      const recent = await backend.recentLogs()
      // Prepend the snapshot, then trim to the cap.
      entries.value = [...recent, ...entries.value].slice(-MAX_ENTRIES)
    } catch {
      // Backend not reachable yet — live events will still populate the buffer.
    }
  }

  function clear(): void {
    entries.value = []
  }

  return { entries, init, clear }
})
