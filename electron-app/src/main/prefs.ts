import { app } from 'electron'
import { readFileSync, renameSync, writeFileSync } from 'fs'
import { join } from 'path'
import type { TelemetryConfig, UiPrefs, UpdateConfig } from '../shared/types'

// Tiny JSON prefs store for renderer-only UI state (onboarding, etc.). Kept separate from the
// backend's settings.json so the renderer can read/write it before the sidecar is even running.
// Read once (sync) on first access, then cached; writes are atomic (temp file + rename).

const DEFAULT_UPDATE: UpdateConfig = {
  source: 'none',
  manifestUrl: '',
  githubRepo: '',
  includePrerelease: false,
  autoCheck: true
}

const DEFAULT_TELEMETRY: TelemetryConfig = {
  enabled: false,
  endpoint: '',
  installId: null
}

const DEFAULTS: UiPrefs = {
  onboardingDone: false,
  startHidden: false,
  migratedLoginItem: false,
  update: { ...DEFAULT_UPDATE },
  telemetry: { ...DEFAULT_TELEMETRY }
}

// Per-key validation table — extend here when UiPrefs grows. Unknown keys are ignored.
type Validator = (v: unknown) => boolean

/** True for an https URL, or an http URL pointing at loopback (localhost / 127.0.0.1 / ::1). */
function isAllowedUpdateUrl(url: string): boolean {
  if (url.length === 0) return true
  if (url.length > 2048) return false
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return false
  }
  if (parsed.protocol === 'https:') return true
  if (parsed.protocol !== 'http:') return false
  const host = parsed.hostname.toLowerCase()
  return host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]'
}

const GITHUB_REPO_RE = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/

/** Validates a full UpdateConfig object (nested objects are validated as a whole). */
function isUpdateConfig(v: unknown): boolean {
  if (!v || typeof v !== 'object') return false
  const o = v as Record<string, unknown>
  if (o.source !== 'none' && o.source !== 'manifest' && o.source !== 'github') return false
  if (typeof o.manifestUrl !== 'string' || !isAllowedUpdateUrl(o.manifestUrl)) return false
  if (typeof o.githubRepo !== 'string') return false
  if (o.githubRepo.length > 0 && !GITHUB_REPO_RE.test(o.githubRepo)) return false
  if (typeof o.includePrerelease !== 'boolean') return false
  if (typeof o.autoCheck !== 'boolean') return false
  return true
}

/** Validates a full TelemetryConfig object (nested objects are validated as a whole). */
function isTelemetryConfig(v: unknown): boolean {
  if (!v || typeof v !== 'object') return false
  const o = v as Record<string, unknown>
  if (typeof o.enabled !== 'boolean') return false
  if (typeof o.endpoint !== 'string' || !isAllowedUpdateUrl(o.endpoint)) return false
  return true
}

const VALIDATORS: { [K in keyof UiPrefs]: Validator } = {
  onboardingDone: (v) => typeof v === 'boolean',
  startHidden: (v) => typeof v === 'boolean',
  migratedLoginItem: (v) => typeof v === 'boolean',
  update: isUpdateConfig,
  telemetry: isTelemetryConfig
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

/** Normalizes a validated update config into a fresh object (drops unknown keys). */
function normalizeUpdate(v: unknown): UpdateConfig {
  const o = v as Record<string, unknown>
  return {
    source: o.source as UpdateConfig['source'],
    manifestUrl: o.manifestUrl as string,
    githubRepo: o.githubRepo as string,
    includePrerelease: o.includePrerelease as boolean,
    autoCheck: o.autoCheck as boolean
  }
}

/** Normalizes a validated telemetry config into a fresh object (drops unknown keys). */
function normalizeTelemetry(v: unknown): TelemetryConfig {
  const o = v as Record<string, unknown>
  return {
    enabled: o.enabled as boolean,
    endpoint: o.endpoint as string,
    installId: null
  }
}

function load(): UiPrefs {
  try {
    const raw = JSON.parse(readFileSync(prefsPath(), 'utf8')) as Record<string, unknown>
    const out: UiPrefs = {
      ...DEFAULTS,
      update: { ...DEFAULT_UPDATE },
      telemetry: { ...DEFAULT_TELEMETRY }
    }
    for (const key of Object.keys(VALIDATORS) as Array<keyof UiPrefs>) {
      const v = raw[key]
      if (VALIDATORS[key](v)) {
        if (key === 'update') {
          out.update = normalizeUpdate(v)
        } else if (key === 'telemetry') {
          out.telemetry = normalizeTelemetry(v)
        } else {
          assign(out, key, v)
        }
      }
    }
    return out
  } catch {
    // Missing or corrupt file → defaults.
    return { ...DEFAULTS, update: { ...DEFAULT_UPDATE }, telemetry: { ...DEFAULT_TELEMETRY } }
  }
}

/** Returns the cached prefs, loading them from disk on first access. */
export function getPrefs(): UiPrefs {
  if (!cache) cache = load()
  return { ...cache, update: { ...cache.update }, telemetry: { ...cache.telemetry } }
}

/** Merges a validated patch into the prefs and persists them atomically. Returns the new prefs. */
export function setPrefs(patch: Partial<UiPrefs>): UiPrefs {
  const current = cache ?? load()
  const next: UiPrefs = { ...current, update: { ...current.update }, telemetry: { ...current.telemetry } }
  for (const key of Object.keys(VALIDATORS) as Array<keyof UiPrefs>) {
    if (!(key in patch)) continue
    const v = (patch as Record<string, unknown>)[key]
    if (key === 'update') {
      // Nested object: validate the whole thing, then merge over the current config so a partial
      // patch (e.g. only `source`) keeps the other fields.
      if (v && typeof v === 'object') {
        const merged = { ...current.update, ...(v as Record<string, unknown>) }
        if (isUpdateConfig(merged)) next.update = normalizeUpdate(merged)
      }
      continue
    }
    if (key === 'telemetry') {
      if (v && typeof v === 'object') {
        const merged = { ...current.telemetry, ...(v as Record<string, unknown>) }
        if (isTelemetryConfig(merged)) next.telemetry = normalizeTelemetry(merged)
      }
      continue
    }
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
  return { ...next, update: { ...next.update }, telemetry: { ...next.telemetry } }
}
