// Advisory hints for the accelerate view: tell the user when acceleration is unnecessary.
// Purely informational — nothing here blocks starting or stopping acceleration.
import type { EffectSnapshot } from '@shared/types'

export type EffectAdviceKind = 'directGood' | 'notBetter'

export interface EffectAdvice {
  kind: EffectAdviceKind
  title: string
  detail: string
}

/** Minimum samples for a stored direct baseline to be trusted. */
export const ADVICE_MIN_BASELINE_SAMPLES = 3
/** A direct connection counts as "good enough" at or below these values. */
export const ADVICE_GOOD_LATENCY_MS = 50
export const ADVICE_GOOD_LOSS_PCT = 0.5
export const ADVICE_GOOD_JITTER_MS = 15
/** Minimum live samples before the during-acceleration comparison is trusted. */
export const ADVICE_MIN_CURRENT_SAMPLES = 10
/** Acceleration counts as "no better" when within these tolerances of the direct baseline. */
export const ADVICE_NOT_BETTER_LATENCY_MS = 5
export const ADVICE_NOT_BETTER_LOSS_PCT = 0.5

/**
 * Before accelerating: suggest skipping acceleration when the stored direct baseline is already
 * good (low latency/loss/jitter with enough samples). Returns null when no advice applies.
 */
export function adviseBeforeAccel(baseline: EffectSnapshot | null): EffectAdvice | null {
  if (!baseline) return null
  if (baseline.samples < ADVICE_MIN_BASELINE_SAMPLES) return null
  if (baseline.latency < 0) return null
  if (baseline.latency > ADVICE_GOOD_LATENCY_MS) return null
  if (baseline.loss > ADVICE_GOOD_LOSS_PCT) return null
  if (baseline.jitter > ADVICE_GOOD_JITTER_MS) return null
  return {
    kind: 'directGood',
    title: '网络良好，可不加速',
    detail: `直连 ${baseline.latency} ms · 丢包 ${baseline.loss}%，直连已足够流畅；需要时仍可加速。`
  }
}

/**
 * While accelerating: note when the accelerated line is no better than the direct baseline
 * (within the tolerances), so the user can switch lines or stop accelerating. Returns null
 * when the data is insufficient or acceleration is clearly better.
 */
export function adviseDuringAccel(
  baseline: EffectSnapshot | null,
  current: EffectSnapshot | null
): EffectAdvice | null {
  if (!baseline || !current) return null
  if (baseline.samples < ADVICE_MIN_BASELINE_SAMPLES || baseline.latency < 0) return null
  if (current.samples < ADVICE_MIN_CURRENT_SAMPLES || current.latency < 0) return null
  if (current.latency < baseline.latency - ADVICE_NOT_BETTER_LATENCY_MS) return null
  if (current.loss < baseline.loss - ADVICE_NOT_BETTER_LOSS_PCT) return null
  return {
    kind: 'notBetter',
    title: '加速效果不明显',
    detail: `直连 ${baseline.latency} ms / 丢包 ${baseline.loss}% → 加速 ${current.latency} ms / 丢包 ${current.loss}%。可尝试更换线路，或停止加速直接连接。`
  }
}
