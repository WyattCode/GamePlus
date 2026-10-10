import { defineStore } from 'pinia'
import { watch } from 'vue'
import type { AutoAccelEvent, TrayAction, TrayInfo } from '@shared/types'
import { backend } from '@/api/backend'
import { useAccelStore } from '@/stores/accel'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { useLibraryStore } from '@/stores/library'
import { useToastStore } from '@/stores/toast'
import { track } from '@/utils/telemetry'

// Global auto-acceleration listener + tray status sync. Initialised once from App.vue's startApp().
export const useAutoStore = defineStore('auto', () => {
  const accel = useAccelStore()
  const library = useLibraryStore()
  const toast = useToastStore()
  const diagnostics = useDiagnosticsStore()

  let started = false

  /** Forwards a message to the OS notification centre when the window is hidden. */
  function notify(title: string, body: string): void {
    try {
      void window.gameplus?.app?.notify?.(title, body)
    } catch {
      // notifications are best-effort
    }
  }

  function onEvent(e: AutoAccelEvent): void {
    track({ type: 'auto_accel', kind: e.kind })
    switch (e.kind) {
      case 'started': {
        toast.info(`检测到 ${e.gameName} 启动，已自动加速`)
        const g = library.games.find((x) => x.id === e.gameId) ?? null
        if (g) accel.selectedGame = g
        notify('Game+ 自动加速', `检测到 ${e.gameName} 启动，已自动加速`)
        break
      }
      case 'stopped': {
        toast.info(`${e.gameName} 已退出，已停止加速`)
        notify('Game+ 自动加速', `${e.gameName} 已退出，已停止加速`)
        break
      }
      case 'failed': {
        toast.error(`自动加速 ${e.gameName} 失败：${e.message}`, {
          label: '诊断',
          run: () => diagnostics.open(e.message)
        })
        notify('Game+ 自动加速失败', `${e.gameName}：${e.message}`)
        break
      }
      case 'skipped': {
        toast.info(e.message)
        break
      }
    }
  }

  function onTrayAction(action: TrayAction): void {
    if (action === 'toggle') {
      if (!accel.selectedGame) {
        // No game selected → surface the window and tell the user what to do.
        toast.info('请先选择游戏')
        return
      }
      void accel.toggle().catch(() => {
        // errors surface via the status stream / toasts elsewhere
      })
    } else if (action === 'diagnose') {
      diagnostics.open()
    }
  }

  // --- Tray status sync (throttled for latency-only changes) ---
  let lastPush = 0
  let lastKey = ''
  let pending: TrayInfo | null = null
  let timer: ReturnType<typeof setTimeout> | null = null

  function pushTray(info: TrayInfo): void {
    try {
      void window.gameplus?.tray?.update?.(info)
    } catch {
      // tray is best-effort
    }
  }

  function scheduleTray(info: TrayInfo): void {
    const key = `${info.state}|${info.gameName ?? ''}`
    const now = Date.now()
    // State/game changes push immediately; latency-only changes are throttled to once per 2 s.
    if (key !== lastKey || now - lastPush >= 2000) {
      lastKey = key
      lastPush = now
      pending = null
      pushTray(info)
      return
    }
    pending = info
    if (timer) return
    timer = setTimeout(() => {
      timer = null
      if (pending) {
        lastKey = `${pending.state}|${pending.gameName ?? ''}`
        lastPush = Date.now()
        pushTray(pending)
        pending = null
      }
    }, 2000 - (now - lastPush))
  }

  function start(): void {
    if (started) return
    started = true

    // App-lifetime listeners: start() runs once from App.vue outside any effect scope.
    backend.onAutoAccel(onEvent)

    window.gameplus?.tray?.onAction?.(onTrayAction)

    watch(
      () => [accel.status.state, accel.status.latency, accel.selectedGame?.name ?? null] as const,
      () => {
        scheduleTray({
          state: accel.status.state,
          gameName: accel.selectedGame?.name ?? null,
          latency: accel.status.latency
        })
      },
      { immediate: true }
    )
  }

  return { start }
})
