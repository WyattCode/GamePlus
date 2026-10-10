import type {
  AccelSession,
  AccelStatus,
  AdvancedSettings,
  AutoAccelEvent,
  AutoStartState,
  AutoSwitchEvent,
  BaselineInfo,
  CaptureMode,
  CaptureModeKind,
  DetectedGame,
  DiagnosticBundle,
  DiagnosticReport,
  EffectReport,
  EngineSettings,
  ExtraApp,
  Game,
  GameCompat,
  ImportResult,
  LanAddress,
  LanShareSettings,
  LanShareUpdate,
  LibraryScanResult,
  LogEntry,
  NodeRanking,
  RankedNode,
  RuleExport,
  RuleImportResult,
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
import { validateLaunchTarget } from '@/utils/launchApi'

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
  /** Imports nodes from pasted text (share links, subscription blob, Clash YAML or a subscription URL). */
  importNodes(text: string): Promise<ImportResult>
  /** Exports one game's rules as pretty JSON + a gameplus-rule: share string. */
  exportGameRule(id: string): Promise<RuleExport>
  /** Imports a single-game rule (JSON text or gameplus-rule: share string). */
  importGameRule(text: string, onConflict?: 'ask' | 'replace' | 'copy'): Promise<RuleImportResult>
  toggleFavorite(id: string): Promise<Game[]>
  setGameAuto(id: string, enabled: boolean): Promise<Game[]>
  setGameServer(id: string, server: 'cn' | 'intl'): Promise<Game[]>
  /** Sets (or clears, with "default") the per-game capture-mode override. */
  setGameMode(id: string, mode: CaptureMode | 'default'): Promise<Game[]>
  /** Toggles whether the game's download (CDN) traffic also goes through the node. */
  setGameDownloadProxy(id: string, enabled: boolean): Promise<Game[]>
  /** Sets (or clears, with an empty string) the per-game launch target (steam://, epic:// or a local .exe path). */
  setGameLaunch(id: string, target: string): Promise<Game[]>
  /** Returns launch-target suggestions derived from the installed-games scan (empty when none). */
  suggestLaunch(id: string): Promise<string[]>
  onAutoAccel(cb: (e: AutoAccelEvent) => void): () => void
  /** Pushes AutoSwitchEvent when the auto node switcher reacts to sustained bad quality. */
  onAutoSwitch(cb: (e: AutoSwitchEvent) => void): () => void
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
  /** Marks the game to re-measure its direct baseline on the next acceleration. */
  remeasureEffect(gameId: string): Promise<{ ok: boolean }>
  /** Returns the stored direct-connection baseline for a game (null when never measured). */
  getBaseline(gameId: string): Promise<BaselineInfo>
  runDiagnostics(opts: {
    gameId?: string | null
    nodeId?: string | null
    error?: string | null
  }): Promise<DiagnosticReport>
  fixDiagnostic(action: string): Promise<{ fixed: boolean }>
  /** Builds a one-click diagnostic bundle (zip, base64) for the client to save. */
  exportDiagnosticBundle(client: unknown): Promise<DiagnosticBundle>
  scanLibrary(): Promise<LibraryScanResult>
  addDetectedGame(name: string, executables: string[], source?: string): Promise<Game[]>
  rulesStatus(): Promise<RulesStatus>
  updateRules(): Promise<RulesUpdateResult>
  getAutoStart(): Promise<AutoStartState>
  setAutoStart(enabled: boolean): Promise<AutoStartState>
  /** Per-game acceleration history, newest first. */
  listHistory(gameId?: string, limit?: number): Promise<AccelSession[]>
  /** Clears all history, or only one game's when gameId is given. */
  clearHistory(gameId?: string): Promise<void>
  /** Private IPv4 addresses of this PC, for the LAN-share bind-address picker. */
  getLanAddresses(): Promise<LanAddress[]>
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
  { code: 'JP', name: '日本', re: /东京|大阪|🇯🇵|Japan|Tokyo|Osaka/i },
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

function hasServerChoice(game: Game | null): boolean {
  if (!game || !game.regions || game.regions.length === 0) return false
  const hasCn = game.regions.some((r) => r.includes('国服'))
  const hasIntl = game.regions.some((r) => r.includes('国际服'))
  return hasCn && hasIntl
}

function preferredRegionsFor(
  game: Game | null,
  serverChoice?: 'cn' | 'intl' | null
): { regions: string[]; label: string | null; exclude: string[] } {
  if (!game || !game.regions || game.regions.length === 0) return { regions: [], label: null, exclude: [] }

  if (hasServerChoice(game)) {
    if (serverChoice === 'cn') return { regions: ['CN'], label: '国服', exclude: [] }
    const intl = buildPreference(game.regions, true)
    if (intl) return { ...intl, exclude: [] }
    return { regions: [], label: '国际服', exclude: ['CN'] }
  }

  const p = buildPreference(game.regions, false)
  return p ? { ...p, exclude: [] } : { regions: [], label: null, exclude: [] }
}

function buildPreference(
  regions: string[],
  ignoreCn: boolean
): { regions: string[]; label: string | null } | null {
  const codes: string[] = []
  let label: string | null = null
  const add = (code: string): void => {
    if (!codes.includes(code)) codes.push(code)
  }
  for (const raw of regions) {
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
      if (ignoreCn) continue
      add('CN'); label ??= '国服'
    }
  }
  return codes.length === 0 ? null : { regions: codes, label: label ?? '偏好地区' }
}

