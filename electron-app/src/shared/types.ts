// Domain model shared between the Electron main process, preload bridge and Vue renderer.
// Mirrors the C# Netch.Core models so the headless C# sidecar can serialize straight into these.

export type ServerKind =
  | 'vless'
  | 'vmess'
  | 'trojan'
  | 'ss'
  | 'socks5'
  | 'hysteria2'
  | 'wireguard'

export interface Server {
  id: string
  kind: ServerKind
  /** Short protocol tag for the UI chip, e.g. "SS", "VLESS", "HY2". */
  type: string
  remark: string
  hostname: string
  port: number
  /** Subscription tag; "NONE" means a manually-added node. */
  group: string
  /** Latency in ms; -1 = untested, -2 = timeout. */
  delay: number
}

/** A concrete capture method (no "auto"): process capture, TUN IP capture or system proxy. */
export type CaptureModeKind = 'process' | 'tun' | 'proxy'

/** User-selectable acceleration mode: "auto" plus the three concrete capture methods. */
export type CaptureMode = 'auto' | CaptureModeKind

export interface Game {
  id: string
  name: string
  /** Emoji or short glyph shown on the tile. */
  icon: string
  /** Hex accent used for the tile gradient. */
  accent: string
  regionText: string
  ruleCount: number
  categories: string[]
  favorite: boolean
  /** True for user-added games (sorted to the top). */
  custom: boolean
  // --- editor fields (present for custom games; optional for seed data) ---
  englishName?: string
  aliases?: string[]
  category?: string
  regions?: string[]
  platforms?: string[]
  proxyDomains?: string[]
  proxyIps?: string[]
  proxyProcesses?: string[]
  /** True when this game is in the auto-acceleration list. */
  autoAccelerate?: boolean
  /** True when the game has both 国服 and 国际服, so the user can pick a server. */
  hasServerChoice?: boolean
  /** Per-game server choice; only meaningful when hasServerChoice is true. */
  serverChoice?: 'cn' | 'intl' | null
  /** Per-game capture-mode override; null/undefined = follow the global default. */
  captureMode?: CaptureMode | null
  /** Capture methods available for this game's rules (subset of process/tun/proxy). */
  availableModes?: CaptureModeKind[]
}

export type AccelState =
  | 'idle'
  | 'connecting'
  | 'accelerating'
  | 'stopping'

/** Which capture method is actually active. Null while idle. */
export type ActiveCaptureMode = CaptureModeKind

export interface AccelStatus {
  state: AccelState
  latency: number
  loss: number
  jitter: number
  /** Seconds since acceleration started. */
  durationSec: number
  downloadBps: number
  uploadBps: number
  gameId: string | null
  nodeId: string | null
  /** Active capture method, or null when idle. */
  captureMode: ActiveCaptureMode | null
}

export interface Subscription {
  id: string
  remark: string
  link: string
  serverCount: number
  lastUpdated: string | null
}

export type LogLevel = 'debug' | 'info' | 'warning' | 'error'

export interface LogEntry {
  ts: string
  level: LogLevel
  message: string
}

/** How an extra application's traffic is treated in process-capture mode. */
export type ExtraAppMode = 'proxy' | 'bypass'

/** An extra application additionally proxied or explicitly bypassed (process mode only). */
export interface ExtraApp {
  name: string
  /** Executable file name, e.g. "Discord.exe". */
  process: string
  mode: ExtraAppMode
  enabled: boolean
}

/** Advanced engine options (TUN DNS, local port, routing/measurement tuning). */
export interface AdvancedSettings {
  /** DNS server used in TUN mode; IP, optionally with a port. */
  tunDns: string
  /** Measure the direct-connection baseline before every acceleration. */
  measureEffect: boolean
  /** Latency penalty (ms) applied to nodes outside the game's region. */
  regionPenaltyMs: number
  /** Fixed local SOCKS5 port; 0 = pick a free port automatically. */
  localSocksPort: number
  /** Global default acceleration mode; per-game overrides take precedence. */
  defaultCaptureMode: CaptureMode
  /** Master switch for automatic node switching on sustained bad quality. */
  autoSwitchEnabled: boolean
  /** Loss percentage threshold (1–50); loss >= this counts as a bad sample. */
  autoSwitchLossPct: number
  /** Latency threshold in ms (50–2000); latency >= this (or < 0 = timeout) counts as bad. */
  autoSwitchLatencyMs: number
  /** Sustain window in seconds (5–120); bad samples must span this long to trigger. */
  autoSwitchSustainSec: number
  /** Cooldown after a switch in seconds (10–600) before evaluating again. */
  autoSwitchCooldownSec: number
  /** Maximum switches per acceleration session (1–10). */
  autoSwitchMaxSwitches: number
}

