import type { UiPrefs } from '@shared/types'

// Renderer-side accessor for the main-process prefs store. In Electron this goes through the
// preload bridge; in a plain browser (vite preview with MockBackend) it falls back to localStorage.

const STORAGE_KEY = 'gameplus.prefs'

const DEFAULTS: UiPrefs = {
  onboardingDone: false,
  startHidden: false
}

function readLocal(): UiPrefs {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULTS }
    const parsed = JSON.parse(raw) as Partial<UiPrefs>
    return {
      onboardingDone: typeof parsed.onboardingDone === 'boolean' ? parsed.onboardingDone : false,
      startHidden: typeof parsed.startHidden === 'boolean' ? parsed.startHidden : false
    }
  } catch {
    return { ...DEFAULTS }
  }
}

function writeLocal(patch: Partial<UiPrefs>): UiPrefs {
  const next = { ...readLocal(), ...patch }
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
  } catch {
    // storage unavailable — ignore
  }
  return next
}

export async function getPrefs(): Promise<UiPrefs> {
  if (typeof window !== 'undefined' && window.gameplus?.prefs) {
    return window.gameplus.prefs.get()
  }
  return readLocal()
}

export async function setPrefs(patch: Partial<UiPrefs>): Promise<UiPrefs> {
  if (typeof window !== 'undefined' && window.gameplus?.prefs) {
    return window.gameplus.prefs.set(patch)
  }
  return writeLocal(patch)
}
