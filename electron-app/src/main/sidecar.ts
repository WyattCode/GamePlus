import { spawn, type ChildProcess } from 'child_process'
import { existsSync, mkdirSync, readFileSync, rmSync } from 'fs'
import { connect } from 'net'
import { join } from 'path'
import { app, BrowserWindow } from 'electron'
import { is } from '@electron-toolkit/utils'

export interface BackendConnection {
  port: number
  token: string
  /** Host process id — used to poll liveness (elevated host has no child handle). */
  pid?: number
}

let child: ChildProcess | null = null
let connPromise: Promise<BackendConnection> | null = null
let healthTimer: ReturnType<typeof setInterval> | null = null
let hostGone = false
let shuttingDown = false
let lastElevateAt = 0

function sidecarPath(): string {
  if (is.dev) {
    return join(
      app.getAppPath(),
      '..',
      'NetchModern',
      'src',
      'Netch.Host',
      'bin',
      'Debug',
      'net10.0-windows',
      'gameplus-host.exe'
    )
  }
  return join(process.resourcesPath, 'backend', 'gameplus-host.exe')
}

/**
 * Liveness probe: try a short TCP connect to the host's loopback port. Success = alive; refused/timeout =
 * dead. This checks the actual thing the renderer depends on (the listening port), so it is immune to PID
 * reuse — unlike polling a bare PID, where Windows could reassign a dead host's PID to another process.
 */
function checkPortAlive(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const sock = connect({ port, host: '127.0.0.1' })
    let settled = false
    const done = (alive: boolean): void => {
      if (settled) return
      settled = true
      sock.destroy()
      resolve(alive)
    }
    sock.setTimeout(1500)
    sock.once('connect', () => done(true))
    sock.once('timeout', () => done(false))
    sock.once('error', () => done(false))
  })
}

function notifyRenderer(channel: string): void {
  for (const w of BrowserWindow.getAllWindows()) if (!w.isDestroyed()) w.webContents.send(channel)
}

function stopHealthMonitor(): void {
  if (healthTimer) {
    clearInterval(healthTimer)
    healthTimer = null
  }
}

/** Poll host liveness for an elevated host we have no child handle for. A direct child instead uses its
 *  authoritative 'exit' event (below), so we skip TCP polling when `child` is set — that avoids a transient
 *  connect blip false-positively declaring a live child dead. Three consecutive failures before declaring
 *  death gives ~9s of grace so only a truly hung/dead host trips it. */
function startHealthMonitor(port?: number): void {
  stopHealthMonitor()
  hostGone = false
  if (!port || child) return
  let strikes = 0
  healthTimer = setInterval(async () => {
    if (await checkPortAlive(port)) {
      strikes = 0
      return
    }
    if (++strikes >= 3) onHostGone()
  }, 3000)
}

/** Fires once per connection, from either the child 'exit' event or the PID poll. */
function onHostGone(): void {
  if (hostGone) return
  hostGone = true
  stopHealthMonitor()
  child = null
  connPromise = null
  if (shuttingDown) return
  console.warn('[sidecar] backend host gone; asking renderer to reconnect')
  // The renderer drops its (possibly-hung) socket and calls getConnection again, which re-spawns
  // (dev) or re-elevates (prod, throttled) the host and hands back the new port/token.
  notifyRenderer('backend:lost')
}

/**
 * Spawns the C# backend and resolves with its {port, token}. Idempotent.
 * Dev runs it non-elevated (frictionless). Production elevates it via UAC so capture-mode games
 * (TUN / netfilter drivers need admin) work — falling back to a non-elevated run if the user declines.
 * A health monitor watches the host and drives reconnection if it dies.
 */
export function startSidecar(): Promise<BackendConnection> {
  // Never resurrect the backend once teardown has begun — a late reconnect during quit would spawn an
  // orphaned host (and possibly re-enable the system proxy) after cleanup already ran.
  if (shuttingDown) return Promise.reject(new Error('app is shutting down'))
  if (connPromise) return connPromise
  const p = (is.dev ? spawnDirect() : spawnElevatedWithFallback())
    .then((c) => {
      startHealthMonitor(c.port)
      return c
    })
    .catch((e) => {
      if (connPromise === p) connPromise = null
      throw e
    })
  connPromise = p
  return p
}