// Mirrors SplitRouting.IsValidProcessName in Netch.Core/Services/SplitRouting.cs.
const PROCESS_NAME_RE = /^[A-Za-z0-9 _.+\-()[\]]{1,100}\.exe$/i

export function isValidProcessName(s: string): boolean {
  if (!s) return false
  if (s.includes('/') || s.includes('\\')) return false
  return PROCESS_NAME_RE.test(s)
}

// Schemes the mock recognises when pretending to import pasted share links.
const IMPORT_SCHEMES = ['ss://', 'vmess://', 'trojan://', 'vless://', 'hysteria2://', 'socks://', 'socks5://']

// Single-game rule export/import (mirrors the C# RuleShareService contract).
const RULE_SHARE_PREFIX = 'gameplus-rule:v1:'
const RULE_MAX_BYTES = 64 * 1024
const RULE_UNRECOGNIZED = '无法识别的规则内容（需要 Game+ 规则 JSON 或 gameplus-rule: 分享串）'

// Per-game compatibility notes (mirrors the backend's GameDto.Compat); applied to seed games only.
const MOCK_COMPAT: Record<string, GameCompat> = {
  valorant: {
    recommendedMode: null,
    note: 'Riot Vanguard 为内核级反作弊，开机即加载；如进程模式驱动加载失败，可尝试 TUN 模式（需该游戏有 IP 规则）。',
    verified: false
  },
  steam: {
    recommendedMode: 'auto',
    note: '商店/社区/登录在自动模式下即可加速；游戏下载默认直连，可在加速页「游戏下载」开关中改为走代理。',
    verified: false
  },
  apex: {
    recommendedMode: null,
    note: '使用 Easy Anti-Cheat 反作弊。加速器只转发网络流量、不修改游戏，但无法保证不被误判，风险自负。',
    verified: false
  }
}

