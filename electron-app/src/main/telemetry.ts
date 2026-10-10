import { app, dialog, ipcMain, shell } from 'electron'
import { randomUUID } from 'crypto'
import {
  appendFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync
} from 'fs'
import { join } from 'path'
import { getPrefs, setPrefs } from './prefs'
import {
  formatCrashReport,
  isAllowedEndpoint,
  redactCrashText,
  sanitizeEvent,
  MAX_CRASH_BYTES
} from './telemetry-core'
import type { CrashFileInfo, TelemetryConfig, TelemetryEvent } from '../shared/types'

// Client-side telemetry. OFF by default: nothing is collected locally and nothing is uploaded unless
// the user opts in AND an endpoint is configured. Local crash files are always written (they never
// leave the machine unless uploaded under the same opt-in rule).

const MAX_EVENTS_PER_BATCH = 500
const MAX_EVENT_LOG_BYTES = 1024 * 1024
const UPLOAD_TIMEOUT_MS = 10_000
const FLUSH_INITIAL_DELAY_MS = 60_000
const FLUSH_INTERVAL_MS = 30 * 60_000
const KEEP_CRASHES = 20

let flushTimer: NodeJS.Timeout | null = null
let flushInterval: NodeJS.Timeout | null = null
let crashDialogShown = false

function userData(): string {
  return app.getPath('userData')
}

function eventsPath(): string {
  return join(userData(), 'telemetry', 'events.jsonl')
}

function eventsRotatedPath(): string {
  return join(userData(), 'telemetry', 'events.1.jsonl')
}

function eventsSendingPath(): string {
  return join(userData(), 'telemetry', 'events.sending.jsonl')
}

function installIdPath(): string {
  return join(userData(), 'telemetry-id')
}

function crashesDir(): string {
  return join(userData(), 'crashes')
}

function sentDir(): string {
  return join(userData(), 'crashes', 'sent')
}

/** Host crash folder: %AppData%\Game+\crashes on Windows. */
function hostCrashesDir(): string {
  return join(app.getPath('appData'), 'Game+', 'crashes')
}

function appVersion(): string {
  try {
    return app.getVersion()
  } catch {
    return '0.0.0'
  }
}

function userName(): string | null {
  try {
    return process.env.USERNAME ?? process.env.USER ?? null
  } catch {
    return null
  }
}

function userProfile(): string | null {
  try {
    return app.getPath('home')
  } catch {
    return null
  }
}

// --- Config ---

/** Returns the current telemetry config plus the install id (only when enabled). */
export function getTelemetryConfig(): TelemetryConfig {
  const t = getPrefs().telemetry
  return {
    enabled: t.enabled,
    endpoint: t.endpoint,
    installId: t.enabled ? readInstallId() : null
  }
}

/** Merges a validated patch into the telemetry prefs. */
export function setTelemetryConfig(patch: Partial<TelemetryConfig>): TelemetryConfig {
  const clean: Partial<TelemetryConfig> = {}
  if (typeof patch.enabled === 'boolean') clean.enabled = patch.enabled
  if (typeof patch.endpoint === 'string' && isAllowedEndpoint(patch.endpoint)) clean.endpoint = patch.endpoint
  setPrefs({ telemetry: clean as TelemetryConfig })
  return getTelemetryConfig()
}

// --- Install id ---

function readInstallId(): string | null {
  try {
    const p = installIdPath()
    if (!existsSync(p)) return null
    const id = readFileSync(p, 'utf8').trim()
    return id.length > 0 ? id : null
  } catch {
    return null
  }
}

/** Returns the install id, creating it lazily on first call. */
function ensureInstallId(): string | null {
  const existing = readInstallId()
  if (existing) return existing
  try {
    const id = randomUUID()
    mkdirSync(userData(), { recursive: true })
    writeFileSync(installIdPath(), id, 'utf8')
    return id
  } catch {
    return null
  }
}

