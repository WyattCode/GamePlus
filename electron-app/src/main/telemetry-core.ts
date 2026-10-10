// Pure helpers for telemetry: event whitelist + sanitization, endpoint validation and crash-report
// formatting. No electron import so this module can be unit-tested in plain Node.

import type { TelemetryEvent } from '../shared/types'

/** Maximum length of a sanitized `reason` field. */
export const MAX_REASON = 200

/** Maximum size of a crash report uploaded to the endpoint. */
export const MAX_CRASH_BYTES = 64 * 1024

const IPV4_RE = /\b\d{1,3}(?:\.\d{1,3}){3}\b/g
// IPv6: hex groups separated by colons, at least two groups (avoids matching plain "::").
const IPV6_RE = /\b(?:[0-9a-fA-F]{1,4}:){2,}[0-9a-fA-F]{0,4}\b/g
const URL_RE = /\b[a-zA-Z][a-zA-Z0-9+.-]*:\/\/[^\s"'<>]+/g
const WIN_PATH_RE = /\b[A-Za-z]:\\[^\s"'<>]*/g
// Hostname: label(.label)+ with a TLD-ish suffix. Conservative to avoid eating normal words.
const HOST_RE = /\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}\b/g

/**
 * Redacts addresses, hostnames/URLs, Windows paths and the user name from a free-form string.
 * Order matters: URLs first (they contain hostnames), then paths, then IPs, then bare hostnames.
 */
export function sanitizeReason(text: string, userName: string | null): string {
  let out = text
  out = out.replace(URL_RE, '<host>')
  out = out.replace(WIN_PATH_RE, '<path>')
  out = out.replace(IPV4_RE, '<ip>')
  out = out.replace(IPV6_RE, '<ip>')
  out = out.replace(HOST_RE, '<host>')
  if (userName && userName.length >= 3) {
    const re = new RegExp(escapeRegExp(userName), 'gi')
    out = out.replace(re, '<user>')
  }
  if (out.length > MAX_REASON) out = out.slice(0, MAX_REASON)
  return out
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Validates and normalizes a raw event against the whitelist. Returns null when the event is not
 * recognized or its required fields are missing/wrong-typed. Unknown keys are dropped.
 */
export function sanitizeEvent(raw: unknown, userName: string | null): TelemetryEvent | null {
  if (!raw || typeof raw !== 'object') return null
  const o = raw as Record<string, unknown>
  const type = o.type
  if (typeof type !== 'string') return null

  switch (type) {
    case 'accel_start': {
      if (typeof o.ok !== 'boolean') return null
      const ev: TelemetryEvent = { type: 'accel_start', ok: o.ok }
      if (o.mode === 'process' || o.mode === 'tun' || o.mode === 'proxy') ev.mode = o.mode
      if (typeof o.reason === 'string') ev.reason = sanitizeReason(o.reason, userName)
      if (typeof o.durationMs === 'number' && Number.isFinite(o.durationMs)) ev.durationMs = o.durationMs
      return ev
    }
    case 'accel_stop': {
      const ev: TelemetryEvent = { type: 'accel_stop' }
      if (typeof o.durationSec === 'number' && Number.isFinite(o.durationSec)) ev.durationSec = o.durationSec
      return ev
    }
    case 'auto_accel': {
      const kind = o.kind
      if (kind !== 'started' && kind !== 'stopped' && kind !== 'failed' && kind !== 'skipped') return null
      return { type: 'auto_accel', kind }
    }
    case 'diagnostics': {
      if (typeof o.failed !== 'number' || typeof o.warned !== 'number') return null
      return { type: 'diagnostics', failed: o.failed, warned: o.warned }
    }
    case 'app_start': {
      if (typeof o.version !== 'string') return null
      return { type: 'app_start', version: o.version.slice(0, 64) }
    }
    case 'crash': {
      const where = o.where
      if (where !== 'main' && where !== 'renderer' && where !== 'child') return null
      const ev: TelemetryEvent = { type: 'crash', where }
      if (typeof o.reason === 'string') ev.reason = sanitizeReason(o.reason, userName)
      return ev
    }
    default:
      return null
  }
}

/** True for an empty string, an https URL, or an http URL pointing at loopback. */
export function isAllowedEndpoint(url: string): boolean {
  if (url.length === 0) return true
  if (url.length > 2048) return false
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return false
  }
  if (parsed.protocol === 'https:') return true
  if (parsed.protocol !== 'http:') return false
  const host = parsed.hostname.toLowerCase()
  return host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]'
}

/** Formats a crash report in the same shape as the host's CrashReporter. */
export function formatCrashReport(opts: {
  source: string
  version: string
  platform: string
  arch: string
  extra?: string | null
  error?: unknown
  now?: Date
}): string {
  const now = opts.now ?? new Date()
  const lines: string[] = []
  lines.push('time: ' + now.toISOString())
  lines.push('source: ' + opts.source)
  lines.push('version: ' + opts.version)
  lines.push('os: ' + opts.platform)
  lines.push('arch: ' + opts.arch)
  if (opts.extra) lines.push('extra: ' + opts.extra)
  lines.push('')
  if (opts.error !== undefined && opts.error !== null) {
    lines.push(formatError(opts.error))
  }
  return lines.join('\n') + '\n'
}

function formatError(err: unknown): string {
  if (err instanceof Error) {
    let out = err.stack ?? `${err.name}: ${err.message}`
    let cause: unknown = (err as { cause?: unknown }).cause
    while (cause) {
      out += '\nCaused by: ' + (cause instanceof Error ? cause.stack ?? cause.message : String(cause))
      cause = cause instanceof Error ? (cause as { cause?: unknown }).cause : undefined
    }
    return out
  }
  return String(err)
}

/** Redacts the user profile path and user name from a crash report text (case-insensitive). */
export function redactCrashText(text: string, profile: string | null, userName: string | null): string {
  let out = text
  if (profile && profile.length > 0) {
    out = replaceAllCI(out, profile, '%USERPROFILE%')
    const alt = profile.replace(/\\/g, '/')
    if (alt !== profile) out = replaceAllCI(out, alt, '%USERPROFILE%')
  }
  if (userName && userName.length >= 3) {
    out = replaceAllCI(out, userName, '<user>')
  }
  return out
}

function replaceAllCI(haystack: string, needle: string, replacement: string): string {
  if (needle.length === 0) return haystack
  const re = new RegExp(escapeRegExp(needle), 'gi')
  return haystack.replace(re, replacement)
}
