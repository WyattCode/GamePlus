import { describe, expect, it } from 'vitest'
import {
  INVALID_TARGET_MESSAGE,
  isValidGameId,
  parseLaunchArg,
  sanitizeShortcutName,
  validateLaunchTarget
} from './launch'

describe('parseLaunchArg', () => {
  it('accepts --launch <id>', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch', 'abc123'])).toBe('abc123')
  })

  it('accepts --launch=<id>', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch=abc_1.x'])).toBe('abc_1.x')
  })

  it('handles second-instance argv with extra chromium flags', () => {
    expect(
      parseLaunchArg([
        'C:\\Program Files\\Game+\\Game+.exe',
        '--hidden',
        '--flag-switched-beginning-of-args',
        '--launch',
        'game-9',
        '--no-sandbox'
      ])
    ).toBe('game-9')
  })

  it('returns null when --launch is absent', () => {
    expect(parseLaunchArg([])).toBeNull()
    expect(parseLaunchArg(['Game+.exe'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--hidden'])).toBeNull()
  })

  it('returns null when --launch has no value', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch='])).toBeNull()
  })

  it('rejects injected shell text', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch', 'x & calc'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch=x & calc'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', 'foo; rm -rf /'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', 'a\nb'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', '..\\evil.exe'])).toBeNull()
  })

  it('rejects ids that fail the game id format', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch', '_leading'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', '.dot'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', 'has space'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launch', 'a'.repeat(65)])).toBeNull()
  })

  it('accepts the maximum length id', () => {
    expect(parseLaunchArg(['Game+.exe', '--launch', 'a'.repeat(64)])).toBe('a'.repeat(64))
  })

  it('does not confuse near-miss flags with --launch', () => {
    expect(parseLaunchArg(['Game+.exe', '--launchx', 'abc'])).toBeNull()
    expect(parseLaunchArg(['Game+.exe', '--launchx=abc'])).toBeNull()
  })
})

describe('isValidGameId', () => {
  it('accepts valid ids', () => {
    expect(isValidGameId('a')).toBe(true)
    expect(isValidGameId('9')).toBe(true)
    expect(isValidGameId('game_1.x-2')).toBe(true)
    expect(isValidGameId('A'.repeat(64))).toBe(true)
  })

  it('rejects invalid ids', () => {
    expect(isValidGameId('')).toBe(false)
    expect(isValidGameId('_lead')).toBe(false)
    expect(isValidGameId('-lead')).toBe(false)
    expect(isValidGameId('.lead')).toBe(false)
    expect(isValidGameId('has space')).toBe(false)
    expect(isValidGameId('x&calc')).toBe(false)
    expect(isValidGameId('a'.repeat(65))).toBe(false)
  })
})

describe('validateLaunchTarget', () => {
  const existsTrue = (): boolean => true
  const existsFalse = (): boolean => false

  it('accepts steam rungameid links', () => {
    expect(validateLaunchTarget('steam://rungameid/570', existsTrue)).toEqual({
      ok: true,
      kind: 'steam'
    })
    expect(validateLaunchTarget('steam://rungameid/1234567890', existsTrue)).toEqual({
      ok: true,
      kind: 'steam'
    })
  })

  it('rejects malformed steam links', () => {
    expect(validateLaunchTarget('steam://rungameid/12345678901', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('steam://rungameid/12a', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('steam://rungameid/', existsTrue).ok).toBe(false)
    expect(
      validateLaunchTarget('steam://rungameid/570?extra=1', existsTrue).ok
    ).toBe(false)
    expect(validateLaunchTarget('Steam://rungameid/570', existsTrue).ok).toBe(false)
  })

  it('accepts epic launch links', () => {
    expect(
      validateLaunchTarget(
        'com.epicgames.launcher://apps/Owl%3AApp?action=launch&silent=true',
        existsTrue
      )
    ).toEqual({ ok: true, kind: 'epic' })
    expect(
      validateLaunchTarget('com.epicgames.launcher://apps/abc._-9?action=launch&silent=true', existsTrue)
    ).toEqual({ ok: true, kind: 'epic' })
  })

  it('rejects malformed epic links', () => {
    expect(
      validateLaunchTarget(
        'com.epicgames.launcher://apps/abc?action=launch',
        existsTrue
      ).ok
    ).toBe(false)
    expect(
      validateLaunchTarget(
        'com.epicgames.launcher://apps/abc?action=launch&silent=true&x=1',
        existsTrue
      ).ok
    ).toBe(false)
    expect(
      validateLaunchTarget(
        'com.epicgames.launcher://apps/a b?action=launch&silent=true',
        existsTrue
      ).ok
    ).toBe(false)
    expect(
      validateLaunchTarget(
        'com.epicgames.launcher://apps/' + 'a'.repeat(201) + '?action=launch&silent=true',
        existsTrue
      ).ok
    ).toBe(false)
  })

  it('accepts an existing .exe path', () => {
    expect(validateLaunchTarget('C:\\Games\\My Game\\game.exe', existsTrue)).toEqual({
      ok: true,
      kind: 'exe'
    })
    expect(validateLaunchTarget('c:/games/game.EXE', existsTrue)).toEqual({
      ok: true,
      kind: 'exe'
    })
  })

  it('rejects a non-existent .exe path', () => {
    expect(validateLaunchTarget('C:\\Games\\game.exe', existsFalse).ok).toBe(false)
  })

  it('rejects UNC, relative and non-exe paths', () => {
    expect(validateLaunchTarget('\\\\server\\share\\game.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('Games\\game.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game.bat', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game.exe.txt', existsTrue).ok).toBe(false)
  })

  it('rejects paths with quotes, control chars or forbidden characters', () => {
    expect(validateLaunchTarget('C:\\Games\\"game".exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game\n.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game|.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game?.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game*.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:\\Games\\game>.exe', existsTrue).ok).toBe(false)
    expect(validateLaunchTarget('C:' + '\\a'.repeat(200) + '.exe', existsTrue).ok).toBe(false)
  })

  it('rejects empty targets with the standard message', () => {
    const result = validateLaunchTarget('', existsTrue)
    expect(result).toEqual({ ok: false, error: INVALID_TARGET_MESSAGE })
  })

  it('reports the standard message for every rejection', () => {
    expect(validateLaunchTarget('C:\\nope.exe', existsFalse)).toEqual({
      ok: false,
      error: INVALID_TARGET_MESSAGE
    })
  })
})

describe('sanitizeShortcutName', () => {
  it('strips forbidden filename characters and control characters', () => {
    expect(sanitizeShortcutName('DOOM<>:"/\\|?* Eternal', 'id')).toBe('DOOM Eternal')
    expect(sanitizeShortcutName('Ga\u0000me', 'id')).toBe('Game')
  })

  it('trims whitespace', () => {
    expect(sanitizeShortcutName('  我的游戏  ', 'id')).toBe('我的游戏')
  })

  it('caps at 60 characters', () => {
    expect(sanitizeShortcutName('a'.repeat(100), 'id')).toHaveLength(60)
  })

  it('falls back to the id when nothing usable remains', () => {
    expect(sanitizeShortcutName('', 'game1')).toBe('game1')
    expect(sanitizeShortcutName('***', 'game1')).toBe('game1')
    expect(sanitizeShortcutName('   ', 'game1')).toBe('game1')
  })
})
