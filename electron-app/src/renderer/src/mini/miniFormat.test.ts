import { describe, expect, it } from 'vitest'
import { formatMiniLatency } from './miniFormat'

const IDLE = '未加速'

describe('formatMiniLatency', () => {
  it('shows the idle label when idle', () => {
    expect(formatMiniLatency('idle', -1, IDLE)).toEqual({ text: IDLE, kind: 'idle' })
    expect(formatMiniLatency('idle', 42, IDLE)).toEqual({ text: IDLE, kind: 'idle' })
  })

  it('shows a placeholder while connecting or stopping', () => {
    expect(formatMiniLatency('connecting', 42, IDLE)).toEqual({ text: '—', kind: 'pending' })
    expect(formatMiniLatency('stopping', 42, IDLE)).toEqual({ text: '—', kind: 'pending' })
  })

  it('shows a placeholder for non-positive latency', () => {
    expect(formatMiniLatency('accelerating', 0, IDLE)).toEqual({ text: '—', kind: 'pending' })
    expect(formatMiniLatency('accelerating', -1, IDLE)).toEqual({ text: '—', kind: 'pending' })
  })

  it('formats accelerating latency with thresholds', () => {
    expect(formatMiniLatency('accelerating', 12, IDLE)).toEqual({ text: '12 ms', kind: 'ok' })
    expect(formatMiniLatency('accelerating', 79, IDLE)).toEqual({ text: '79 ms', kind: 'ok' })
    expect(formatMiniLatency('accelerating', 80, IDLE)).toEqual({ text: '80 ms', kind: 'warn' })
    expect(formatMiniLatency('accelerating', 149, IDLE)).toEqual({ text: '149 ms', kind: 'warn' })
    expect(formatMiniLatency('accelerating', 150, IDLE)).toEqual({ text: '150 ms', kind: 'danger' })
    expect(formatMiniLatency('accelerating', 320, IDLE)).toEqual({ text: '320 ms', kind: 'danger' })
  })
})