/** Engine-side settings persisted by the C# backend (subset of the full AppSettings). */
export interface EngineSettings {
  theme: 'light' | 'dark' | 'system'
  language: string
  minimizeToTray: boolean
  /** Remote game-rules URL; empty = the feature is disabled (no network traffic). */
  rulesUrl: string
  /** Global master switch for auto-acceleration on game launch. */
  autoAccelerate: boolean
  /** Automatic subscription refresh interval in hours; 0 = disabled (allowed: 0, 6, 12, 24, 48). */
  subscriptionAutoUpdateHours: number
  /** Extra applications for per-app split routing (process mode only). */
  extraApps: ExtraApp[]
  /** Advanced engine options. */
  advanced: AdvancedSettings
}

/**
 * Patch accepted by settings.set: any top-level field, plus a partial `advanced` object so callers
 * can update a single advanced field without resending (and re-validating) the whole block.
 */
export type SettingsPatch = Partial<Omit<EngineSettings, 'advanced'>> & {
  advanced?: Partial<AdvancedSettings>
}

/** Result of importing nodes (share links, subscription blob, Clash YAML or a subscription URL). */
export interface ImportResult {
  added: number
  duplicates: number
  invalid: number
  /** Detected payload format: "subscription" | "clash" | "links" | "base64". */
  format: string
  /** Full server list after the import. */
  servers: Server[]
}

/** Connection state of the C# backend sidecar. */
export type BackendState = 'starting' | 'connected' | 'disconnected' | 'error'

export type UpdateCheckStatus = 'upToDate' | 'updateAvailable' | 'failed' | 'notConfigured'

export interface UpdateInfo {
  status: UpdateCheckStatus
  /** Running version, e.g. "3.0.0". */
  current: string
  /** Latest version parsed from the update source. */
  latest: string | null
  /** Raw tag from the update source, when available. */
  tag: string | null
  /** Release page URL, when available. */
  url: string | null
  /** Short user-facing reason when status is "failed" or "notConfigured". */
  error: string | null
}

/** Aggregated latency/loss/jitter for a set of samples. */
export interface EffectSnapshot {
  /** Mean latency in ms; -1 when no sample succeeded. */
  latency: number
  /** Loss percentage (1 decimal). */
  loss: number
  /** Population std-dev of successful samples, ms. */
  jitter: number
  samples: number
}

/** One accelerated sample point. */
export interface EffectPoint {
  /** Unix ms timestamp. */
  ts: number
  /** Measured latency in ms; negative = failure. */
  accel: number
}

/** Before/after acceleration measurement against the game's own target. */
export interface EffectReport {
  hasTarget: boolean
  target: string | null
  baseline: EffectSnapshot | null
  current: EffectSnapshot | null
  /** Latency improvement percentage; null when there isn't enough data. */
  latencyPct: number | null
  summary: string
  series: EffectPoint[]
  measuring: boolean
  /** ISO timestamp of when the direct baseline was measured; null when unknown. */
  baselineMeasuredAt?: string | null
  /** True when the baseline was reused from a previous measurement instead of measured now. */
  baselineFromCache?: boolean
}

/** One ranked candidate node from the smart-routing ranker. */
export interface RankedNode {
  id: string
  remark: string
  /** Short protocol tag, e.g. "SS", "HY2". */
  type: string
  /** Latency in ms; -1 = untested, < -1 = failed. */
  delay: number
  /** Detected region code, e.g. "HK"; null when unknown. */
  region: string | null
  /** Chinese region name, e.g. "香港"; null when unknown. */
  regionName: string | null
  /** True when the node is in one of the game's preferred regions. */
  regionMatch: boolean
  /** Ranking score (delay + region penalty); lower is better. */
  score: number
  /** Short Chinese note, e.g. "区服匹配". */
  note: string
}

