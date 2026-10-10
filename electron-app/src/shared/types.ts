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
}

export type AccelState =
  | 'idle'
  | 'connecting'
  | 'accelerating'
  | 'stopping'

/** Which capture method is actually active. Null while idle. */
export type CaptureMode = 'process' | 'tun' | 'proxy'

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
  captureMode: CaptureMode | null
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

/** Engine-side settings persisted by the C# backend (subset of the full AppSettings). */
export interface EngineSettings {
  theme: 'light' | 'dark' | 'system'
  language: string
  minimizeToTray: boolean
  /** Remote game-rules URL; empty = the feature is disabled (no network traffic). */
  rulesUrl: string
}

/** Connection state of the C# backend sidecar. */
export type BackendState = 'starting' | 'connected' | 'disconnected' | 'error'

export type UpdateCheckStatus = 'upToDate' | 'updateAvailable' | 'failed' | 'notConfigured'

export interface UpdateInfo {
  status: UpdateCheckStatus
  /** Running version, e.g. "3.0.0". */
  current: string
  /** Latest version parsed from the update source, when available. */
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

/** UI preferences persisted by the Electron main process (userData/prefs.json). */
export interface UiPrefs {
  /** True once the user has completed (or skipped) the first-run onboarding. */
  onboardingDone: boolean
}
