import type {
  AccelStatus,
  AutoAccelEvent,
  DiagnosticReport,
  EffectReport,
  EngineSettings,
  Game,
  LibraryScanResult,
  LogEntry,
  NodeRanking,
  RulesStatus,
  RulesUpdateResult,
  Server,
  Subscription,
  UpdateInfo
} from '@shared/types'
import type { Backend, SubMutation } from './backend'

// Per-method call timeouts (ms). Long-running backend operations get a larger budget; everything else
// uses the default. Without this a hung backend would leave the UI waiting forever.
const DEFAULT_TIMEOUT_MS = 30_000
const METHOD_TIMEOUTS: Record<string, number> = {
  'subs.update': 90_000,
  'servers.ping': 60_000,
  'nodes.rank': 60_000,
  'accel.start': 60_000,
  'diag.run': 30_000,
  'library.scan': 60_000,
  'rules.update': 30_000
}

// Module-level gate: nothing may spawn/elevate the sidecar until the app explicitly allows it. The
// onboarding flow shows an explanation of the UAC prompt first, so any store that eagerly connects
// (e.g. the accel store on the default route) must wait here instead of triggering elevation.
let releaseGate!: () => void
const gate = new Promise<void>((r) => (releaseGate = r))
let gateOpen = false

/** Opens the backend connection gate. Idempotent — safe to call from multiple places. */
export function allowBackendConnection(): void {
  if (gateOpen) return
  gateOpen = true
  releaseGate()
}

// Speaks the JSON-RPC/WebSocket protocol of the headless C# sidecar (see Netch.Host).
// Requests: {id, method, params} -> {id, result|error}. Events: {event, data}.
export class WebSocketBackend implements Backend {
  private ws: WebSocket | null = null
  private connecting: Promise<void> | null = null
  private readonly pending = new Map<string, { resolve: (v: unknown) => void; reject: (e: Error) => void }>()
  private seq = 0

  private readonly statusCbs = new Set<(s: AccelStatus) => void>()
  private readonly logCbs = new Set<(l: LogEntry) => void>()
  private readonly effectCbs = new Set<(r: EffectReport) => void>()
  private readonly autoAccelCbs = new Set<(e: AutoAccelEvent) => void>()
  private lastStatus: AccelStatus | null = null
  private lastEffect: EffectReport | null = null
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null

  constructor() {
    // Main tells us when the backend host died (crash, or an elevated host it can't watch via a child
    // handle). Drop our socket and reconnect — getConnection() re-spawns / re-elevates the host.
    window.gameplus?.backend?.onLost?.(() => this.forceReconnect())
  }

  private forceReconnect(): void {
    if (this.ws) {
      try {
        this.ws.close()
      } catch {
        // ignore
      }
      this.ws = null
    }
    // Don't clear `connecting`: an in-flight connect settles on its own (its error/close nulls it), and
    // clearing it here would let a concurrent ensure() open a second, leaked socket.
    this.onDisconnected()
  }

  private async ensure(): Promise<void> {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) return
    if (this.connecting) return this.connecting

    this.connecting = (async () => {
      // Wait for the app to allow connecting (onboarding may still be explaining the UAC prompt).
      await gate
      const { port, token } = await window.gameplus.backend.getConnection()
      await new Promise<void>((resolve, reject) => {
        const ws = new WebSocket(`ws://127.0.0.1:${port}/ws?token=${token}`)
        ws.addEventListener('open', () => {
          this.ws = ws
          resolve()
        })
        ws.addEventListener('error', () => reject(new Error('backend connection failed')))
        ws.addEventListener('close', () => {
          if (this.ws === ws) this.ws = null
          this.connecting = null
          // Fail any in-flight calls so callers don't hang.
          this.pending.forEach((p) => p.reject(new Error('backend disconnected')))
          this.pending.clear()
          this.onDisconnected()
        })
        ws.addEventListener('message', (ev) => this.onMessage(ev.data as string))
      })
    })()

