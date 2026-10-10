// Shared 「加速并启动」 logic: used by AccelerateView (button) and App.vue (--launch <gameId>
// requests from the main process, e.g. the desktop shortcut).
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { Game } from '@shared/types'
import { useAccelStore } from '@/stores/accel'
import { useLibraryStore } from '@/stores/library'
import { errorMessage, useToastStore } from '@/stores/toast'
import { launchGame } from '@/utils/launchApi'

/** How long to wait for the accelerating state before giving up (ms). */
const ACCEL_WAIT_MS = 30_000

/**
 * Game id whose launch-target dialog should be open. Module-level so both the accelerate view's
 * 「启动方式」 button and a --launch request without a configured target can open the same dialog.
 */
export const launchDialogGameId = ref<string | null>(null)

type AccelStore = ReturnType<typeof useAccelStore>

/** Resolves true once the accel state reaches 'accelerating', false after timeoutMs. */
function waitForAccelerating(accel: AccelStore, timeoutMs: number): Promise<boolean> {
  if (accel.status.state === 'accelerating') return Promise.resolve(true)
  return new Promise((resolve) => {
    let done = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const stop = watch(
      () => accel.status.state,
      (s) => {
        if (done) return
        if (s === 'accelerating') {
          done = true
          if (timer) clearTimeout(timer)
          stop()
          resolve(true)
        }
      }
    )
    timer = setTimeout(() => {
      if (done) return
      done = true
      stop()
      resolve(false)
    }, timeoutMs)
  })
}

export function useAccelerateAndLaunch() {
  const accel = useAccelStore()
  const library = useLibraryStore()
  const toast = useToastStore()
  const router = useRouter()

  /** Makes sure the library is loaded and the game is selected; returns the game or null. */
  async function ensureGameSelected(gameId: string): Promise<Game | null> {
    // A --launch request can arrive before the library finished loading (main only waits for
    // did-finish-load), so load on demand; load() is idempotent.
    if (library.games.length === 0) {
      try {
        await library.load()
      } catch {
        // fall through: the lookup below fails with a toast
      }
    }
    const game = library.games.find((g) => g.id === gameId) ?? null
    if (!game) return null
    if (accel.selectedGame?.id !== gameId) accel.selectedGame = game
    return game
  }

  /**
   * Starts acceleration for the game (smart node, like the normal start), waits until the state is
   * 'accelerating' (30 s timeout → error toast), then asks the main process to launch the target.
   * Without a configured target: error toast + open the launch-target dialog.
   */
  async function accelerateAndLaunch(gameId?: string): Promise<void> {
    const id = gameId ?? accel.selectedGame?.id
    if (!id) {
      toast.error('请先选择游戏')
      return
    }
    const game = await ensureGameSelected(id)
    if (!game) {
      toast.error('未找到该游戏')
      return
    }
    if (!game.launchTarget) {
      toast.error('请先设置启动方式')
      launchDialogGameId.value = game.id
      if (router.currentRoute.value.path !== '/accelerate') void router.push('/accelerate')
      return
    }
    try {
      // Already accelerating this very game → just launch; otherwise start (smart node).
      if (!(accel.status.state === 'accelerating' && accel.status.gameId === game.id)) {
        accel.smartNode = true
        await accel.start()
      }
    } catch (e) {
      toast.error('加速启动失败：' + errorMessage(e))
      return
    }
    const ok = await waitForAccelerating(accel, ACCEL_WAIT_MS)
    if (!ok) {
      toast.error('加速启动超时，请稍后重试')
      return
    }
    const res = await launchGame(game.launchTarget)
    if (!res.ok) toast.error(res.error ?? '启动游戏失败')
  }

  /** Handles a 'launch:request' from the main process (--launch <gameId>): select + navigate + run. */
  async function handleLaunchRequest(gameId: string): Promise<void> {
    if (router.currentRoute.value.path !== '/accelerate') void router.push('/accelerate')
    await accelerateAndLaunch(gameId)
  }

  return { accelerateAndLaunch, handleLaunchRequest }
}
