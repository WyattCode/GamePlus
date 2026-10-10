import type { TelemetryEvent } from '@shared/types'

// Best-effort telemetry hook. The main process applies the whitelist/sanitizer; the renderer must
// still only pass allowed fields (no game/node names, no paths, no IPs/domains).
// Never throws and is a no-op in the browser preview (no window.gameplus.telemetry).
export function track(event: TelemetryEvent): void {
  try {
    void window.gameplus?.telemetry?.record?.(event)
  } catch {
    // telemetry is best-effort — never surface errors to the user
  }
}
