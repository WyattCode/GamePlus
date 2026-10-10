// Typed accessor for the preload bridge's window.gameplus.launch namespace.
// In a plain browser (vite preview) the bridge is absent; every function degrades gracefully
// instead of throwing, so the UI stays usable outside Electron.

interface LaunchBridge {
  launchGame(target: string): Promise<{ ok: boolean; error?: string }>
  createShortcut(gameId: string, gameName: string): Promise<{ ok: boolean; path?: string; error?: string }>
  pickExe(): Promise<string | null>
  onLaunchRequest(cb: (gameId: string) => void): () => void
}

function bridge(): LaunchBridge | null {
  if (typeof window === 'undefined') return null
  const gp = (window as unknown as { gameplus?: { launch?: LaunchBridge } }).gameplus
  return gp?.launch ?? null
}

/** Asks the main process to launch the game target (no shell, no extra arguments). */
export async function launchGame(target: string): Promise<{ ok: boolean; error?: string }> {
  const b = bridge()
  if (!b) return { ok: false, error: '仅桌面版可用' }
  return b.launchGame(target)
}

/** Creates the "<name> - Game+ 加速.lnk" desktop shortcut for the game. */
export async function createShortcut(
  gameId: string,
  gameName: string
): Promise<{ ok: boolean; path?: string; error?: string }> {
  const b = bridge()
  if (!b) return { ok: false, error: '仅桌面版可用' }
  return b.createShortcut(gameId, gameName)
}

/** Opens a native .exe file picker; returns the chosen path or null when cancelled/unavailable. */
export async function pickExe(): Promise<string | null> {
  const b = bridge()
  if (!b) return null
  return b.pickExe()
}

/** Subscribes to 'launch:request' events (--launch <gameId>); returns an unsubscribe function. */
export function onLaunchRequest(cb: (gameId: string) => void): () => void {
  const b = bridge()
  if (!b) return () => {}
  return b.onLaunchRequest(cb)
}

// --- launch-target validation (mirrors the C# backend's rules; no filesystem check here) ---

const STEAM_TARGET_RE = /^steam:\/\/rungameid\/\d{1,10}$/
const EPIC_TARGET_RE = /^com\.epicgames\.launcher:\/\/apps\/[A-Za-z0-9%:._-]{1,200}\?action=launch&silent=true$/

export const LAUNCH_TARGET_INVALID = '启动方式无效：只支持 Steam/Epic 启动链接或本机 .exe 路径'

/**
 * Validates a launch target. Returns null when the target is valid, otherwise a Chinese error.
 * Allowed forms: steam://rungameid/<1-10 digits>, the Epic launcher URL, or an absolute Windows
 * path to an .exe (drive letter, no UNC, no quotes/control chars, ≤ 260 chars). The .exe existence
 * check happens in the backend, not here.
 */
export function validateLaunchTarget(target: string): string | null {
  const t = (target ?? '').trim()
  if (!t) return LAUNCH_TARGET_INVALID
  if (STEAM_TARGET_RE.test(t) || EPIC_TARGET_RE.test(t)) return null
  if (
    t.length <= 260 &&
    /^[A-Za-z]:[\\/]/.test(t) &&
    t.toLowerCase().endsWith('.exe') &&
    !/["\x00-\x1f]/.test(t)
  ) {
    return null
  }
  return LAUNCH_TARGET_INVALID
}
