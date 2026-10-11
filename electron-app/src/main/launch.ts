// Pure helpers for the 「加速并启动」 feature: CLI arg parsing, launch-target
// validation and desktop-shortcut name sanitising. Kept free of Electron
// imports at module top-level so they stay unit-testable.

/** Game ids accepted by `--launch`: letter/digit first, then letters/digits/._- (max 64 total). */
const GAME_ID_RE = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/

const STEAM_TARGET_RE = /^steam:\/\/rungameid\/\d{1,10}$/
const EPIC_TARGET_RE =
  /^com\.epicgames\.launcher:\/\/apps\/[A-Za-z0-9%:._-]{1,200}\?action=launch&silent=true$/

export const INVALID_TARGET_MESSAGE = '启动方式无效：只支持 Steam/Epic 启动链接或本机 .exe 路径'

export type LaunchTargetKind = 'steam' | 'epic' | 'exe'
export type LaunchTargetValidation =
  | { ok: true; kind: LaunchTargetKind }
  | { ok: false; error: string }

/** True when `id` matches the game id format accepted by `--launch`. */
export function isValidGameId(id: string): boolean {
  return GAME_ID_RE.test(id)
}

/**
 * Extracts the game id from `--launch <id>` / `--launch=<id>`.
 * Returns null when absent — or when a `--launch` argument is present but its
 * value is not a valid game id (injection attempts are rejected, not ignored).
 */
export function parseLaunchArg(argv: string[]): string | null {
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === '--launch') {
      const value = argv[i + 1]
      // A bare --launch must be followed by exactly one valid id; anything else
      // (missing value, injected shell text, a stray chromium flag) is rejected.
      return value !== undefined && isValidGameId(value) ? value : null
    }
    if (arg.startsWith('--launch=')) {
      const value = arg.slice('--launch='.length)
      return isValidGameId(value) ? value : null
    }
  }
  return null
}

/**
 * Validates a launch target. Only three forms are allowed:
 * 1. steam://rungameid/<1-10 digits>
 * 2. com.epicgames.launcher://apps/<id>?action=launch&silent=true
 * 3. an absolute Windows path to an existing .exe file (drive-letter, no UNC,
 *    no quotes, no control chars, max 260 chars) — existence via `exists`.
 */
export function validateLaunchTarget(
  target: string,
  exists: (p: string) => boolean
): LaunchTargetValidation {
  if (typeof target !== 'string' || target.length === 0) {
    return { ok: false, error: INVALID_TARGET_MESSAGE }
  }
  if (STEAM_TARGET_RE.test(target)) return { ok: true, kind: 'steam' }
  if (EPIC_TARGET_RE.test(target)) return { ok: true, kind: 'epic' }
  if (isWindowsExePath(target) && exists(target)) return { ok: true, kind: 'exe' }
  return { ok: false, error: INVALID_TARGET_MESSAGE }
}

/** Shape check for an absolute drive-letter Windows .exe path (no existence check). */
function isWindowsExePath(target: string): boolean {
  if (target.length < 3 || target.length > 260) return false
  // Drive-letter paths only — this also excludes UNC (\\server\share) paths.
  if (!/^[A-Za-z]:[\\/]/.test(target)) return false
  for (let i = 0; i < target.length; i++) {
    const code = target.charCodeAt(i)
    if (code < 0x20 || code === 0x7f) return false // control characters
    if (i === 1) continue // the drive colon is the only allowed ":"
    const ch = target[i]
    if (ch === '<' || ch === '>' || ch === '"' || ch === '|' || ch === '?' || ch === '*') {
      return false
    }
  }
  return /\.exe$/i.test(target)
}

/**
 * Sanitises a game name for use in a desktop-shortcut file name: strips
 * <>:"/\|?* and control characters, trims, caps at 60 chars; falls back to
 * `fallback` (the game id) when nothing usable remains.
 */
export function sanitizeShortcutName(name: string, fallback: string): string {
  const cleaned = name
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, '')
    .trim()
    .slice(0, 60)
    .trim()
  return cleaned.length > 0 ? cleaned : fallback
}
