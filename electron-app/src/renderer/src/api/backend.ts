import type {
  AccelStatus,
  AdvancedSettings,
  AutoAccelEvent,
  DetectedGame,
  DiagnosticReport,
  EffectReport,
  EngineSettings,
  ExtraApp,
  Game,
  LibraryScanResult,
  LogEntry,
  NodeRanking,
  RankedNode,
  RulesStatus,
  RulesUpdateResult,
  Server,
  SettingsPatch,
  Subscription,
  UpdateInfo
} from '@shared/types'
import { sampleGames, sampleServers, sampleSubscriptions } from '@/data/sampleData'
import { WebSocketBackend, allowBackendConnection as allowBackendConnectionImpl } from './ws-backend'
import { ADVANCED_DEFAULTS, validateDns, validateLocalPort, validateRegionPenalty } from '@/utils/advanced'

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
  rankNodes(gameId: string | null, ping: boolean): Promise<NodeRanking>
  startAccelerate(gameId: string, nodeId: string | null): Promise<void>
  stopAccelerate(): Promise<void>
  onStatus(cb: (s: AccelStatus) => void): () => void
  onLog(cb: (l: LogEntry) => void): () => void
  recentLogs(): Promise<LogEntry[]>
  addServerFromLink(link: string): Promise<Server[]>
  removeServer(id: string): Promise<Server[]>
  addSubscription(remark: string, link: string): Promise<Subscription[]>
  removeSubscription(id: string): Promise<SubMutation>
  updateSubscription(id: string): Promise<SubMutation>
  toggleFavorite(id: string): Promise<Game[]>
  setGameAuto(id: string, enabled: boolean): Promise<Game[]>
  onAutoAccel(cb: (e: AutoAccelEvent) => void): () => void
  addGame(data: Partial<Game>): Promise<Game[]>
  updateGame(id: string, data: Partial<Game>): Promise<Game[]>
  removeGame(id: string): Promise<Game[]>
  exportServers(): Promise<string[]>
  getSettings(): Promise<EngineSettings>
  setSettings(patch: SettingsPatch): Promise<EngineSettings>
  resetAdvancedSettings(): Promise<EngineSettings>
  checkUpdate(): Promise<UpdateInfo>
  getEffect(): Promise<EffectReport>
  onEffect(cb: (r: EffectReport) => void): () => void
  runDiagnostics(opts: {
    gameId?: string | null
    nodeId?: string | null
    error?: string | null
  }): Promise<DiagnosticReport>
  fixDiagnostic(action: string): Promise<{ fixed: boolean }>
  scanLibrary(): Promise<LibraryScanResult>
  addDetectedGame(name: string, executables: string[], source?: string): Promise<Game[]>
  rulesStatus(): Promise<RulesStatus>
  updateRules(): Promise<RulesUpdateResult>
}

// Re-exported so callers can open the connection gate without importing the transport directly.
// The mock backend has no gate (it never spawns a process), so this is a no-op there.
export function allowBackendConnection(): void {
  if (inElectron) allowBackendConnectionImpl()
}

function jitter(base: number): number {
  return Math.max(1, Math.round(base + (Math.random() - 0.5) * base * 0.25))
}

// Mirrors NodeRegions in Netch.Core/Services/NodeRanker.cs (subset sufficient for the mock).
// Short latin codes are matched case-SENSITIVELY with lookarounds (not \b) so "JP01" / "香港HK2"
// match while lowercase "us-west" does not. Long words / CJK / emoji are matched case-insensitively.
const SHORT_CODES: Array<{ code: string; name: string; re: RegExp }> = [
  { code: 'HK', name: '香港', re: /(?<![A-Za-z])HK(?![A-Za-z])/ },
  { code: 'TW', name: '台湾', re: /(?<![A-Za-z])TW(?![A-Za-z])/ },
  { code: 'JP', name: '日本', re: /(?<![A-Za-z])JP(?![A-Za-z])/ },
  { code: 'KR', name: '韩国', re: /(?<![A-Za-z])KR(?![A-Za-z])/ },
  { code: 'SG', name: '新加坡', re: /(?<![A-Za-z])SG(?![A-Za-z])/ },
  { code: 'US', name: '美国', re: /(?<![A-Za-z])USA?(?![A-Za-z])/ },
  { code: 'EU', name: '欧洲', re: /(?<![A-Za-z])(?:DE|UK|GB|FR|NL)(?![A-Za-z])/ },
  { code: 'CN', name: '中国大陆', re: /(?<![A-Za-z])CN(?![A-Za-z])/ }
]

