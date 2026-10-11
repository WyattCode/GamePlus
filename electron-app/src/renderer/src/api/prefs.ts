import type { TelemetryConfig, UiPrefs, UpdateConfig, UpdateSource } from '@shared/types'

// Renderer-side accessor for the main-process prefs store. In Electron this goes through the
// preload bridge; in a plain browser (vite preview with MockBackend) it falls back to localStorage.

const STORAGE_KEY = 'gameplus.prefs'

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
  launchNodeMode: 'keep',
  update: { ...DEFAULT_UPDATE },
  telemetry: { ...DEFAULT_TELEMETRY }
}

function parseUpdate(raw: unknown): UpdateConfig {
  const p = (raw ?? {}) as Partial<UpdateConfig>
  const source: UpdateSource =
    p.source === 'manifest' || p.source === 'github' || p.source === 'none' ? p.source : 'none'
  return {
    source,
    manifestUrl: typeof p.manifestUrl === 'string' ? p.manifestUrl : '',
    githubRepo: typeof p.githubRepo === 'string' ? p.githubRepo : '',
    includePrerelease: typeof p.includePrerelease === 'boolean' ? p.includePrerelease : false,
    autoCheck: typeof p.autoCheck === 'boolean' ? p.autoCheck : true
  }
}

function parseTelemetry(raw: unknown): TelemetryConfig {
  const p = (raw ?? {}) as Partial<TelemetryConfig>
  return {
    enabled: typeof p.enabled === 'boolean' ? p.enabled : false,
    endpoint: typeof p.endpoint === 'string' ? p.endpoint : '',
    installId: typeof p.installId === 'string' ? p.installId : null
  }
}

function readLocal(): UiPrefs {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return { ...DEFAULTS, update: { ...DEFAULT_UPDATE }, telemetry: { ...DEFAULT_TELEMETRY } }
    }
    const parsed = JSON.parse(raw) as Partial<UiPrefs>
    return {
      onboardingDone: typeof parsed.onboardingDone === 'boolean' ? parsed.onboardingDone : false,
      startHidden: typeof parsed.startHidden === 'boolean' ? parsed.startHidden : false,
      migratedLoginItem: typeof parsed.migratedLoginItem === 'boolean' ? parsed.migratedLoginItem : false,
      launchNodeMode: parsed.launchNodeMode === 'smart' ? 'smart' : 'keep',
      update: parseUpdate(parsed.update),
      telemetry: parseTelemetry(parsed.telemetry)
    }
  } catch {
    return { ...DEFAULTS, update: { ...DEFAULT_UPDATE }, telemetry: { ...DEFAULT_TELEMETRY } }
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
