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

export interface GameServerOption {
  id: string
  /** Region label, e.g. "亚服", "美服". */
  region: string
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

export interface AppSettings {
  language: 'zh-CN' | 'en-US'
  theme: 'light' | 'dark' | 'system'
  minimizeToTray: boolean
  smartNode: boolean
}

/** Engine-side settings persisted by the C# backend (subset of the full AppSettings). */
export interface EngineSettings {
  theme: 'light' | 'dark' | 'system'
  language: string
  minimizeToTray: boolean
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
