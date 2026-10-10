import { describe, expect, it } from 'vitest'
import {
  formatCrashReport,
  isAllowedEndpoint,
  redactCrashText,
  sanitizeEvent,
  sanitizeReason,
  MAX_REASON
} from './telemetry-core'

describe('sanitizeEvent', () => {
  it('rejects unknown event types', () => {
    expect(sanitizeEvent({ type: 'unknown' }, null)).toBeNull()
    expect(sanitizeEvent({ type: 'accel_start' }, null)).toBeNull() // missing ok
    expect(sanitizeEvent(null, null)).toBeNull()
    expect(sanitizeEvent('nope', null)).toBeNull()
  })

  it('accepts a valid accel_start and drops extra keys', () => {
    const ev = sanitizeEvent(
      { type: 'accel_start', ok: true, mode: 'process', gameName: 'League of Legends', nodeName: 'HK-1' },
      null
    )
    expect(ev).toEqual({ type: 'accel_start', ok: true, mode: 'process' })
    expect((ev as Record<string, unknown>).gameName).toBeUndefined()
  })

  it('rejects an invalid mode', () => {
    const ev = sanitizeEvent({ type: 'accel_start', ok: true, mode: 'weird' }, null)
    expect(ev).toEqual({ type: 'accel_start', ok: true })
  })

  it('accepts auto_accel with a valid kind', () => {
    expect(sanitizeEvent({ type: 'auto_accel', kind: 'started' }, null)).toEqual({
      type: 'auto_accel',
      kind: 'started'
    })
    expect(sanitizeEvent({ type: 'auto_accel', kind: 'nope' }, null)).toBeNull()
  })

  it('accepts diagnostics with numeric fields', () => {
    expect(sanitizeEvent({ type: 'diagnostics', failed: 1, warned: 2 }, null)).toEqual({
      type: 'diagnostics',
      failed: 1,
      warned: 2
    })
    expect(sanitizeEvent({ type: 'diagnostics', failed: 'x', warned: 2 }, null)).toBeNull()
  })

  it('accepts app_start and crash', () => {
    expect(sanitizeEvent({ type: 'app_start', version: '3.0.0' }, null)).toEqual({
      type: 'app_start',
      version: '3.0.0'
    })
    expect(sanitizeEvent({ type: 'crash', where: 'main', reason: 'boom' }, null)).toEqual({
      type: 'crash',
      where: 'main',
      reason: 'boom'
    })
    expect(sanitizeEvent({ type: 'crash', where: 'nope' }, null)).toBeNull()
  })
})

describe('sanitizeReason', () => {
  it('redacts IPv4 addresses', () => {
    expect(sanitizeReason('connect to 192.168.1.10 failed', null)).toBe('connect to <ip> failed')
  })

  it('redacts IPv6 addresses', () => {
    expect(sanitizeReason('addr 2001:db8:85a3::8a2e:370:7334 unreachable', null)).toContain('<ip>')
  })

  it('redacts URLs and hostnames', () => {
    expect(sanitizeReason('GET https://api.example.com/v1 failed', null)).toBe('GET <host> failed')
    expect(sanitizeReason('host game.example.org refused', null)).toBe('host <host> refused')
  })

  it('redacts Windows paths', () => {
    expect(sanitizeReason('open C:\\Users\\Alice\\game.exe failed', null)).toBe('open <path> failed')
  })

  it('redacts the user name case-insensitively', () => {
    expect(sanitizeReason('user ALICE failed', 'alice')).toBe('user <user> failed')
  })

  it('truncates to MAX_REASON', () => {
    const long = 'x'.repeat(MAX_REASON + 50)
    expect(sanitizeReason(long, null).length).toBe(MAX_REASON)
  })
})

describe('isAllowedEndpoint', () => {
  it('allows empty and https', () => {
    expect(isAllowedEndpoint('')).toBe(true)
    expect(isAllowedEndpoint('https://telemetry.example.com/ingest')).toBe(true)
  })

  it('allows http only for loopback', () => {
    expect(isAllowedEndpoint('http://localhost:8080/x')).toBe(true)
    expect(isAllowedEndpoint('http://127.0.0.1:8080/x')).toBe(true)
    expect(isAllowedEndpoint('http://example.com/x')).toBe(false)
  })

  it('rejects other schemes and over-long urls', () => {
    expect(isAllowedEndpoint('ftp://example.com')).toBe(false)
    expect(isAllowedEndpoint('not a url')).toBe(false)
    expect(isAllowedEndpoint('https://example.com/' + 'a'.repeat(2100))).toBe(false)
  })
})

describe('formatCrashReport', () => {
  it('includes time, source, version and error', () => {
    const text = formatCrashReport({
      source: 'uncaughtException',
      version: '3.0.0',
      platform: 'win32',
      arch: 'x64',
      error: new Error('boom'),
      now: new Date('2024-01-01T00:00:00Z')
    })
    expect(text).toContain('time: 2024-01-01T00:00:00.000Z')
    expect(text).toContain('source: uncaughtException')
    expect(text).toContain('version: 3.0.0')
    expect(text).toContain('boom')
  })
})

describe('redactCrashText', () => {
  it('replaces the profile path and user name case-insensitively', () => {
    const text = 'C:\\Users\\Alice\\x and c:\\users\\alice\\y and ALICE'
    const out = redactCrashText(text, 'C:\\Users\\Alice', 'Alice')
    expect(out).not.toContain('Alice')
    expect(out).toContain('%USERPROFILE%')
    expect(out).toContain('<user>')
  })

  it('ignores short user names', () => {
    expect(redactCrashText('ab', null, 'ab')).toBe('ab')
  })
})
