import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { router } from '@/router'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { UpdateConfig, UpdateConfigView, UpdateState } from '@shared/types'

/**
 * Renderer-side mirror of the main-process auto-updater. The main process owns the actual state
 * machine; this store just caches the latest snapshot, forwards user actions and surfaces errors.
 */
export const useUpdateStore = defineStore('update', () => {
  const toast = useToastStore()

  const state = ref<UpdateState | null>(null)
  const config = ref<UpdateConfigView | null>(null)
  const supported = ref<boolean>(!!window.gameplus?.update)

  let unsubscribe: (() => void) | null = null
  let initialized = false

  function apply(next: UpdateState | null): void {
    if (!next) return
    const prev = state.value
    state.value = next
    // Only notify on a transition into "available" (not on every push while it stays available).
    if (next.status === 'available' && prev?.status !== 'available' && next.latest) {
      toast.info(`发现新版本 v${next.latest.version}`, {
        label: '查看',
        run: () => router.push('/settings')
      })
    }
  }

  /** Subscribes to main-process pushes and loads the initial state/config. Safe to call twice. */
  async function init(): Promise<void> {
    if (initialized) return
    initialized = true
    const api = window.gameplus?.update
    if (!api) {
      supported.value = false
      return
    }
    supported.value = true
    unsubscribe = api.onState((s) => apply(s))
    try {
      apply(await api.getState())
    } catch {
      // ignore — the state stays null until the first push
    }
    try {
      config.value = await api.getConfig()
    } catch {
      // ignore
    }
  }

  function dispose(): void {
    unsubscribe?.()
    unsubscribe = null
    initialized = false
  }

  async function check(): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      apply(await api.check())
    } catch (e) {
      toast.error('检查更新失败：' + errorMessage(e))
    }
  }

  async function download(): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      apply(await api.download())
    } catch (e) {
      toast.error('下载更新失败：' + errorMessage(e))
    }
  }

  async function cancel(): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      await api.cancel()
    } catch (e) {
      toast.error('取消下载失败：' + errorMessage(e))
    }
  }

  async function install(): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      apply(await api.install())
    } catch (e) {
      toast.error('安装更新失败：' + errorMessage(e))
    }
  }

  async function saveConfig(patch: Partial<UpdateConfig>): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      const next = await api.setConfig(patch)
      if (next) config.value = next
    } catch (e) {
      toast.error('保存更新设置失败：' + errorMessage(e))
    }
  }

  async function setToken(token: string | null): Promise<void> {
    const api = window.gameplus?.update
    if (!api) return
    try {
      const ok = await api.setToken(token)
      if (ok && config.value) config.value = { ...config.value, hasToken: !!token }
    } catch (e) {
      toast.error('保存令牌失败：' + errorMessage(e))
    }
  }

  const progressPct = computed<number>(() => {
    const p = state.value?.progress
    if (!p || !p.total) return 0
    return Math.min(100, Math.max(0, Math.round((p.received / p.total) * 100)))
  })

  return {
    state,
    config,
    supported,
    progressPct,
    init,
    dispose,
    check,
    download,
    cancel,
    install,
    saveConfig,
    setToken
  }
})
