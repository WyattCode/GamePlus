import type { TrayInfo } from '@shared/types'

export type MiniLatencyKind = 'idle' | 'pending' | 'ok' | 'warn' | 'danger'

export interface MiniLatency {
  text: string
  kind: MiniLatencyKind
}

/**
 * Formats the latency shown in the mini window.
 *
 * - `idle` state → the caller-provided idle label (kind `idle`)
 * - `connecting` / `stopping`, or a non-positive latency → `—` (kind `pending`)
 * - otherwise `${latency} ms`, coloured by thresholds (ok < 80, warn < 150, danger)
 */
export function formatMiniLatency(
  state: TrayInfo['state'],
  latency: number,
  idleLabel: string
): MiniLatency {
  if (state === 'idle') return { text: idleLabel, kind: 'idle' }
  if (state === 'connecting' || state === 'stopping') return { text: '—', kind: 'pending' }
  if (!(latency > 0)) return { text: '—', kind: 'pending' }
  const kind: MiniLatencyKind = latency < 80 ? 'ok' : latency < 150 ? 'warn' : 'danger'
  return { text: `${latency} ms`, kind }
}