/** Result of ranking the node pool for a game. */
export interface NodeRanking {
  recommendedId: string | null
  reason: string
  preferenceLabel: string | null
  preferredRegions: string[]
  candidates: RankedNode[]
  /** Per-game server choice ("cn" | "intl"); null for games without a 国服/国际服 choice. */
  serverChoice?: 'cn' | 'intl' | null
}

export type DiagnosticStatus = 'ok' | 'warning' | 'error' | 'skipped'

/** One diagnostic check result. */
export interface DiagnosticItem {
  id: string
  title: string
  status: DiagnosticStatus
  detail: string
  suggestion: string | null
  /** Machine id for an optional one-click fix (currently only "fixProxy"). */
  fixAction: string | null
}

/** Result of a diagnostics run. */
export interface DiagnosticReport {
  items: DiagnosticItem[]
  summary: string
  /** ISO timestamp of the run. */
  ranAt: string
  /** Plain-Chinese explanation of the last error, when one was supplied. */
  errorExplanation: string | null
}

/** A one-click diagnostic bundle produced by the backend and saved by the client. */
export interface DiagnosticBundle {
  fileName: string
  base64: string
  size: number
}

/** Where the client auto-updater fetches releases from. */
export type UpdateSource = 'none' | 'manifest' | 'github'

/** Client auto-update configuration (persisted in UiPrefs). */
export interface UpdateConfig {
  source: UpdateSource
  /** Direct URL to a JSON manifest (source === 'manifest'). */
  manifestUrl: string
  /** GitHub repository in "owner/repo" form (source === 'github'). */
  githubRepo: string
  /** Include GitHub prereleases when picking the latest release. */
  includePrerelease: boolean
  /** Check for updates automatically after startup and periodically. */
  autoCheck: boolean
}

/** Update config as exposed to the renderer, plus whether a GitHub token is stored. */
export interface UpdateConfigView extends UpdateConfig {
  hasToken: boolean
}

export type UpdateStatus =
  | 'disabled'
  | 'idle'
  | 'checking'
  | 'upToDate'
  | 'available'
  | 'downloading'
  | 'ready'
  | 'error'

/** Latest release metadata surfaced to the renderer. */
export interface UpdateLatest {
  version: string
  notes: string
  publishedAt?: string
  size?: number
}

/** Download progress in bytes. */
export interface UpdateProgress {
  received: number
  total: number
}

/** Full auto-update state pushed to the renderer on every change. */
export interface UpdateState {
  status: UpdateStatus
  /** Running app version. */
  current: string
  latest?: UpdateLatest
  progress?: UpdateProgress
  error?: string
  /** ISO timestamp of the last completed check. */
  lastCheck?: string
}

/** Telemetry configuration (persisted in UiPrefs). */
export interface TelemetryConfig {
  /** Master switch. Off by default; nothing is collected or uploaded when false. */
  enabled: boolean
  /** Upload endpoint; empty = never send. */
  endpoint: string
  /** Random install id, only present when telemetry is enabled. */
  installId: string | null
}

/** One whitelisted telemetry event (as stored locally / uploaded). */
export type TelemetryEvent =
  | { type: 'accel_start'; ok: boolean; mode?: CaptureModeKind; reason?: string; durationMs?: number }
  | { type: 'accel_stop'; durationSec?: number }
  | { type: 'auto_accel'; kind: 'started' | 'stopped' | 'failed' | 'skipped' }
  | { type: 'diagnostics'; failed: number; warned: number }
  | { type: 'app_start'; version: string }
  | { type: 'crash'; where: 'main' | 'renderer' | 'child'; reason?: string }
  | { type: string; ts?: string; v?: string; [k: string]: unknown }

/** One local crash report file. */
export interface CrashFileInfo {
  name: string
  where: 'main' | 'host'
  size: number
  mtime: number
}

/** UI preferences persisted by the Electron main process (userData/prefs.json). */
export interface UiPrefs {
  /** True once the user has completed (or skipped) the first-run onboarding. */
  onboardingDone: boolean
  /** True when the app was launched at login and should start minimized to the tray. */
  startHidden: boolean
  /** True once the old Electron login-item has been migrated to the scheduled task. */
  migratedLoginItem: boolean
  /** Client auto-update configuration. */
  update: UpdateConfig
  /** Telemetry configuration. */
  telemetry: TelemetryConfig
}

