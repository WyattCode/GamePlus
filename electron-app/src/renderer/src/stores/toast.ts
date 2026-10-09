import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ToastKind = 'info' | 'error'

export interface Toast {
  id: number
  kind: ToastKind
  message: string
}

let seq = 0

/** Minimal global toast queue. Views call `toast.error(msg)` / `toast.info(msg)`. */
export const useToastStore = defineStore('toast', () => {
  const items = ref<Toast[]>([])

  function push(kind: ToastKind, message: string, timeoutMs = 4000): void {
    const id = ++seq
    items.value.push({ id, kind, message })
    setTimeout(() => dismiss(id), timeoutMs)
  }

  function dismiss(id: number): void {
    items.value = items.value.filter((t) => t.id !== id)
  }

  function error(message: string): void {
    push('error', message)
  }

  function info(message: string): void {
    push('info', message)
  }

  return { items, push, dismiss, error, info }
})

/** Convenience: turn an unknown thrown value into a user-facing message. */
export function errorMessage(e: unknown): string {
  if (e instanceof Error) return e.message
  return String(e)
}
