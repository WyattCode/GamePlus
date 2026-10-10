import { app } from 'electron'
import { readFileSync, renameSync, writeFileSync } from 'fs'
import { join } from 'path'
import type { UiPrefs } from '../shared/types'

// Tiny JSON prefs store for renderer-only UI state (onboarding, etc.). Kept separate from the
// backend's settings.json so the renderer can read/write it before the sidecar is even running.
// Read once (sync) on first access, then cached; writes are atomic (temp file + rename).

const DEFAULTS: UiPrefs = {
  onboardingDone: false
}

// Per-key validation table — extend here when UiPrefs grows. Unknown keys are ignored.
type Validator = (v: unknown) => boolean
const VALIDATORS: { [K in keyof UiPrefs]: Validator } = {
  onboardingDone: (v) => typeof v === 'boolean'
}

let cache: UiPrefs | null = null

function prefsPath(): string {
  return join(app.getPath('userData'), 'prefs.json')
}

// Assigns a validated value to a known key. The validator guarantees the runtime type matches the
// declared field type, so the cast is confined to this one helper instead of being sprinkled around.
function assign<K extends keyof UiPrefs>(o: UiPrefs, k: K, v: unknown): void {
  o[k] = v as UiPrefs[K]
}

function load(): UiPrefs {
  try {
    const raw = JSON.parse(readFileSync(prefsPath(), 'utf8')) as Record<string, unknown>
    const out: UiPrefs = { ...DEFAULTS }
    for (const key of Object.keys(VALIDATORS) as Array<keyof UiPrefs>) {
      const v = raw[key]
      if (VALIDATORS[key](v)) {
        assign(out, key, v)
      }
    }
    return out
  } catch {
    // Missing or corrupt file → defaults.
    return { ...DEFAULTS }
  }
}

/** Returns the cached prefs, loading them from disk on first access. */
export function getPrefs(): UiPrefs {
  if (!cache) cache = load()
  return { ...cache }
}

/** Merges a validated patch into the prefs and persists them atomically. Returns the new prefs. */
export function setPrefs(patch: Partial<UiPrefs>): UiPrefs {
  const current = cache ?? load()
  const next: UiPrefs = { ...current }
  for (const key of Object.keys(VALIDATORS) as Array<keyof UiPrefs>) {
    if (!(key in patch)) continue
    const v = (patch as Record<string, unknown>)[key]
    if (VALIDATORS[key](v)) {
      assign(next, key, v)
    }
  }
  cache = next
  try {
    const p = prefsPath()
    const tmp = p + '.tmp'
    writeFileSync(tmp, JSON.stringify(next, null, 2), 'utf8')
    renameSync(tmp, p)
  } catch (e) {
    console.error('failed to persist prefs:', e)
  }
  return { ...next }
}