const LONG_RULES: Array<{ code: string; name: string; re: RegExp }> = [
  { code: 'HK', name: '香港', re: /香港|港|🇭🇰|Hong ?Kong/i },
  { code: 'TW', name: '台湾', re: /台北|🇹🇼|Taiwan/i },
  { code: 'JP', name: '日本', re: /日本|东京|大阪|🇯🇵|Japan|Tokyo|Osaka/i },
  { code: 'KR', name: '韩国', re: /韩国|首尔|🇰🇷|Korea|Seoul/i },
  { code: 'SG', name: '新加坡', re: /新加坡|狮城|🇸🇬|Singapore/i },
  { code: 'US', name: '美国', re: /美国|洛杉矶|硅谷|圣何塞|西雅图|🇺🇸|United States|Los Angeles|San Jose/i },
  { code: 'EU', name: '欧洲', re: /德国|英国|法国|荷兰|欧洲|🇩🇪|🇬🇧|🇫🇷|🇳🇱|Germany|London|Frankfurt|Amsterdam/i },
  { code: 'CN', name: '中国大陆', re: /中国|国内|回国|大陆|上海|北京|广州|深圳|杭州|🇨🇳|China/i }
]

const HK_TW_RE = /香港|港|🇭🇰|(?<![A-Za-z])HK(?![A-Za-z])|Hong ?Kong|台湾|台北|🇹🇼|(?<![A-Za-z])TW(?![A-Za-z])|Taiwan/i

function detectRegion(text: string): { code: string; name: string } | null {
  for (let i = 0; i < LONG_RULES.length; i++) {
    const long = LONG_RULES[i]
    const short = SHORT_CODES[i]
    if (long.code === 'CN' && HK_TW_RE.test(text)) continue
    if (long.re.test(text) || short.re.test(text)) return { code: long.code, name: long.name }
  }
  return null
}

function preferredRegionsFor(game: Game | null): { regions: string[]; label: string | null } {
  if (!game || !game.regions || game.regions.length === 0) return { regions: [], label: null }
  const hasCn = game.regions.some((r) => r.includes('国服'))
  const hasIntl = game.regions.some((r) => r.includes('国际服'))
  if (hasCn && hasIntl) return { regions: [], label: null }

  const regions: string[] = []
  let label: string | null = null
  const add = (code: string): void => {
    if (!regions.includes(code)) regions.push(code)
  }
  for (const raw of game.regions) {
    const r = raw.trim()
    if (r.includes('亚服')) {
      add('HK'); add('TW'); add('JP'); add('KR'); add('SG')
      label ??= '亚服'
    } else if (r.includes('日服')) {
      add('JP'); label ??= '日服'
    } else if (r.includes('韩服')) {
      add('KR'); label ??= '韩服'
    } else if (r.includes('港服')) {
      add('HK'); label ??= '港服'
    } else if (r.includes('台服')) {
      add('TW'); label ??= '台服'
    } else if (r.includes('北美服') || r.includes('美服')) {
      add('US'); label ??= '美服'
    } else if (r.includes('欧服')) {
      add('EU'); label ??= '欧服'
    } else if (r.includes('国服')) {
      add('CN'); label ??= '国服'
    }
  }
  return { regions, label }
}

// Mirrors SplitRouting.IsValidProcessName in Netch.Core/Services/SplitRouting.cs.
const PROCESS_NAME_RE = /^[A-Za-z0-9 _.+\-()[\]]{1,100}\.exe$/i

export function isValidProcessName(s: string): boolean {
  if (!s) return false
  if (s.includes('/') || s.includes('\\')) return false
  return PROCESS_NAME_RE.test(s)
}

