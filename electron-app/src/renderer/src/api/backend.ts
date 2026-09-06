import type {
  AccelStatus,
  EngineSettings,
  Game,
  LogEntry,
  Server,
  Subscription,
  UpdateInfo
} from '@shared/types'
import { sampleGames, sampleServers, sampleSubscriptions } from '@/data/sampleData'
import { WebSocketBackend } from './ws-backend'

export interface SubMutation {
  subscriptions: Subscription[]
  servers: Server[]
}

// Abstraction over the backend. Today it returns seeded data and a simulated accel loop;
// the real implementation will speak JSON-RPC/WebSocket to the headless C# Netch.Core sidecar.
// Keeping this interface stable means views never change when the transport is swapped.
export interface Backend {
  listGames(): Promise<Game[]>
  listServers(): Promise<Server[]>
  listSubscriptions(): Promise<Subscription[]>
  pingAll(): Promise<Server[]>
  startAccelerate(gameId: string, nodeId: string | null): Promise<void>
  stopAccelerate(): Promise<void>
  onStatus(cb: (s: AccelStatus) => void): () => void
  onLog(cb: (l: LogEntry) => void): () => void
  addServerFromLink(link: string): Promise<Server[]>
  removeServer(id: string): Promise<Server[]>
  addSubscription(remark: string, link: string): Promise<Subscription[]>
  removeSubscription(id: string): Promise<SubMutation>
  updateSubscription(id: string): Promise<SubMutation>
  toggleFavorite(id: string): Promise<Game[]>
  addGame(data: Partial<Game>): Promise<Game[]>
  updateGame(id: string, data: Partial<Game>): Promise<Game[]>
  removeGame(id: string): Promise<Game[]>
  getSettings(): Promise<EngineSettings>
  setSettings(patch: Partial<EngineSettings>): Promise<EngineSettings>
  checkUpdate(): Promise<UpdateInfo | null>
}

function jitter(base: number): number {
  return Math.max(1, Math.round(base + (Math.random() - 0.5) * base * 0.25))
}