/** Deletes the stored install id (used by clear()). */
export function resetInstallId(): void {
  try {
    const p = installIdPath()
    if (existsSync(p)) rmSync(p)
  } catch {
    // ignore
  }
}

// --- Event log ---

function rotateIfNeeded(): void {
  try {
    const p = eventsPath()
    if (!existsSync(p)) return
    if (statSync(p).size <= MAX_EVENT_LOG_BYTES) return
    const rotated = eventsRotatedPath()
    if (existsSync(rotated)) rmSync(rotated)
    renameSync(p, rotated)
  } catch {
    // ignore
  }
}

/** Records a whitelisted event locally. No-op when telemetry is disabled. */
export function recordEvent(raw: unknown): void {
  if (!getPrefs().telemetry.enabled) return
  const ev = sanitizeEvent(raw, userName())
  if (!ev) return
  const line = JSON.stringify({ ...ev, ts: new Date().toISOString(), v: appVersion() })
  try {
    mkdirSync(join(userData(), 'telemetry'), { recursive: true })
    rotateIfNeeded()
    appendFileSync(eventsPath(), line + '\n', 'utf8')
  } catch {
    // ignore
  }
}

/** Returns the most recent events from the local log (newest last), capped at `limit`. */
export function getRecentEvents(limit: number): TelemetryEvent[] {
  const cap = Math.max(0, Math.min(200, Math.floor(limit)))
  if (cap === 0) return []
  try {
    const p = eventsPath()
    if (!existsSync(p)) return []
    const lines = readFileSync(p, 'utf8').split('\n').filter((l) => l.trim().length > 0)
    const tail = lines.slice(-cap)
    const out: TelemetryEvent[] = []
    for (const l of tail) {
      try {
        out.push(JSON.parse(l) as TelemetryEvent)
      } catch {
        // skip malformed line
      }
    }
    return out
  } catch {
    return []
  }
}

/** Deletes the local event log and resets the install id. */
export function clearTelemetry(): void {
  try {
    const p = eventsPath()
    if (existsSync(p)) rmSync(p)
    const r = eventsRotatedPath()
    if (existsSync(r)) rmSync(r)
    const s = eventsSendingPath()
    if (existsSync(s)) rmSync(s)
  } catch {
    // ignore
  }
  resetInstallId()
}

// --- Crash capture ---

/** Writes a crash report locally (always on) and records a crash event (only when enabled). */
export function writeCrash(source: string, error: unknown, extra?: string): string | null {
  try {
    mkdirSync(crashesDir(), { recursive: true })
    const now = new Date()
    const name = `crash-main-${now.toISOString().replace(/[:.]/g, '-')}.txt`
    const path = join(crashesDir(), name)
    const text = formatCrashReport({
      source,
      version: appVersion(),
      platform: process.platform,
      arch: process.arch,
      extra: extra ?? null,
      error,
      now
    })
    const redacted = redactCrashText(text, userProfile(), userName())
    writeFileSync(path, redacted, 'utf8')
    pruneCrashes()
    return path
  } catch {
    return null
  }
}

function pruneCrashes(): void {
  try {
    const files = readdirSync(crashesDir())
      .filter((f) => f.startsWith('crash-main-') && f.endsWith('.txt'))
      .map((f) => join(crashesDir(), f))
    if (files.length <= KEEP_CRASHES) return
    files.sort()
    const remove = files.length - KEEP_CRASHES
    for (let i = 0; i < remove; i++) {
      try {
        rmSync(files[i])
      } catch {
        // ignore
      }
    }
  } catch {
    // ignore
  }
}

