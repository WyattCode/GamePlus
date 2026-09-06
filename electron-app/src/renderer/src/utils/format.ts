export function fmtLatency(ms: number): string {
  if (ms <= 0) return '—'
  return `${ms} ms`
}

export function fmtDelay(delay: number): string {
  if (delay === -1) return '—'
  if (delay === -2) return '超时'
  return `${delay} ms`
}

export function delayColor(delay: number): string {
  if (delay < 0) return 'var(--text-3)'
  if (delay <= 60) return 'var(--ok)'
  if (delay <= 120) return 'var(--warn)'
  return 'var(--danger)'
}

export function fmtDuration(sec: number): string {
  const h = Math.floor(sec / 3600)
  const m = Math.floor((sec % 3600) / 60)
  const s = sec % 60
  const pad = (n: number): string => n.toString().padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`
}

export function fmtRate(bps: number): string {
  if (bps <= 0) return '0 KB/s'
  if (bps < 1024 * 1024) return `${(bps / 1024).toFixed(0)} KB/s`
  return `${(bps / 1024 / 1024).toFixed(1)} MB/s`
}
