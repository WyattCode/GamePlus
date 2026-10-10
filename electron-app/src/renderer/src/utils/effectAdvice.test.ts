import { describe, expect, it } from 'vitest'
import type { EffectSnapshot } from '@shared/types'
import { adviseBeforeAccel, adviseDuringAccel } from './effectAdvice'

function snap(latency: number, loss: number, jitter: number, samples: number): EffectSnapshot {
  return { latency, loss, jitter, samples }
}

describe('adviseBeforeAccel', () => {
  it('returns directGood for a good baseline', () => {
    const a = adviseBeforeAccel(snap(28, 0, 3, 5))
    expect(a).not.toBeNull()
    expect(a!.kind).toBe('directGood')
    expect(a!.title).toBe('网络良好，可不加速')
    expect(a!.detail).toContain('28 ms')
    expect(a!.detail).toContain('丢包 0%')
  })

  it('returns null for a poor baseline', () => {
    expect(adviseBeforeAccel(snap(140, 3.2, 18, 6))).toBeNull()
  })

  it('returns null when samples are insufficient', () => {
    expect(adviseBeforeAccel(snap(20, 0, 2, 2))).toBeNull()
  })

  it('returns null for failed latency (-1)', () => {
    expect(adviseBeforeAccel(snap(-1, 0, 2, 5))).toBeNull()
  })

  it('returns null for a null baseline', () => {
    expect(adviseBeforeAccel(null)).toBeNull()
  })

  it('treats the thresholds as inclusive boundaries', () => {
    expect(adviseBeforeAccel(snap(50, 0.5, 15, 3))).not.toBeNull()
  })

  it('rejects just past each boundary', () => {
    expect(adviseBeforeAccel(snap(50.1, 0, 10, 5))).toBeNull()
    expect(adviseBeforeAccel(snap(20, 0.6, 10, 5))).toBeNull()
    expect(adviseBeforeAccel(snap(20, 0, 15.1, 5))).toBeNull()
    // Sanity: 4 samples with good values still passes.
    expect(adviseBeforeAccel(snap(20, 0, 10, 4))).not.toBeNull()
  })
})

describe('adviseDuringAccel', () => {
  const base = snap(120, 2, 6, 5)

  it('returns notBetter when acceleration is no better than direct', () => {
    const a = adviseDuringAccel(base, snap(130, 3, 4, 12))
    expect(a).not.toBeNull()
    expect(a!.kind).toBe('notBetter')
    expect(a!.title).toBe('加速效果不明显')
    expect(a!.detail).toContain('直连 120 ms / 丢包 2%')
    expect(a!.detail).toContain('加速 130 ms / 丢包 3%')
  })

  it('returns null when acceleration is clearly better', () => {
    expect(adviseDuringAccel(base, snap(70, 0, 4, 12))).toBeNull()
  })

  it('treats the tolerance boundaries as inclusive', () => {
    // Latency exactly baseline - 5 and loss exactly baseline - 0.5 still counts as notBetter.
    expect(adviseDuringAccel(base, snap(115, 1.5, 4, 10))).not.toBeNull()
  })

  it('returns null when latency improves beyond the tolerance', () => {
    expect(adviseDuringAccel(base, snap(114, 3, 4, 10))).toBeNull()
  })

  it('returns null when loss improves beyond the tolerance', () => {
    expect(adviseDuringAccel(base, snap(130, 1.4, 4, 10))).toBeNull()
  })

  it('returns null with insufficient current samples', () => {
    expect(adviseDuringAccel(base, snap(130, 3, 4, 9))).toBeNull()
  })

  it('returns null with insufficient baseline samples', () => {
    expect(adviseDuringAccel(snap(120, 2, 6, 2), snap(130, 3, 4, 12))).toBeNull()
  })

  it('returns null for failed measurements (-1 latency)', () => {
    expect(adviseDuringAccel(snap(-1, 2, 6, 5), snap(130, 3, 4, 12))).toBeNull()
    expect(adviseDuringAccel(base, snap(-1, 3, 4, 12))).toBeNull()
  })

  it('returns null when either snapshot is missing', () => {
    expect(adviseDuringAccel(null, snap(130, 3, 4, 12))).toBeNull()
    expect(adviseDuringAccel(base, null)).toBeNull()
    expect(adviseDuringAccel(null, null)).toBeNull()
  })
})