/** Non-elevated spawn with a stdout handshake (dev, and the fallback when UAC is declined). */
function spawnDirect(): Promise<BackendConnection> {
  return new Promise<BackendConnection>((resolve, reject) => {
    let proc: ChildProcess
    try {
      proc = spawn(sidecarPath(), [], { stdio: ['pipe', 'pipe', 'pipe'] })
    } catch (e) {
      reject(e as Error)
      return
    }
    child = proc

    let buf = ''
    let settled = false
    proc.stdout?.on('data', (d: Buffer) => {
      buf += d.toString()
      const line = buf.split(/\r?\n/).find((l) => l.startsWith('GAMEPLUS_READY'))
      if (line && !settled) {
        settled = true
        try {
          resolve(JSON.parse(line.slice('GAMEPLUS_READY '.length)))
        } catch (e) {
          reject(e as Error)
        }
      }
    })
    proc.stderr?.on('data', (d: Buffer) => console.error('[sidecar]', d.toString().trimEnd()))
    proc.on('error', (e) => !settled && ((settled = true), reject(e)))
    proc.on('exit', (code) => {
      console.log('[sidecar] exited', code)
      // If it died before the handshake, fail startup fast instead of hanging until the 15s timeout.
      if (!settled) {
        settled = true
        reject(new Error(`sidecar exited during startup (code ${code})`))
      }
      onHostGone()
    })
    setTimeout(() => !settled && ((settled = true), reject(new Error('sidecar start timeout'))), 15000)
  })
}

/** Elevated spawn: UAC-launch the host, read {port,token,pid} from a file it writes. */
async function spawnElevatedWithFallback(): Promise<BackendConnection> {
  // Throttle UAC: never prompt more than once per 8s, so a crash-looping host can't spam the dialog.
  const sinceLast = Date.now() - lastElevateAt
  if (sinceLast < 8000) throw new Error('elevation throttled')
  lastElevateAt = Date.now()

  const exe = sidecarPath()
  const dir = join(app.getPath('temp'), 'gameplus')
  // Unique per launch (nonce, not just pid) so a stale file from a prior run — even one that reused this
  // pid — can never be mistaken for this launch's handshake.
  const nonce = Math.random().toString(36).slice(2, 10)
  const connFile = join(dir, `conn-${process.pid}-${nonce}.json`)
  try {
    mkdirSync(dir, { recursive: true })
    if (existsSync(connFile)) rmSync(connFile)
  } catch {
    // ignore
  }

  // Start-Process -Verb RunAs triggers the UAC prompt; the elevated host writes connFile then serves.
  const psCommand =
    `Start-Process -FilePath "${exe}" -Verb RunAs -WindowStyle Hidden ` +
    `-ArgumentList '--conn-file','${connFile}','--parent-pid','${process.pid}'`

  let launcherExited: number | null = null
  const launcher = spawn(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', psCommand],
    { stdio: ['ignore', 'ignore', 'pipe'] }
  )
  launcher.stderr?.on('data', (d: Buffer) => console.error('[uac]', d.toString().trimEnd()))
  launcher.on('exit', (code) => (launcherExited = code ?? 0))
  child = null // the elevated host is not a child we can signal; lifecycle is bound to our PID

  // Poll for the handshake file for up to 40s (user may sit on the UAC dialog).
  const deadline = Date.now() + 40_000
  while (Date.now() < deadline) {
    if (existsSync(connFile)) {
      try {
        const data = JSON.parse(readFileSync(connFile, 'utf8')) as BackendConnection
        try {
          rmSync(connFile) // don't leave the token sitting in temp
        } catch {
          // ignore
        }
        return data
      } catch {
        // file mid-write; retry
      }
    }
    // launcher exited non-zero before the file appeared → UAC declined / failed → fall back.
    if (launcherExited !== null && launcherExited !== 0 && !existsSync(connFile)) {
      console.warn('[sidecar] elevation declined; falling back to non-elevated (domain games only)')
      return spawnDirect()
    }
    await new Promise((r) => setTimeout(r, 300))
  }
  throw new Error('elevated sidecar start timeout')
}

/**
 * Graceful teardown. For a direct child we must let it run its own Cleanup (restore the system proxy,
 * stop xray/cores) — so we close stdin (its EOF-watch triggers Cleanup) and WAIT for it to exit, only
 * force-killing if it overstays. Killing it outright (TerminateProcess) would skip Cleanup and strand the
 * system proxy enabled. The elevated host has no child handle; it self-cleans when it sees our PID gone.
 */
export function stopSidecar(): Promise<void> {
  shuttingDown = true
  stopHealthMonitor()
  connPromise = null
  const c = child
  child = null
  if (!c) return Promise.resolve()

  return new Promise<void>((resolve) => {
    let done = false
    const finish = (): void => {
      if (done) return
      done = true
      resolve()
    }
    c.once('exit', finish)
    try {
      c.stdin?.end()
    } catch {
      // ignore
    }
    // Cleanup (StopAsync up to 6s + proxy restore) usually finishes in 1–2s; cap the wait generously, then
    // force-kill as a last resort. The cap must comfortably exceed the host's own 6s StopAsync bound.
    setTimeout(() => {
      try {
        if (!done) c.kill()
      } catch {
        // ignore
      }
      finish()
    }, 12000)
  })
}
