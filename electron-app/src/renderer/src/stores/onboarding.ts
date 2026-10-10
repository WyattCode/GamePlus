import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getPrefs, setPrefs } from '@/api/prefs'

/**
 * First-run onboarding state. `step` is 0..3:
 *   0 = welcome / admin-permission explanation (backend not started yet)
 *   1 = import lines (subscription or single link)
 *   2 = pick a game
 *   3 = start accelerating
 */
export const useOnboardingStore = defineStore('onboarding', () => {
  const visible = ref(false)
  const step = ref(0)
  const loaded = ref(false)

  // Called once when the user leaves step 0 (or skips/finishes onboarding). App.vue uses this to
  // start the backend + load app data, which must not happen while the UAC explanation is showing.
  let onLeaveStep0: (() => void) | null = null

  /** Registers the callback fired the first time the user leaves step 0. */
  function setOnLeaveStep0(cb: () => void): void {
    onLeaveStep0 = cb
  }

  function fireLeaveStep0(): void {
    const cb = onLeaveStep0
    onLeaveStep0 = null
    cb?.()
  }

  /** Reads prefs and shows the dialog when onboarding has not been completed yet. */
  async function load(): Promise<void> {
    try {
      const p = await getPrefs()
      visible.value = !p.onboardingDone
    } catch {
      // If prefs are unreadable, don't block the app with a dialog.
      visible.value = false
    } finally {
      loaded.value = true
    }
  }

  function next(): void {
    if (step.value === 0) fireLeaveStep0()
    if (step.value < 3) step.value++
  }

  function back(): void {
    if (step.value > 0) step.value--
  }

  /** Persists onboardingDone=true and hides the dialog. */
  async function finish(): Promise<void> {
    fireLeaveStep0()
    visible.value = false
    try {
      await setPrefs({ onboardingDone: true })
    } catch {
      // best-effort — the dialog is already hidden
    }
  }

  /** Re-opens the dialog from settings; skips the permission step because the backend already runs. */
  function reopen(): void {
    step.value = 1
    visible.value = true
  }

  return { visible, step, loaded, load, next, back, finish, reopen, setOnLeaveStep0 }
})