class MockBackend implements Backend {
  private statusCbs = new Set<(s: AccelStatus) => void>()
  private logCbs = new Set<(l: LogEntry) => void>()
  private effectCbs = new Set<(r: EffectReport) => void>()
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
    nodeId: null,
    captureMode: null
  }

  private effect: EffectReport = {
    hasTarget: false,
    target: null,
    baseline: null,
    current: null,
    latencyPct: null,
    summary: '该游戏未配置可测量的域名/IP，仅显示线路延迟',
    series: [],
    measuring: false
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

  async rankNodes(gameId: string | null, ping: boolean): Promise<NodeRanking> {
    if (ping) {
      await new Promise((r) => setTimeout(r, 500))
      this.mockServers = this.mockServers.map((s) => ({ ...s, delay: jitter(s.delay || 90) }))
    }
    const game = gameId ? this.mockGames.find((g) => g.id === gameId) ?? null : null
    const pref = preferredRegionsFor(game)

    const candidates: RankedNode[] = this.mockServers.map((s) => {
      const region = detectRegion(`${s.remark} ${s.hostname}`)
      const match = region != null && pref.regions.includes(region.code)
      const tested = s.delay >= 0
      // Untested/failed nodes get score -1 so the UI renders them as grey/empty bars.
      const score = tested ? s.delay + (pref.regions.length > 0 && !match ? 60 : 0) : -1
      return {
        id: s.id,
        remark: s.remark,
        type: s.type,
        delay: s.delay,
        region: region?.code ?? null,
        regionName: region?.name ?? null,
        regionMatch: match,
        score,
        note: ''
      }
    })

    const bucket = (d: number): number => (d >= 0 ? 0 : d === -1 ? 1 : 2)
    candidates.sort((a, b) => {
      const ba = bucket(a.delay)
      const bb = bucket(b.delay)
      if (ba !== bb) return ba - bb
      if (a.delay >= 0 && b.delay >= 0 && a.score !== b.score) return a.score - b.score
      return a.remark.localeCompare(b.remark, 'zh-CN')
    })

    // Notes are tags: lowest-delay tested node → "延迟最低", matching nodes → "区服匹配", both → joined.
    const testedDelays = candidates.filter((c) => c.delay >= 0).map((c) => c.delay)
    const lowestDelay = testedDelays.length ? Math.min(...testedDelays) : null
    for (const c of candidates) {
      if (c.delay < 0) {
        c.note = c.delay === -1 ? '未测速' : '超时/不可达'
        continue
      }
      const tags: string[] = []
      if (lowestDelay != null && c.delay === lowestDelay) tags.push('延迟最低')
      if (c.regionMatch) tags.push('区服匹配')
      c.note = tags.join(' · ')
    }

    const tested = candidates.filter((c) => c.delay >= 0)
    const recommended = candidates[0] ?? null
    let reason: string
    if (tested.length === 0) {
      reason = '所有线路均未测通，已选择列表中的第一条线路'
    } else {
      const lowest = [...tested].sort((a, b) => a.delay - b.delay)[0]
      if (pref.regions.length === 0) {
        const hasCn = !!game?.regions?.some((r) => r.includes('国服'))
        const hasIntl = !!game?.regions?.some((r) => r.includes('国际服'))
        reason = `延迟最低（${lowest.delay} ms）${hasCn && hasIntl ? '；该游戏同时有国服和国际服，按延迟选择' : ''}`
      } else if (recommended && recommended.regionMatch) {
        // The reason must describe the RECOMMENDED node, not the lowest-delay one.
        reason =
          recommended.delay === lowest.delay
            ? `延迟最低（${recommended.delay} ms），且位于${pref.label}所在地区（${recommended.regionName}）`
            : `位于${pref.label}所在地区（${recommended.regionName}），延迟 ${recommended.delay} ms，仅比最低延迟线路高 ${recommended.delay - lowest.delay} ms`
      } else {
        const matching = tested.filter((c) => c.regionMatch)
        if (matching.length === 0) {
          reason = `没有${pref.label}所在地区的可用线路，已按延迟最低选择（${lowest.delay} ms）`
        } else {
          reason = `${pref.label}所在地区的线路（${recommended?.regionName ?? ''} ${recommended?.delay ?? lowest.delay} ms）比最低延迟线路慢 ${(recommended?.delay ?? lowest.delay) - lowest.delay} ms 以上，已选择延迟最低的线路（${lowest.delay} ms）`
        }
      }
    }

    return {
      recommendedId: recommended?.id ?? null,
      reason,
      preferenceLabel: pref.label,
      preferredRegions: pref.regions,
      candidates
    }
  }

  async startAccelerate(gameId: string, nodeId: string | null): Promise<void> {
    this.log('info', `开始加速 ${gameId} via ${nodeId ?? '智能选路'}`)
    this.setState('connecting')
    await new Promise((r) => setTimeout(r, 800))
    this.startedAt = Date.now()
    // Mock always reports process capture — the real backend picks process/tun/proxy.
    this.status = { ...this.status, gameId, nodeId, state: 'accelerating', loss: 0, captureMode: 'process' }
    this.emit()
    // Seed a plausible before/after measurement: direct ~120 ms / 2% loss, accelerated ~70 ms.
    this.effect = {
      hasTarget: true,
      target: 'game.example.com:443',
      baseline: { latency: 120, loss: 2, jitter: 6, samples: 5 },
      current: null,
      latencyPct: null,
      summary: '正在测量…',
      series: [],
      measuring: true
    }
    this.emitEffect()
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
      nodeId: null,
      captureMode: null
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
  async recentLogs(): Promise<LogEntry[]> {
    return []
  }

  getEffect(): Promise<EffectReport> {
    return Promise.resolve(structuredClone(this.effect))
  }
  onEffect(cb: (r: EffectReport) => void): () => void {
    this.effectCbs.add(cb)
    cb(structuredClone(this.effect))
    return () => this.effectCbs.delete(cb)
  }

  private mockServers: Server[] = structuredClone(sampleServers)
  private mockSubs: Subscription[] = structuredClone(sampleSubscriptions)
  private mockGames: Game[] = structuredClone(sampleGames)
  private mockSettings: EngineSettings = {
    theme: 'light',
    language: 'System',
    minimizeToTray: true,
    rulesUrl: '',
    autoAccelerate: true,
    extraApps: [],
    advanced: { ...ADVANCED_DEFAULTS }
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
  async setGameAuto(id: string, enabled: boolean): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    if (!g.proxyProcesses || g.proxyProcesses.length === 0) {
      throw new Error('该游戏没有配置进程规则，无法自动加速')
    }
    g.autoAccelerate = enabled
    return structuredClone(this.mockGames)
  }
  onAutoAccel(_cb: (e: AutoAccelEvent) => void): () => void {
    // The mock never detects real game processes, so this event never fires.
    return () => {}
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
  async exportServers(): Promise<string[]> {
    return this.mockServers.map((s) => `${s.type}://${s.hostname}:${s.port}#${s.remark}`)
  }
  async checkUpdate(): Promise<UpdateInfo> {
    return {
      status: 'notConfigured',
      current: __APP_VERSION__,
      latest: null,
      tag: null,
      url: null,
      error: '未配置更新源'
    }
  }
  async getSettings(): Promise<EngineSettings> {
    return structuredClone(this.mockSettings)
  }
  async setSettings(patch: SettingsPatch): Promise<EngineSettings> {
    if (patch.extraApps) {
      const seen = new Set<string>()
      for (const app of patch.extraApps) {
        const process = (app.process ?? '').trim()
        if (!isValidProcessName(process)) {
          throw new Error(`无效的进程名：${process}（应为 xxx.exe）`)
        }
        const key = process.toLowerCase()
        if (seen.has(key)) throw new Error(`进程 ${process} 重复`)
        seen.add(key)
      }
    }
    const { advanced, ...rest } = patch
    if (advanced) {
      const adv: Partial<AdvancedSettings> = advanced
      if (adv.tunDns !== undefined) {
        const err = validateDns(adv.tunDns)
        if (err) throw new Error(err)
      }
      if (adv.regionPenaltyMs !== undefined) {
        const err = validateRegionPenalty(adv.regionPenaltyMs)
        if (err) throw new Error(err)
      }
      if (adv.localSocksPort !== undefined) {
        const err = validateLocalPort(adv.localSocksPort)
        if (err) throw new Error(err)
      }
      this.mockSettings = {
        ...this.mockSettings,
        ...rest,
        advanced: { ...this.mockSettings.advanced, ...adv }
      }
      return structuredClone(this.mockSettings)
    }
    this.mockSettings = { ...this.mockSettings, ...rest }
    return structuredClone(this.mockSettings)
  }

  async resetAdvancedSettings(): Promise<EngineSettings> {
    this.mockSettings = { ...this.mockSettings, advanced: { ...ADVANCED_DEFAULTS } }
    return structuredClone(this.mockSettings)
  }

  async scanLibrary(): Promise<LibraryScanResult> {
    await new Promise((r) => setTimeout(r, 900))
    // Match two of the detected games to sample library entries (by process / by name).
    const byProcess = this.mockGames.find((g) => g.id === 'valorant') ?? this.mockGames[0] ?? null
    const byName = this.mockGames.find((g) => g.id === 'gtao') ?? this.mockGames[1] ?? null

    const games: DetectedGame[] = []
    if (byProcess) {
      games.push({
        source: 'Riot',
        sourceId: 'valorant',
        name: byProcess.name,
        installDir: 'C:\\Riot Games\\VALORANT',
        executables: ['VALORANT.exe', 'RiotClientServices.exe'],
        gameId: byProcess.id,
        gameName: byProcess.name,
        matchedBy: 'process'
      })
    }
    if (byName) {
      games.push({
        source: 'Steam',
        sourceId: '271590',
        name: byName.name,
        installDir: 'D:\\SteamLibrary\\steamapps\\common\\GTAV',
        executables: ['GTA5.exe', 'PlayGTAV.exe'],
        gameId: byName.id,
        gameName: byName.name,
        matchedBy: 'name'
      })
    }
    games.push({
      source: 'Steam',
      sourceId: '367520',
      name: 'Hollow Knight',
      installDir: 'D:\\SteamLibrary\\steamapps\\common\\Hollow Knight',
      executables: ['hollow_knight.exe'],
      gameId: null,
      gameName: null,
      matchedBy: null
    })
    games.push({
      source: 'Battle.net',
      sourceId: 'diablo4',
      name: 'Diablo IV',
      installDir: 'C:\\Program Files (x86)\\Diablo IV',
      executables: ['Diablo IV.exe'],
      gameId: null,
      gameName: null,
      matchedBy: null
    })

    return { games, errors: [], scannedAt: new Date().toISOString() }
  }

  async addDetectedGame(name: string, executables: string[], source?: string): Promise<Game[]> {
    const id = 'custom-' + crypto.randomUUID().slice(0, 8)
    this.mockGames.push({
      id,
      name,
      icon: '🎮',
      accent: '',
      regionText: '',
      ruleCount: 0,
      categories: ['PC'],
      favorite: false,
      custom: true,
      englishName: source || '',
      aliases: [],
      category: 'PC',
      regions: [],
      platforms: ['PC'],
      proxyDomains: [],
      proxyIps: [],
      proxyProcesses: [...executables]
    })
    return structuredClone(this.mockGames)
  }

  private mockRules: RulesStatus = {
    version: '1',
    source: 'bundled',
    gameCount: sampleGames.length,
    lastCheck: null,
    lastError: null,
    lastResult: null,
    url: ''
  }

  async rulesStatus(): Promise<RulesStatus> {
    return { ...this.mockRules, url: this.mockSettings.rulesUrl }
  }

  async updateRules(): Promise<RulesUpdateResult> {
    await new Promise((r) => setTimeout(r, 700))
    if (!this.mockSettings.rulesUrl) {
      return {
        status: 'failed',
        message: '未配置规则更新地址',
        version: this.mockRules.version,
        games: structuredClone(this.mockGames)
      }
    }
    this.mockRules = {
      ...this.mockRules,
      lastCheck: new Date().toISOString(),
      lastResult: '已是最新版本',
      lastError: null
    }
    return {
      status: 'upToDate',
      message: '已是最新版本',
      version: this.mockRules.version,
      games: structuredClone(this.mockGames)
    }
  }

  async runDiagnostics(opts: {
    gameId?: string | null
    nodeId?: string | null
    error?: string | null
  }): Promise<DiagnosticReport> {
    await new Promise((r) => setTimeout(r, 600))
    return {
      items: [
        {
          id: 'core',
          title: '加速核心',
          status: 'ok',
          detail: '加速核心文件完整',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'admin',
          title: '管理员权限',
          status: 'warning',
          detail: '当前未以管理员身份运行，进程模式和 TUN 模式不可用，只能使用系统代理模式（大部分游戏无法被加速）',
          suggestion: '退出 Game+ 后重新打开，在系统弹出的权限确认窗口中选择"是"',
          fixAction: null
        },
        {
          id: 'netfilter',
          title: '进程模式驱动',
          status: 'skipped',
          detail: '需要管理员权限才能加载驱动',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'wintun',
          title: 'TUN 模式组件',
          status: 'skipped',
          detail: '需要管理员权限才能创建虚拟网卡',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'port',
          title: '本地端口',
          status: 'ok',
          detail: '可以在本机回环地址上监听端口',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'dns',
          title: 'DNS 解析',
          status: 'ok',
          detail: 'www.microsoft.com → 20.70.246.20',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'node',
          title: '线路连通性',
          status: 'ok',
          detail: '香港 01：42 ms',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'proxy',
          title: '系统代理',
          status: 'error',
          detail: '系统代理指向已关闭的本地端口 1080，浏览器等程序可能无法上网',
          suggestion: '点击"修复"恢复为不使用代理',
          fixAction: 'fixProxy'
        }
      ],
      summary: '发现 1 个错误、1 个警告',
      ranAt: new Date().toISOString(),
      errorExplanation: opts.error ? '未能识别的错误，请参考下方检测结果' : null
    }
  }

  async fixDiagnostic(action: string): Promise<{ fixed: boolean }> {
    await new Promise((r) => setTimeout(r, 300))
    return { fixed: action === 'fixProxy' }
  }

  private tick(): void {
    this.status.durationSec = Math.floor((Date.now() - this.startedAt) / 1000)
    this.status.latency = jitter(72)
    this.status.jitter = Math.round(Math.random() * 8)
    this.status.loss = Math.random() < 0.9 ? 0 : Math.round(Math.random() * 2 * 10) / 10
    this.status.downloadBps = 40_000 + Math.random() * 120_000
    this.status.uploadBps = 8_000 + Math.random() * 30_000
    this.emit()

    if (this.effect.hasTarget) {
      const accel = Math.random() < 0.95 ? jitter(70) : -4
      this.effect.series.push({ ts: Date.now(), accel })
      if (this.effect.series.length > 90) this.effect.series.shift()
      const ok = this.effect.series.filter((p) => p.accel >= 0)
      const mean = ok.length ? Math.round(ok.reduce((a, b) => a + b.accel, 0) / ok.length) : -1
      const loss = Math.round((100 * (this.effect.series.length - ok.length)) / this.effect.series.length * 10) / 10
      this.effect.current = { latency: mean, loss, jitter: 4, samples: this.effect.series.length }
      this.effect.measuring = false
      const base = this.effect.baseline
      if (base && base.latency >= 0 && mean >= 0) {
        const pct = Math.round((100 * (base.latency - mean)) / base.latency)
        this.effect.latencyPct = pct
        this.effect.summary =
          pct >= 5 ? `延迟降低 ${pct}%` : pct <= -5 ? `延迟升高 ${Math.abs(pct)}%，建议切换线路` : '延迟与直连相近'
      }
      this.emitEffect()
    }
  }

  private setState(state: AccelStatus['state']): void {
    this.status.state = state
    this.emit()
  }
  private emit(): void {
    const snap = structuredClone(this.status)
    this.statusCbs.forEach((cb) => cb(snap))
  }
  private emitEffect(): void {
    const snap = structuredClone(this.effect)
    this.effectCbs.forEach((cb) => cb(snap))
  }
  private log(level: LogEntry['level'], message: string): void {
    const entry: LogEntry = { ts: new Date().toISOString(), level, message }
    this.logCbs.forEach((cb) => cb(entry))
  }
}

// In Electron the real C# sidecar is available; in a plain browser (vite preview) fall back to the mock.
const inElectron = typeof window !== 'undefined' && !!window.gameplus?.backend
export const backend: Backend = inElectron ? new WebSocketBackend() : new MockBackend()