class MockBackend implements Backend {
  private statusCbs = new Set<(s: AccelStatus) => void>()
  private logCbs = new Set<(l: LogEntry) => void>()
  private timer: ReturnType<typeof setInterval> | null = null
  private startedAt = 0
  private status: AccelStatus = {
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

  async listGames(): Promise<Game[]> {
    return structuredClone(this.mockGames)
  }
  async listServers(): Promise<Server[]> {
    return structuredClone(this.mockServers)
  }
  async listSubscriptions(): Promise<Subscription[]> {
    return structuredClone(this.mockSubs)
  }

  async pingAll(): Promise<Server[]> {
    await new Promise((r) => setTimeout(r, 500))
    this.mockServers = this.mockServers.map((s) => ({ ...s, delay: jitter(s.delay || 90) }))
    return structuredClone(this.mockServers)
  }

  async startAccelerate(gameId: string, nodeId: string | null): Promise<void> {
    this.log('info', `开始加速 ${gameId} via ${nodeId ?? '智能选路'}`)
    this.setState('connecting')
    await new Promise((r) => setTimeout(r, 800))
    this.startedAt = Date.now()
    this.status = { ...this.status, gameId, nodeId, state: 'accelerating', loss: 0 }
    this.emit()
    this.timer = setInterval(() => this.tick(), 1000)
    this.log('info', '加速已启动')
  }

  async stopAccelerate(): Promise<void> {
    this.setState('stopping')
    if (this.timer) clearInterval(this.timer)
    this.timer = null
    await new Promise((r) => setTimeout(r, 300))
    this.status = {
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
    this.emit()
    this.log('info', '加速已停止')
  }

  onStatus(cb: (s: AccelStatus) => void): () => void {
    this.statusCbs.add(cb)
    cb(this.status)
    return () => this.statusCbs.delete(cb)
  }
  onLog(cb: (l: LogEntry) => void): () => void {
    this.logCbs.add(cb)
    return () => this.logCbs.delete(cb)
  }

  private mockServers: Server[] = structuredClone(sampleServers)
  private mockSubs: Subscription[] = structuredClone(sampleSubscriptions)
  private mockGames: Game[] = structuredClone(sampleGames)
  private mockSettings: EngineSettings = {
    theme: 'light',
    language: 'System',
    minimizeToTray: true
  }

  async addServerFromLink(link: string): Promise<Server[]> {
    this.mockServers.push({
      id: crypto.randomUUID(),
      kind: 'ss',
      type: 'SS',
      remark: link.slice(0, 16) || '导入节点',
      hostname: 'mock.example.net',
      port: 8388,
      group: 'NONE',
      delay: -1
    })
    return structuredClone(this.mockServers)
  }
  async removeServer(id: string): Promise<Server[]> {
    this.mockServers = this.mockServers.filter((s) => s.id !== id)
    return structuredClone(this.mockServers)
  }
  async addSubscription(remark: string, link: string): Promise<Subscription[]> {
    this.mockSubs.push({
      id: crypto.randomUUID(),
      remark: remark || '未命名订阅',
      link,
      serverCount: 0,
      lastUpdated: null
    })
    return structuredClone(this.mockSubs)
  }
  async removeSubscription(id: string): Promise<{ subscriptions: Subscription[]; servers: Server[] }> {
    this.mockSubs = this.mockSubs.filter((s) => s.id !== id)
    return { subscriptions: structuredClone(this.mockSubs), servers: structuredClone(this.mockServers) }
  }
  async updateSubscription(id: string): Promise<{ subscriptions: Subscription[]; servers: Server[] }> {
    const s = this.mockSubs.find((x) => x.id === id)
    if (s) s.lastUpdated = new Date().toISOString()
    return { subscriptions: structuredClone(this.mockSubs), servers: structuredClone(this.mockServers) }
  }
  async toggleFavorite(id: string): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (g) g.favorite = !g.favorite
    return structuredClone(this.mockGames)
  }
  async addGame(data: Partial<Game>): Promise<Game[]> {
    const id = 'custom-' + crypto.randomUUID().slice(0, 8)
    this.mockGames.push({
      id,
      name: data.name || '未命名游戏',
      icon: data.icon || '',
      accent: data.accent || '',
      regionText: (data.regions || []).join(' · '),
      ruleCount: (data.proxyDomains?.length || 0) + (data.proxyIps?.length || 0),
      categories: [data.category || 'PC', ...(data.platforms || [])],
      favorite: false,
      custom: true,
      englishName: data.englishName || '',
      aliases: data.aliases || [],
      category: data.category || 'PC',
      regions: data.regions || [],
      platforms: data.platforms || [],
      proxyDomains: data.proxyDomains || [],
      proxyIps: data.proxyIps || [],
      proxyProcesses: data.proxyProcesses || []
    })
    return structuredClone(this.mockGames)
  }
  async updateGame(id: string, data: Partial<Game>): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (g) Object.assign(g, data, { id })
    return structuredClone(this.mockGames)
  }
  async removeGame(id: string): Promise<Game[]> {
    this.mockGames = this.mockGames.filter((x) => x.id !== id)
    return structuredClone(this.mockGames)
  }
  async checkUpdate(): Promise<UpdateInfo | null> {
    return null
  }
  async getSettings(): Promise<EngineSettings> {
    return structuredClone(this.mockSettings)
  }
  async setSettings(patch: Partial<EngineSettings>): Promise<EngineSettings> {
    this.mockSettings = { ...this.mockSettings, ...patch }
    return structuredClone(this.mockSettings)
  }

  private tick(): void {
    this.status.durationSec = Math.floor((Date.now() - this.startedAt) / 1000)
    this.status.latency = jitter(72)
    this.status.jitter = Math.round(Math.random() * 8)
    this.status.loss = Math.random() < 0.9 ? 0 : Math.round(Math.random() * 2 * 10) / 10
    this.status.downloadBps = 40_000 + Math.random() * 120_000
    this.status.uploadBps = 8_000 + Math.random() * 30_000
    this.emit()
  }

  private setState(state: AccelStatus['state']): void {
    this.status.state = state
    this.emit()
  }
  private emit(): void {
    const snap = structuredClone(this.status)
    this.statusCbs.forEach((cb) => cb(snap))
  }
  private log(level: LogEntry['level'], message: string): void {
    const entry: LogEntry = { ts: new Date().toISOString(), level, message }
    this.logCbs.forEach((cb) => cb(entry))
  }
}

// In Electron the real C# sidecar is available; in a plain browser (vite preview) fall back to the mock.
const inElectron = typeof window !== 'undefined' && !!window.gameplus?.backend
export const backend: Backend = inElectron ? new WebSocketBackend() : new MockBackend()