/** Installs process-level crash handlers. Call before app.whenReady(). */
export function installCrashHandlers(): void {
  process.on('uncaughtException', (err) => {
    writeCrash('uncaughtException', err)
    recordEvent({ type: 'crash', where: 'main', reason: err instanceof Error ? err.message : String(err) })
    console.error('uncaughtException:', err)
    if (!crashDialogShown) {
      crashDialogShown = true
      try {
        dialog.showErrorBox('Game+ 出现错误', '已在本地保存崩溃报告。')
      } catch {
        // ignore
      }
    }
  })

  process.on('unhandledRejection', (reason) => {
    writeCrash('unhandledRejection', reason)
    recordEvent({ type: 'crash', where: 'main', reason: reason instanceof Error ? reason.message : String(reason) })
    console.error('unhandledRejection:', reason)
  })

  app.on('render-process-gone', (_e, _wc, details) => {
    writeCrash('render-process-gone', new Error(details.reason), `exitCode=${details.exitCode}`)
    recordEvent({ type: 'crash', where: 'renderer', reason: details.reason })
  })

  app.on('child-process-gone', (_e, details) => {
    writeCrash('child-process-gone', new Error(details.reason), `type=${details.type} exitCode=${details.exitCode}`)
    recordEvent({ type: 'crash', where: 'child', reason: details.reason })
  })
}

// --- Crash file listing ---

/** Lists crash files from both the main and host crash folders. */
export function listCrashFiles(): CrashFileInfo[] {
  const out: CrashFileInfo[] = []
  collectCrashes(crashesDir(), 'main', out)
  collectCrashes(hostCrashesDir(), 'host', out)
  out.sort((a, b) => b.mtime - a.mtime)
  return out
}

function collectCrashes(dir: string, where: 'main' | 'host', out: CrashFileInfo[]): void {
  try {
    if (!existsSync(dir)) return
    for (const name of readdirSync(dir)) {
      if (!name.startsWith('crash-') || !name.endsWith('.txt')) continue
      try {
        const st = statSync(join(dir, name))
        if (!st.isFile()) continue
        out.push({ name, where, size: st.size, mtime: st.mtimeMs })
      } catch {
        // ignore
      }
    }
  } catch {
    // ignore
  }
}

/** Opens the main crash folder in the OS file manager. */
export async function openCrashFolder(): Promise<void> {
  try {
    mkdirSync(crashesDir(), { recursive: true })
    await shell.openPath(crashesDir())
  } catch {
    // ignore
  }
}

// --- Uploader ---

/** Uploads pending events and crash files. No-op when disabled or no endpoint is configured. */
export async function flush(): Promise<void> {
  const t = getPrefs().telemetry
  if (!t.enabled || t.endpoint.length === 0) return
  const installId = ensureInstallId()
  if (!installId) return

  await uploadEvents(t.endpoint, installId)
  await uploadCrashes(t.endpoint, installId)
}

async function uploadEvents(endpoint: string, installId: string): Promise<void> {
  const p = eventsPath()
  if (!existsSync(p)) return
  const sending = eventsSendingPath()
  try {
    if (existsSync(sending)) rmSync(sending)
    renameSync(p, sending)
  } catch {
    return
  }

  let lines: string[] = []
  try {
    lines = readFileSync(sending, 'utf8').split('\n').filter((l) => l.trim().length > 0)
  } catch {
    return
  }
  if (lines.length === 0) {
    try { rmSync(sending) } catch { /* ignore */ }
    return
  }

  const batch = lines.slice(0, MAX_EVENTS_PER_BATCH)
  const rest = lines.slice(MAX_EVENTS_PER_BATCH)

  const events: TelemetryEvent[] = []
  for (const l of batch) {
    try {
      events.push(JSON.parse(l) as TelemetryEvent)
    } catch {
      // skip malformed
    }
  }

  const body = JSON.stringify({
    installId,
    platform: process.platform,
    arch: process.arch,
    version: appVersion(),
    events
  })

  const ok = await postJson(endpoint, body)
  if (ok) {
    // Keep any lines that were appended after we renamed the file, plus the overflow batch.
    try {
      const appended = existsSync(p) ? readFileSync(p, 'utf8') : ''
      const merged = rest.join('\n') + (rest.length > 0 ? '\n' : '') + appended
      if (merged.length > 0) writeFileSync(p, merged, 'utf8')
      rmSync(sending)
    } catch {
      // ignore
    }
  } else {
    // Restore the sending file so the next flush retries.
    try {
      const appended = existsSync(p) ? readFileSync(p, 'utf8') : ''
      writeFileSync(sending, rest.join('\n') + (rest.length > 0 ? '\n' : '') + appended, 'utf8')
      if (existsSync(p)) rmSync(p)
      renameSync(sending, p)
    } catch {
      // ignore
    }
  }
}

