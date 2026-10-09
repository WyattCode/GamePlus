import type {
  AccelStatus,
  EngineSettings,
  Game,
  LogEntry,
  Server,
  Subscription,
  UpdateInfo
} from '@shared/types'
import type { Backend, SubMutation } from './backend'

// Speaks the JSON-RPC/WebSocket protocol of the headless C# sidecar (see Netch.Host).
// Requests: {id, method, params} -> {id, result|error}. Events: {event, data}.
export class WebSocketBackend implements Backend {
  private ws: WebSocket | null = null
  private connecting: Promise<void> | null = null
  private readonly pending = new Map<string, { resolve: (v: unknown) => void; reject: (e: Error) => void }>()
  private seq = 0

  private readonly statusCbs = new Set<(s: AccelStatus) => void>()
  private readonly logCbs = new Set<(l: LogEntry) => void>()
  private lastStatus: AccelStatus | null = null
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
    if (this.reconnectTimer || (!this.statusCbs.size && !this.logCbs.size)) return
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
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject })
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
}
