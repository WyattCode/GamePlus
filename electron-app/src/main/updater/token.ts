import { app, safeStorage } from 'electron'
import { existsSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'

// The optional GitHub token (for private repos / higher rate limits) is stored encrypted with the
// OS keychain via safeStorage. It is never persisted in prefs.json and never sent to the renderer.

function tokenPath(): string {
  return join(app.getPath('userData'), 'update-token.bin')
}

/** Stores (or clears) the GitHub token. Throws when secure storage is unavailable. */
export function setToken(token: string | null): void {
  const p = tokenPath()
  if (token === null || token.length === 0) {
    try {
      if (existsSync(p)) rmSync(p)
    } catch {
      // ignore
    }
    return
  }
  if (!safeStorage.isEncryptionAvailable()) {
    throw new Error('系统不支持安全存储，无法保存令牌')
  }
  const encrypted = safeStorage.encryptString(token)
  writeFileSync(p, encrypted)
}

/** Returns the stored token, or null when none is stored / decryption fails. */
export function getToken(): string | null {
  const p = tokenPath()
  if (!existsSync(p)) return null
  try {
    const buf = readFileSync(p)
    if (!safeStorage.isEncryptionAvailable()) return null
    const token = safeStorage.decryptString(buf)
    return token.length > 0 ? token : null
  } catch {
    return null
  }
}

/** True when a token is stored and decryptable. */
export function hasToken(): boolean {
  return getToken() !== null
}