async function uploadCrashes(endpoint: string, installId: string): Promise<void> {
  const dirs: Array<{ dir: string; where: 'main' | 'host' }> = [
    { dir: crashesDir(), where: 'main' },
    { dir: hostCrashesDir(), where: 'host' }
  ]
  for (const { dir } of dirs) {
    try {
      if (!existsSync(dir)) continue
      for (const name of readdirSync(dir)) {
        if (!name.startsWith('crash-') || !name.endsWith('.txt')) continue
        const full = join(dir, name)
        let text: string
        try {
          const st = statSync(full)
          if (!st.isFile() || st.size > MAX_CRASH_BYTES) continue
          text = readFileSync(full, 'utf8')
        } catch {
          continue
        }
        const body = JSON.stringify({
          installId,
          platform: process.platform,
          arch: process.arch,
          version: appVersion(),
          crash: { name, text }
        })
        const ok = await postJson(endpoint, body)
        if (ok) {
          try {
            mkdirSync(sentDir(), { recursive: true })
            renameSync(full, join(sentDir(), name))
          } catch {
            try { rmSync(full) } catch { /* ignore */ }
          }
        }
      }
    } catch {
      // ignore
    }
  }
}

async function postJson(endpoint: string, body: string): Promise<boolean> {
  try {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), UPLOAD_TIMEOUT_MS)
    try {
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body,
        signal: controller.signal
      })
      return res.ok
    } finally {
      clearTimeout(timer)
    }
  } catch {
    return false
  }
}

// --- Lifecycle ---

/** Starts the periodic flush timer. Call after app.whenReady(). */
export function startTelemetry(): void {
  if (getPrefs().telemetry.enabled) {
    recordEvent({ type: 'app_start', version: appVersion() })
  }
  if (flushTimer) clearTimeout(flushTimer)
  if (flushInterval) clearInterval(flushInterval)
  flushTimer = setTimeout(() => {
    void flush()
  }, FLUSH_INITIAL_DELAY_MS)
  flushInterval = setInterval(() => {
    void flush()
  }, FLUSH_INTERVAL_MS)
}

/** Stops the flush timers (called on quit). */
export function stopTelemetry(): void {
  if (flushTimer) {
    clearTimeout(flushTimer)
    flushTimer = null
  }
  if (flushInterval) {
    clearInterval(flushInterval)
    flushInterval = null
  }
}

// --- IPC ---

/** Registers the telemetry IPC handlers. */
export function registerTelemetryIpc(): void {
  ipcMain.handle('telemetry:get-config', () => getTelemetryConfig())
  ipcMain.handle('telemetry:set-config', (_e, patch: unknown) => {
    if (!patch || typeof patch !== 'object') return getTelemetryConfig()
    return setTelemetryConfig(patch as Partial<TelemetryConfig>)
  })
  ipcMain.handle('telemetry:record', (_e, event: unknown) => {
    recordEvent(event)
  })
  ipcMain.handle('telemetry:get-recent', (_e, limit: unknown) => {
    const n = typeof limit === 'number' && Number.isFinite(limit) ? limit : 100
    return getRecentEvents(n)
  })
  ipcMain.handle('telemetry:clear', () => {
    clearTelemetry()
  })
  ipcMain.handle('telemetry:list-crashes', () => listCrashFiles())
  ipcMain.handle('telemetry:open-crash-folder', () => openCrashFolder())
}