/** UTF-8 safe base64url (no padding) of a string. */
function toBase64Url(s: string): string {
  return btoa(unescape(encodeURIComponent(s)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

/** Inverse of toBase64Url; throws when the input is not valid base64url. */
function fromBase64Url(s: string): string {
  let b64 = s.replace(/-/g, '+').replace(/_/g, '/')
  while (b64.length % 4 !== 0) b64 += '='
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(b64)) throw new Error('bad base64')
  return decodeURIComponent(escape(atob(b64)))
}

/** Capture methods available for a game's rules (mirrors the backend's GameDto.AvailableModes). */
export function availableModesFor(game: Game): CaptureModeKind[] {
  const modes: CaptureModeKind[] = []
  if (game.proxyProcesses && game.proxyProcesses.length > 0) modes.push('process')
  if (game.proxyIps && game.proxyIps.length > 0) modes.push('tun')
  if (game.proxyDomains && game.proxyDomains.length > 0) modes.push('proxy')
  return modes
}

/** Chinese error for a mode that the game's rules cannot support; null when the mode is usable. */
export function modeUnavailableError(mode: CaptureModeKind): string {
  if (mode === 'process') return '该游戏没有进程规则，无法使用进程模式'
  if (mode === 'tun') return '该游戏没有 IP 规则，无法使用 TUN 模式'
  return '该游戏没有域名规则，无法使用系统代理模式'
}

/** True when s is a dotted quad inside 10/8, 172.16/12 or 192.168/16. */
function isPrivateIpv4(s: string): boolean {
  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(s)
  if (!m) return false
  const octets = [Number(m[1]), Number(m[2]), Number(m[3]), Number(m[4])]
  if (octets.some((o) => o > 255)) return false
  const [a, b] = octets
  return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168)
}

class MockBackend implements Backend {
  private statusCbs = new Set<(s: AccelStatus) => void>()
  private logCbs = new Set<(l: LogEntry) => void>()
  private effectCbs = new Set<(r: EffectReport) => void>()
  private autoSwitchCbs = new Set<(e: AutoSwitchEvent) => void>()
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
    captureMode: null,
    lanShareEndpoint: null
  }

  private effect: EffectReport = {
    hasTarget: false,
    target: null,
    baseline: null,
    current: null,
    latencyPct: null,
    summary: '该游戏未配置可测量的域名/IP，仅显示线路延迟',
    series: [],
    measuring: false,
    baselineMeasuredAt: null,
    baselineFromCache: false
  }

  /** Per-game capture-mode overrides (gameId → mode); missing = follow the global default. */
  private gameModes = new Map<string, CaptureMode>()

  /** Per-game download-via-proxy flags (gameId → true); missing = downloads go DIRECT. */
  private downloadProxy = new Map<string, boolean>()

  /** Per-game launch targets (gameId → target); mirrors AppSettings.GameLaunchTargets. */
  private launchTargets = new Map<string, string>()

  private mockServers: Server[] = structuredClone(sampleServers)
  private mockSubs: Subscription[] = structuredClone(sampleSubscriptions)
  private mockGames: Game[] = structuredClone(sampleGames)

  constructor() {
    // Seed the Steam built-in download-split domains (mirrors the backend's seed data): the CDN
    // download domains are no longer part of proxyDomains but live in downloadDomains.
    const steam = this.mockGames.find((g) => g.id === 'steam' || g.name.includes('Steam'))
    if (steam) {
      steam.downloadDomains = ['steamcontent.com', 'steampipe.akamaized.net']
      if (steam.proxyDomains) {
        steam.proxyDomains = steam.proxyDomains.filter((d) => !d.includes('steamcontent.com'))
      }
    }
    // Seed per-game compatibility notes (only for seed games that actually exist).
    for (const [id, compat] of Object.entries(MOCK_COMPAT)) {
      const g = this.mockGames.find((x) => x.id === id)
      if (g) g.compat = structuredClone(compat)
    }
  }

  /** Returns the mock game list with captureMode/availableModes/downloadViaProxy/launchTarget filled in. */
  private gamesWithModes(): Game[] {
    return this.mockGames.map((g) => ({
      ...g,
      captureMode: this.gameModes.get(g.id) ?? null,
      downloadViaProxy: this.downloadProxy.get(g.id) ?? false,
      launchTarget: this.launchTargets.get(g.id) ?? null,
      availableModes: availableModesFor(g)
    }))
  }

  async listGames(): Promise<Game[]> {
    return structuredClone(this.gamesWithModes())
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
    const choice = game && hasServerChoice(game) ? game.serverChoice ?? 'intl' : null
    const pref = preferredRegionsFor(game, choice)

    const candidates: RankedNode[] = this.mockServers.map((s) => {
      const region = detectRegion(`${s.remark} ${s.hostname}`)
      const regionCode = region?.code ?? null
      let match: boolean
      let penalised: boolean
      if (pref.exclude.length > 0) {
        const isExcluded = regionCode != null && pref.exclude.includes(regionCode)
        match = regionCode != null && !isExcluded
        penalised = isExcluded
      } else {
        match = regionCode != null && pref.regions.includes(regionCode)
        penalised = pref.regions.length > 0 && !match
      }
      const tested = s.delay >= 0
      // Untested/failed nodes get score -1 so the UI renders them as grey/empty bars.
      const score = tested ? s.delay + (penalised ? 60 : 0) : -1
      return {
        id: s.id,
        remark: s.remark,
        type: s.type,
        delay: s.delay,
        region: regionCode,
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
      if (pref.exclude.length > 0) {
        const overseas = tested.filter((c) => !(c.region != null && pref.exclude.includes(c.region)))
        if (overseas.length > 0) {
          const o = [...overseas].sort((a, b) => a.delay - b.delay)[0]
          reason = `国际服：已排除国内线路，选择延迟最低的海外线路（${o.delay} ms）`
        } else {
          reason = `国际服：没有可用的海外线路，已选择延迟最低的线路（${lowest.delay} ms）`
        }
      } else if (pref.regions.length === 0) {
        reason = `延迟最低（${lowest.delay} ms）`
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
      candidates,
      serverChoice: choice
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
      measuring: true,
      baselineMeasuredAt: new Date().toISOString(),
      baselineFromCache: false
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
      captureMode: null,
      lanShareEndpoint: null
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

  async remeasureEffect(_gameId: string): Promise<{ ok: boolean }> {
    // The mock has no persistent per-game baseline cache, so just clear the cached flag.
    this.effect = { ...this.effect, baselineFromCache: false }
    this.emitEffect()
    return { ok: true }
  }

  async getBaseline(gameId: string): Promise<BaselineInfo> {
    // Deterministic per-game mock: 'valorant' has a good direct baseline (the "可不加速" path);
    // every other odd-indexed game has a poor one; the rest were never measured.
    if (gameId === 'valorant') {
      return {
        baseline: { latency: 28, loss: 0, jitter: 3, samples: 5 },
        measuredAt: new Date(Date.now() - 2 * 24 * 3_600_000).toISOString()
      }
    }
    const idx = this.mockGames.findIndex((g) => g.id === gameId)
    if (idx > 0 && idx % 2 === 1) {
      return {
        baseline: { latency: 140, loss: 3.2, jitter: 18, samples: 6 },
        measuredAt: new Date(Date.now() - 5 * 24 * 3_600_000).toISOString()
      }
    }
    return { baseline: null, measuredAt: null }
  }

  private mockSettings: EngineSettings = {
    theme: 'light',
    language: 'System',
    minimizeToTray: true,
    rulesUrl: '',
    autoAccelerate: true,
    subscriptionAutoUpdateHours: 12,
    extraApps: [],
    advanced: {
      ...ADVANCED_DEFAULTS,
      defaultCaptureMode: 'auto' as CaptureMode,
      autoSwitchEnabled: false,
      autoSwitchLossPct: 5,
      autoSwitchLatencyMs: 200,
      autoSwitchSustainSec: 15,
      autoSwitchCooldownSec: 60,
      autoSwitchMaxSwitches: 3
    },
    lanShare: {
      enabled: false,
      bindAddress: '',
      httpPort: 7899,
      socksPort: 0,
      allowedClients: [],
      username: '',
      hasPassword: false
    }
  }

  private mockAutoStart: AutoStartState = { enabled: false, supported: true, reason: null }

  // Deterministic fake history: 6 sessions for the requested game (or a default game id).
  private mockHistory: AccelSession[] = []

  private buildMockHistory(gameId: string): AccelSession[] {
    const nodes = ['香港 01', '日本 02', '新加坡 03', '美国 04', '台湾 05', '韩国 06']
    const durations = [1240, 860, 2450, 320, 1780, 640]
    const baselines = [128, 142, 96, 210, 118, 155]
    const accelerated = [62, 71, 58, 88, 66, 74]
    const base = Date.UTC(2025, 0, 15, 12, 0, 0)
    const sessions: AccelSession[] = []
    for (let i = 0; i < 6; i++) {
      const startedAt = new Date(base - i * 3_600_000)
      const endedAt = new Date(startedAt.getTime() + durations[i] * 1000)
      const pct = Math.round((100 * (baselines[i] - accelerated[i])) / baselines[i])
      sessions.push({
        id: `mock-${gameId}-${i}`,
        gameId,
        gameName: gameId,
        nodeName: nodes[i],
        mode: i % 2 === 0 ? 'process' : 'tun',
        serverChoice: i % 3 === 0 ? 'intl' : null,
        startedAt: startedAt.toISOString(),
        endedAt: endedAt.toISOString(),
        durationSec: durations[i],
        baselineMs: baselines[i],
        acceleratedMs: accelerated[i],
        latencyPct: pct,
        summary: `参考延迟降低 ${pct}%`
      })
    }
    return sessions
  }

  async listHistory(gameId?: string, limit?: number): Promise<AccelSession[]> {
    const id = gameId ?? 'valorant'
    if (this.mockHistory.length === 0) this.mockHistory = this.buildMockHistory(id)
    const filtered = gameId ? this.mockHistory.filter((s) => s.gameId === gameId) : this.mockHistory
    const capped = limit && limit > 0 ? filtered.slice(0, limit) : filtered
    return structuredClone(capped)
  }

  async clearHistory(gameId?: string): Promise<void> {
    if (gameId) this.mockHistory = this.mockHistory.filter((s) => s.gameId !== gameId)
    else this.mockHistory = []
  }

  async getLanAddresses(): Promise<LanAddress[]> {
    return [{ address: '192.168.1.23', interfaceName: '以太网' }]
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
    this.mockSubs = this.mockSubs.filter((x) => x.id !== id)
    return { subscriptions: structuredClone(this.mockSubs), servers: structuredClone(this.mockServers) }
  }
  async updateSubscription(id: string): Promise<{ subscriptions: Subscription[]; servers: Server[] }> {
    const s = this.mockSubs.find((x) => x.id === id)
    if (s) s.lastUpdated = new Date().toISOString()
    return { subscriptions: structuredClone(this.mockSubs), servers: structuredClone(this.mockServers) }
  }

  async importNodes(text: string): Promise<ImportResult> {
    const trimmed = (text ?? '').trim()
    if (!trimmed) throw new Error('无法识别的节点内容')

    // A single http(s) URL is treated as a subscription.
    if (/^https?:\/\/\S+$/i.test(trimmed)) {
      this.mockSubs.push({
        id: crypto.randomUUID(),
        remark: '导入订阅',
        link: trimmed,
        serverCount: this.mockServers.length,
        lastUpdated: new Date().toISOString()
      })
      return {
        added: this.mockServers.length,
        duplicates: 0,
        invalid: 0,
        format: 'subscription',
        servers: structuredClone(this.mockServers)
      }
    }

    const lines = trimmed
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => IMPORT_SCHEMES.some((s) => l.toLowerCase().startsWith(s)))

    if (lines.length === 0) throw new Error('无法识别的节点内容')

    // Pretend half are new and the rest duplicates.
    const added = Math.ceil(lines.length / 2)
    const duplicates = lines.length - added
    for (let i = 0; i < added; i++) {
      this.mockServers.push({
        id: crypto.randomUUID(),
        kind: 'ss',
        type: 'SS',
        remark: `导入节点 ${this.mockServers.length + 1}`,
        hostname: 'mock.example.net',
        port: 8388 + i,
        group: 'NONE',
        delay: -1
      })
    }

    return {
      added,
      duplicates,
      invalid: 0,
      format: 'links',
      servers: structuredClone(this.mockServers)
    }
  }

  async exportGameRule(id: string): Promise<RuleExport> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    // Rules ids can't start with "custom-": exported custom games get "user-" + rest.
    const exportId = g.id.startsWith('custom-') ? 'user-' + g.id.slice('custom-'.length) : g.id
    const game = {
      id: exportId,
      name: g.name,
      englishName: g.englishName ?? '',
      aliases: g.aliases ?? [],
      icon: g.icon,
      accentColor: g.accent,
      category: g.category ?? 'PC',
      regions: g.regions ?? [],
      platforms: g.platforms ?? [],
      proxyDomains: g.proxyDomains ?? [],
      proxyIps: g.proxyIps ?? [],
      proxyProcesses: g.proxyProcesses ?? [],
      downloadDomains: g.downloadDomains ?? []
    }
    const file = {
      format: 'gameplus-game-rule',
      schema: 1,
      version: 1,
      exportedAt: new Date().toISOString(),
      games: [game]
    }
    const json = JSON.stringify(file, null, 2)
    const share = RULE_SHARE_PREFIX + toBase64Url(JSON.stringify(file))
    return { json, share, fileName: `${exportId}.gameplus-rule.json` }
  }

  async importGameRule(text: string, onConflict: 'ask' | 'replace' | 'copy' = 'ask'): Promise<RuleImportResult> {
    const trimmed = (text ?? '').trim()
    if (!trimmed) throw new Error(RULE_UNRECOGNIZED)
    if (trimmed.length > RULE_MAX_BYTES) throw new Error('规则内容过大')

    let raw = trimmed
    if (trimmed.startsWith(RULE_SHARE_PREFIX)) {
      try {
        raw = fromBase64Url(trimmed.slice(RULE_SHARE_PREFIX.length))
      } catch {
        throw new Error('规则分享串已损坏')
      }
      if (raw.length > RULE_MAX_BYTES) throw new Error('规则内容过大')
    }

    let file: { format?: string; schema?: number; version?: number; games?: unknown }
    try {
      file = JSON.parse(raw)
    } catch {
      throw new Error(trimmed.startsWith(RULE_SHARE_PREFIX) ? '规则分享串已损坏' : RULE_UNRECOGNIZED)
    }
    if (!file || typeof file !== 'object' || file.format !== 'gameplus-game-rule' || file.schema !== 1 || file.version !== 1) {
      throw new Error('规则文件格式不正确（需要 format=gameplus-game-rule、schema=1、version=1）')
    }
    if (!Array.isArray(file.games) || file.games.length !== 1) {
      throw new Error('规则文件必须包含且仅包含一个游戏')
    }
    const incoming = file.games[0] as Record<string, unknown>
    const incomingName = typeof incoming.name === 'string' ? incoming.name.trim() : ''
    const rawId = typeof incoming.id === 'string' ? incoming.id.trim() : ''
    if (!incomingName) throw new Error('规则文件中的游戏缺少名称')
    if (!rawId) throw new Error('规则文件中的游戏缺少 id')

    // Target id = "custom-" + incomingId (strip a leading "user-"; cut to max 64 chars).
    const incomingId = rawId.startsWith('user-') ? rawId.slice('user-'.length) : rawId
    const targetId = ('custom-' + incomingId).slice(0, 64)
    const lowerName = incomingName.toLowerCase()

    let copyId: string | null = null
    let copyName = incomingName

    const builtin = this.mockGames.find(
      (g) => !g.custom && (g.id === incomingId || g.name.toLowerCase() === lowerName)
    )
    if (builtin) {
      if (onConflict === 'replace') throw new Error('内置游戏不能被替换，请选择「另存为副本」')
      if (onConflict === 'ask') {
        return {
          status: 'conflict',
          conflict: { kind: 'builtin', existingId: builtin.id, existingName: builtin.name, incomingName }
        }
      }
      copyId = this.uniqueCopyId(targetId)
      copyName = incomingName + '（导入）'
    } else {
      const existing = this.mockGames.find(
        (g) => g.custom && (g.id === targetId || g.name.toLowerCase() === lowerName)
      )
      if (existing) {
        if (onConflict === 'ask') {
          return {
            status: 'conflict',
            conflict: { kind: 'custom', existingId: existing.id, existingName: existing.name, incomingName }
          }
        }
        if (onConflict === 'replace') {
          // Overwrite the existing custom game's rules/name but keep its id and favorite flag.
          Object.assign(existing, this.gameFromRule(incoming, existing.id, incomingName), {
            favorite: existing.favorite
          })
          return { status: 'imported', importedId: existing.id, games: structuredClone(this.gamesWithModes()) }
        }
        copyId = this.uniqueCopyId(targetId)
        copyName = incomingName + '（导入）'
      }
    }

    const newId = copyId ?? targetId
    this.mockGames.push(this.gameFromRule(incoming, newId, copyName))
    return { status: 'imported', importedId: newId, games: structuredClone(this.gamesWithModes()) }
  }

  /** First free id among base, base-2, base-3, … */
  private uniqueCopyId(base: string): string {
    if (!this.mockGames.some((g) => g.id === base)) return base
    for (let i = 2; ; i++) {
      const candidate = `${base}-${i}`
      if (!this.mockGames.some((g) => g.id === candidate)) return candidate
    }
  }

  /** Maps an imported rule game object (GameRulesFile shape) onto a custom Game. */
  private gameFromRule(incoming: Record<string, unknown>, id: string, name: string): Game {
    const strList = (v: unknown): string[] =>
      Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []
    const category = typeof incoming.category === 'string' && incoming.category ? incoming.category : 'PC'
    const platforms = strList(incoming.platforms)
    const regions = strList(incoming.regions)
    const proxyDomains = strList(incoming.proxyDomains)
    const proxyIps = strList(incoming.proxyIps)
    const proxyProcesses = strList(incoming.proxyProcesses)
    return {
      id,
      name,
      icon: typeof incoming.icon === 'string' && incoming.icon ? incoming.icon : '🎮',
      accent: typeof incoming.accentColor === 'string' ? incoming.accentColor : '',
      regionText: regions.join(' · '),
      ruleCount: proxyDomains.length + proxyIps.length + proxyProcesses.length,
      categories: [category, ...platforms],
      favorite: false,
      custom: true,
      englishName: typeof incoming.englishName === 'string' ? incoming.englishName : '',
      aliases: strList(incoming.aliases),
      category,
      regions,
      platforms,
      proxyDomains,
      proxyIps,
      proxyProcesses,
      downloadDomains: strList(incoming.downloadDomains)
    }
  }

  async toggleFavorite(id: string): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (g) g.favorite = !g.favorite
    return structuredClone(this.gamesWithModes())
  }
  async setGameAuto(id: string, enabled: boolean): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    if (!g.proxyProcesses || g.proxyProcesses.length === 0) {
      throw new Error('该游戏没有配置进程规则，无法自动加速')
    }
    g.autoAccelerate = enabled
    return structuredClone(this.gamesWithModes())
  }
  async setGameServer(id: string, server: 'cn' | 'intl'): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    if (!hasServerChoice(g)) throw new Error('该游戏没有国服/国际服之分')
    g.serverChoice = server
    return structuredClone(this.gamesWithModes())
  }
  async setGameMode(id: string, mode: CaptureMode | 'default'): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    if (mode !== 'default' && mode !== 'auto' && mode !== 'process' && mode !== 'tun' && mode !== 'proxy') {
      throw new Error('无效的加速模式')
    }
    if (mode === 'default') {
      this.gameModes.delete(id)
    } else if (mode === 'auto') {
      this.gameModes.set(id, 'auto')
    } else {
      const available = availableModesFor(g)
      if (!available.includes(mode)) throw new Error(modeUnavailableError(mode))
      this.gameModes.set(id, mode)
    }
    return structuredClone(this.gamesWithModes())
  }
  async setGameDownloadProxy(id: string, enabled: boolean): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    if (!g.downloadDomains || g.downloadDomains.length === 0) {
      throw new Error('该游戏没有配置下载域名')
    }
    if (enabled) this.downloadProxy.set(id, true)
    else this.downloadProxy.delete(id)
    return structuredClone(this.gamesWithModes())
  }
  async setGameLaunch(id: string, target: string): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (!g) throw new Error('游戏不存在')
    const t = (target ?? '').trim()
    if (t) {
      // Light validation only: the mock does not check .exe existence on disk.
      const err = validateLaunchTarget(t)
      if (err) throw new Error(err)
      this.launchTargets.set(id, t)
    } else {
      this.launchTargets.delete(id)
    }
    return structuredClone(this.gamesWithModes())
  }
  async suggestLaunch(id: string): Promise<string[]> {
    if (id === 'apex') return ['steam://rungameid/1172470']
    if (id === 'valorant') return ['C:\\Riot Games\\VALORANT\\live\\VALORANT.exe']
    return []
  }
  onAutoAccel(_cb: (e: AutoAccelEvent) => void): () => void {
    // The mock never detects real game processes, so this event never fires.
    return () => {}
  }
  onAutoSwitch(cb: (e: AutoSwitchEvent) => void): () => void {
    // Stored but never fired: the mock has no real quality measurement to trigger a switch.
    this.autoSwitchCbs.add(cb)
    return () => this.autoSwitchCbs.delete(cb)
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
    return structuredClone(this.gamesWithModes())
  }
  async updateGame(id: string, data: Partial<Game>): Promise<Game[]> {
    const g = this.mockGames.find((x) => x.id === id)
    if (g) Object.assign(g, data, { id })
    return structuredClone(this.gamesWithModes())
  }
  async removeGame(id: string): Promise<Game[]> {
    this.mockGames = this.mockGames.filter((x) => x.id !== id)
    this.gameModes.delete(id)
    this.downloadProxy.delete(id)
    this.launchTargets.delete(id)
    return structuredClone(this.gamesWithModes())
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
      const seen = new Set()
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
    if (patch.subscriptionAutoUpdateHours !== undefined) {
      const hours = patch.subscriptionAutoUpdateHours
      if (![0, 6, 12, 24, 48].includes(hours)) {
        throw new Error('订阅自动更新间隔只能 0、6、12、24 或 48 小时')
      }
    }
    if (patch.lanShare) {
      const cur = this.mockSettings.lanShare
      const u = patch.lanShare
      const next: LanShareSettings = {
        enabled: u.enabled ?? cur.enabled,
        bindAddress: (u.bindAddress ?? cur.bindAddress).trim(),
        httpPort: u.httpPort ?? cur.httpPort,
        socksPort: u.socksPort ?? cur.socksPort,
        allowedClients: u.allowedClients ?? cur.allowedClients,
        username: (u.username ?? cur.username).trim(),
        hasPassword: cur.hasPassword
      }
      if (u.clearPassword) next.hasPassword = false
      else if (u.password) next.hasPassword = true
      if (next.enabled) {
        if (!isPrivateIpv4(next.bindAddress)) {
          throw new Error('共享地址必须是本机已有的局域网 IPv4 地址（10.x / 172.16–31.x / 192.168.x）')
        }
        if (next.allowedClients.length === 0) throw new Error('允许的设备列表不能为空')
        if (next.allowedClients.length > 32) throw new Error('允许的设备最多 32 条')
      }
      if (!Number.isInteger(next.httpPort) || next.httpPort < 1024 || next.httpPort > 65535) {
        throw new Error('HTTP 端口必须在 1024–65535 之间')
      }
      if (
        next.socksPort !== 0 &&
        (!Number.isInteger(next.socksPort) || next.socksPort < 1024 || next.socksPort > 65535)
      ) {
        throw new Error('SOCKS5 端口必须是 0（关闭）或在 1024–65535 之间')
      }
      if (next.socksPort !== 0 && next.socksPort === next.httpPort) {
        throw new Error('HTTP 与 SOCKS5 端口不能相同')
      }
      const localSocks = this.mockSettings.advanced.localSocksPort
      if (localSocks > 0 && (localSocks === next.httpPort || localSocks === next.socksPort)) {
        throw new Error('端口不能与本地 SOCKS 端口相同')
      }
      this.mockSettings = { ...this.mockSettings, lanShare: next }
    }
    const { advanced, lanShare, ...rest } = patch
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
      if (adv.defaultCaptureMode !== undefined) {
        const m = adv.defaultCaptureMode
        if (m !== 'auto' && m !== 'process' && m !== 'tun' && m !== 'proxy') {
          throw new Error('加速模式只能是 auto、process、tun 或 proxy')
        }
      }
      if (adv.autoSwitchLossPct !== undefined && (adv.autoSwitchLossPct < 1 || adv.autoSwitchLossPct > 50)) {
        throw new Error('自动切换丢包阈值必须在 1–50% 之间')
      }
      if (adv.autoSwitchLatencyMs !== undefined && (adv.autoSwitchLatencyMs < 50 || adv.autoSwitchLatencyMs > 2000)) {
        throw new Error('自动切换延迟阈值必须在 50–2000 ms 之间')
      }
      if (adv.autoSwitchSustainSec !== undefined && (adv.autoSwitchSustainSec < 5 || adv.autoSwitchSustainSec > 120)) {
        throw new Error('自动切换持续时间必须在 5–120 秒之间')
      }
      if (adv.autoSwitchCooldownSec !== undefined && (adv.autoSwitchCooldownSec < 10 || adv.autoSwitchCooldownSec > 600)) {
        throw new Error('自动切换冷却时间必须在 10–600 秒之间')
      }
      if (adv.autoSwitchMaxSwitches !== undefined && (adv.autoSwitchMaxSwitches < 1 || adv.autoSwitchMaxSwitches > 10)) {
        throw new Error('自动切换次数必须在 1–10 之间')
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
    this.mockSettings = {
      ...this.mockSettings,
      advanced: {
        ...ADVANCED_DEFAULTS,
        defaultCaptureMode: 'auto' as CaptureMode,
        autoSwitchEnabled: false,
        autoSwitchLossPct: 5,
        autoSwitchLatencyMs: 200,
        autoSwitchSustainSec: 15,
        autoSwitchCooldownSec: 60,
        autoSwitchMaxSwitches: 3
      }
    }
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
    return structuredClone(this.gamesWithModes())
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
        games: structuredClone(this.gamesWithModes())
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
      games: structuredClone(this.gamesWithModes())
    }
  }

  async getAutoStart(): Promise<AutoStartState> {
    return { ...this.mockAutoStart }
  }

  async setAutoStart(enabled: boolean): Promise<AutoStartState> {
    this.mockAutoStart = { ...this.mockAutoStart, enabled }
    return { ...this.mockAutoStart }
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
          id: 'hosts',
          title: 'hosts 文件',
          status: 'warning',
          detail: 'hosts 中有 1 条记录改写了游戏/平台域名：第23行 104.16.0.1 store.steampowered.com',
          suggestion: '以管理员身份用记事本打开 C:\\Windows\\System32\\drivers\\etc\\hosts，删除这些行或在行首加 #，保存后运行 ipconfig /flushdns。如果是 Steam++（Watt Toolkit）等工具写入的，请先在该工具中关闭加速/恢复 hosts。',
          fixAction: null
        },
        {
          id: 'firewall',
          title: 'Windows 防火墙',
          status: 'ok',
          detail: '没有发现阻止 Game+ 或游戏的防火墙规则',
          suggestion: null,
          fixAction: null
        },
        {
          id: 'netfilterLoad',
          title: '进程模式驱动加载',
          status: 'ok',
          detail: '最近没有进程模式驱动加载失败',
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
      summary: '发现 1 个错误、2 个警告',
      ranAt: new Date().toISOString(),
      errorExplanation: opts.error ? '未能识别的错误，请参考下方检测结果' : null
    }
  }

  async fixDiagnostic(action: string): Promise<{ fixed: boolean }> {
    await new Promise((r) => setTimeout(r, 300))
    return { fixed: action === 'fixProxy' }
  }

  async exportDiagnosticBundle(_client: unknown): Promise<DiagnosticBundle> {
    await new Promise((r) => setTimeout(r, 400))
    const now = new Date()
    const pad = (n: number): string => String(n).padStart(2, '0')
    const fileName = `GamePlus-diagnostics-${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}.zip`
    // A valid, empty zip archive (end-of-central-directory record only).
    const base64 = 'UEsFBgAAAAAAAAAAAAAAAAAAAAAAAA=='
    return { fileName, base64, size: 22 }
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
    // LAN share endpoint is derived, never stored: only advertised while actually accelerating.
    const ls = this.mockSettings.lanShare
    this.status.lanShareEndpoint =
      ls.enabled && this.status.state === 'accelerating'
        ? `${ls.bindAddress || '192.168.1.23'}:${ls.httpPort}`
        : null
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