/** One game found by scanning the local machine's launchers/registries. */
export interface DetectedGame {
  /** Launcher the game was found in, e.g. "Steam", "Epic", "Battle.net". */
  source: string
  /** Launcher-specific id (appid, catalog item id, …). */
  sourceId: string
  /** Name as reported by the launcher. */
  name: string
  /** Install directory, when known. */
  installDir: string | null
  /** Executable file names (no path) that identify the game. */
  executables: string[]
  /** Matched library game id, or null when the game is not in the library yet. */
  gameId: string | null
  /** Matched library game name, or null when unmatched. */
  gameName: string | null
  /** How the match was made: "process" (进程) or "name" (名称); null when unmatched. */
  matchedBy: 'process' | 'name' | null
}

/** Result of a local game-library scan. */
export interface LibraryScanResult {
  games: DetectedGame[]
  /** Non-fatal errors (unreadable registry keys, missing launchers, …). */
  errors: string[]
  /** ISO timestamp of the scan. */
  scannedAt: string
}

/** Where the active game-rules set came from. */
export type RulesSource = 'bundled' | 'cached' | 'remote'

/** Current state of the game-rules library. */
export interface RulesStatus {
  version: string
  source: RulesSource
  gameCount: number
  /** ISO timestamp of the last successful check, or null. */
  lastCheck: string | null
  /** Short user-facing reason for the last failure, or null. */
  lastError: string | null
  /** Short user-facing result of the last check, or null. */
  lastResult: string | null
  /** Configured remote URL; empty = disabled. */
  url: string
}

/** Result of a rules update check. */
export interface RulesUpdateResult {
  status: 'updated' | 'upToDate' | 'failed'
  message: string
  version: string
  /** Full game list after the update (unchanged when status is not "updated"). */
  games: Game[]
}

/** Push event emitted by the backend when auto-acceleration reacts to a game process. */
export interface AutoAccelEvent {
  kind: 'started' | 'stopped' | 'failed' | 'skipped'
  gameId: string | null
  gameName: string
  message: string
}

/** Push event emitted when the auto node switcher reacts to sustained bad line quality. */
export interface AutoSwitchEvent {
  kind: 'switched' | 'failed' | 'exhausted'
  gameId: string
  /** Node display names/remarks; null when unknown. */
  fromNode: string | null
  toNode: string | null
  /** Chinese description of the trigger, e.g. "丢包 8% ≥ 5%，持续 15 秒". */
  reason: string
  /** Extra failure detail for kind === 'failed'; null otherwise. */
  message: string | null
}

/** State of the "launch at login" scheduled task, reported by the C# host. */
export interface AutoStartState {
  enabled: boolean
  /** False when the app is not running from an installed location. */
  supported: boolean
  /** Short user-facing reason when unsupported (or null). */
  reason: string | null
}

/** Tray status snapshot pushed from the renderer to the main process. */
export interface TrayInfo {
  state: AccelState
  gameName: string | null
  latency: number
}

/** Quick action triggered from the tray context menu. */
export type TrayAction = 'toggle' | 'diagnose'

/** One completed acceleration session (per-game history). */
export interface AccelSession {
  id: string
  gameId: string
  gameName: string
  /** Node display name (Remark); never a host/port. */
  nodeName: string | null
  /** Capture mode used, e.g. "process" / "tun" / "proxy". */
  mode: string | null
  /** Per-game server choice ("cn" | "intl"); null when the game has no choice. */
  serverChoice: string | null
  /** ISO timestamp of when acceleration started. */
  startedAt: string
  /** ISO timestamp of when acceleration ended; null while still running. */
  endedAt: string | null
  durationSec: number
  /** Direct-connection baseline latency in ms; null when not measured. */
  baselineMs: number | null
  /** Accelerated latency in ms; null when not measured. */
  acceleratedMs: number | null
  /** Latency improvement percentage; null when not enough data. */
  latencyPct: number | null
  /** One-line Chinese summary of the effect measurement. */
  summary: string | null
}