    try {
      await this.connecting
    } finally {
      this.connecting = null
    }
  }

  /** On a dropped connection, stop showing a stale "accelerating" state and try to reconnect. */
  private onDisconnected(): void {
    if (this.lastStatus && this.lastStatus.state !== 'idle') {
      const idle: AccelStatus = {
        ...this.lastStatus,
        state: 'idle',
        latency: 0,
        loss: 0,
        jitter: 0,
        durationSec: 0,
        downloadBps: 0,
        uploadBps: 0,
        gameId: null,
        nodeId: null
      }
      this.lastStatus = idle
      this.statusCbs.forEach((cb) => cb(idle))
    }
    // A respawned sidecar comes up on a new port; ensure() re-fetches it. Retry while anyone is listening.
    if (this.reconnectTimer || (!this.statusCbs.size && !this.logCbs.size && !this.effectCbs.size)) return
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null
      if (!this.ws) this.ensure().catch(() => this.onDisconnected())
    }, 2500)
  }

  private onMessage(raw: string): void {
    let msg: {
      id?: string
      result?: unknown
      error?: string
      event?: string
      data?: unknown
    }
    try {
      msg = JSON.parse(raw)
    } catch {
      return
    }

    if (msg.event) {
      if (msg.event === 'accel.status') {
        this.lastStatus = msg.data as AccelStatus
        this.statusCbs.forEach((cb) => cb(this.lastStatus!))
      } else if (msg.event === 'log') {
        this.logCbs.forEach((cb) => cb(msg.data as LogEntry))
      } else if (msg.event === 'effect') {
        this.lastEffect = msg.data as EffectReport
        this.effectCbs.forEach((cb) => cb(this.lastEffect!))
      } else if (msg.event === 'auto.accel') {
        const e = msg.data as AutoAccelEvent
        this.autoAccelCbs.forEach((cb) => cb(e))
      }
      return
    }

    if (msg.id && this.pending.has(msg.id)) {
      const p = this.pending.get(msg.id)!
      this.pending.delete(msg.id)
      msg.error ? p.reject(new Error(msg.error)) : p.resolve(msg.result)
    }
  }

  private async call<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    await this.ensure()
    const id = String(++this.seq)
    const timeoutMs = METHOD_TIMEOUTS[method] ?? DEFAULT_TIMEOUT_MS
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        if (this.pending.delete(id)) {
          reject(new Error(`请求超时：${method}`))
        }
      }, timeoutMs)
      this.pending.set(id, {
        resolve: (v: unknown) => {
          clearTimeout(timer)
          resolve(v as T)
        },
        reject: (e: Error) => {
          clearTimeout(timer)
          reject(e)
        }
      })
      this.ws!.send(JSON.stringify({ id, method, params }))
    })
  }

  listGames(): Promise<Game[]> {
    return this.call('games.list')
  }
  listServers(): Promise<Server[]> {
    return this.call('servers.list')
  }
  listSubscriptions(): Promise<Subscription[]> {
    return this.call('subs.list')
  }
  pingAll(): Promise<Server[]> {
    return this.call('servers.ping')
  }
  rankNodes(gameId: string | null, ping: boolean): Promise<NodeRanking> {
    return this.call('nodes.rank', { gameId, ping })
  }
  async startAccelerate(gameId: string, nodeId: string | null): Promise<void> {
    await this.call('accel.start', { gameId, nodeId })
  }
  async stopAccelerate(): Promise<void> {
    await this.call('accel.stop')
  }

  onStatus(cb: (s: AccelStatus) => void): () => void {
    this.statusCbs.add(cb)
    if (this.lastStatus) cb(this.lastStatus)
    // Make sure we're connected so events start flowing even before the first RPC.
    this.ensure().catch(() => this.onDisconnected())
    return () => this.statusCbs.delete(cb)
  }
  onLog(cb: (l: LogEntry) => void): () => void {
    this.logCbs.add(cb)
    this.ensure().catch(() => this.onDisconnected())
    return () => this.logCbs.delete(cb)
  }
  recentLogs(): Promise<LogEntry[]> {
    return this.call('logs.recent')
  }

  getEffect(): Promise<EffectReport> {
    return this.call('effect.get')
  }
  onEffect(cb: (r: EffectReport) => void): () => void {
    this.effectCbs.add(cb)
    if (this.lastEffect) cb(this.lastEffect)
    this.ensure().catch(() => this.onDisconnected())
    return () => this.effectCbs.delete(cb)
  }

  onAutoAccel(cb: (e: AutoAccelEvent) => void): () => void {
    this.autoAccelCbs.add(cb)
    // Make sure we're connected so events start flowing even before the first RPC.
    this.ensure().catch(() => this.onDisconnected())
    return () => this.autoAccelCbs.delete(cb)
  }

  addServerFromLink(link: string): Promise<Server[]> {
    return this.call('servers.add', { link })
  }
  removeServer(id: string): Promise<Server[]> {
    return this.call('servers.remove', { id })
  }
  addSubscription(remark: string, link: string): Promise<Subscription[]> {
    return this.call('subs.add', { remark, link })
  }
  removeSubscription(id: string): Promise<SubMutation> {
    return this.call('subs.remove', { id })
  }
  updateSubscription(id: string): Promise<SubMutation> {
    return this.call('subs.update', { id })
  }
  toggleFavorite(id: string): Promise<Game[]> {
    return this.call('games.toggleFavorite', { id })
  }
  setGameAuto(id: string, enabled: boolean): Promise<Game[]> {
    return this.call('games.setAuto', { id, enabled })
  }
  addGame(data: Partial<Game>): Promise<Game[]> {
    return this.call('games.add', data)
  }
  updateGame(id: string, data: Partial<Game>): Promise<Game[]> {
    return this.call('games.update', { ...data, id })
  }
  removeGame(id: string): Promise<Game[]> {
    return this.call('games.remove', { id })
  }
  exportServers(): Promise<string[]> {
    return this.call('servers.export')
  }
  getSettings(): Promise<EngineSettings> {
    return this.call('settings.get')
  }
  setSettings(patch: Partial<EngineSettings>): Promise<EngineSettings> {
    return this.call('settings.set', patch)
  }
  checkUpdate(): Promise<UpdateInfo> {
    return this.call('update.check')
  }

  runDiagnostics(opts: {
    gameId?: string | null
    nodeId?: string | null
    error?: string | null
  }): Promise<DiagnosticReport> {
    return this.call('diag.run', {
      gameId: opts.gameId ?? null,
      nodeId: opts.nodeId ?? null,
      error: opts.error ?? null
    })
  }

  fixDiagnostic(action: string): Promise<{ fixed: boolean }> {
    return this.call('diag.fix', { action })
  }

  scanLibrary(): Promise<LibraryScanResult> {
    return this.call('library.scan')
  }

  addDetectedGame(name: string, executables: string[], source?: string): Promise<Game[]> {
    return this.call('library.addDetected', { name, executables, source: source ?? null })
  }

  rulesStatus(): Promise<RulesStatus> {
    return this.call('rules.status')
  }

  updateRules(): Promise<RulesUpdateResult> {
    return this.call('rules.update')
  }
}
