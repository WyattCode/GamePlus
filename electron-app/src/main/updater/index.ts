import { app, BrowserWindow } from 'electron'
import { createHash } from 'crypto'
import { createReadStream, createWriteStream, existsSync, mkdirSync, renameSync, rmSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { spawn } from 'child_process'
import { getPrefs, setPrefs } from '../prefs'
import { getToken } from './token'
import { ManifestSource, GithubSource } from './sources'
import { compareVersions, type UpdateRelease } from './core'
import type { UpdateState, UpdateConfig, UpdateConfigView } from '../../shared/types'

// Client-side auto-update orchestration. Updates are completely disabled unless the user configures
// a source. State is pushed to the renderer via 'update:state' on every change.

const MAX_DOWNLOAD_BYTES = 500 * 1024 * 1024
const PROGRESS_INTERVAL_MS = 250
const AUTO_CHECK_DELAY_MS = 30_000
const AUTO_CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000

interface Source {
  latest(): Promise<UpdateRelease>
}

export class Updater {
  private state: UpdateState = { status: 'disabled', current: app.getVersion() }
  private release: UpdateRelease | null = null
  private downloadedPath: string | null = null
  private abort: AbortController | null = null
  private lastProgressAt = 0
  private autoTimer: ReturnType<typeof setTimeout> | null = null
  private autoInterval: ReturnType<typeof setInterval> | null = null

  constructor() {
    this.refreshDisabledState()
  }

  /** Current state snapshot (a copy). */
  getState(): UpdateState {
    return { ...this.state }
  }

  /** Config as exposed to the renderer, including whether a GitHub token is stored. */
  getConfig(): UpdateConfigView {
    const cfg = getPrefs().update
    return { ...cfg, hasToken: getToken() !== null }
  }

  /** Applies a validated config patch and re-arms the auto-check timer. */
  setConfig(patch: Partial<UpdateConfig>): UpdateConfigView {
    setPrefs({ update: patch as UpdateConfig })
    this.refreshDisabledState()
    this.armAutoCheck()
    return this.getConfig()
  }

  /** Re-arms the auto-check timer based on the current config. */
  armAutoCheck(): void {
    this.clearAutoCheck()
    const cfg = getPrefs().update
    if (cfg.source === 'none' || !cfg.autoCheck) return
    this.autoTimer = setTimeout(() => {
      this.check().catch(() => undefined)
      this.autoInterval = setInterval(() => {
        this.check().catch(() => undefined)
      }, AUTO_CHECK_INTERVAL_MS)
    }, AUTO_CHECK_DELAY_MS)
  }

  private clearAutoCheck(): void {
    if (this.autoTimer) {
      clearTimeout(this.autoTimer)
      this.autoTimer = null
    }
    if (this.autoInterval) {
      clearInterval(this.autoInterval)
      this.autoInterval = null
    }
  }

  /** Called on app quit so no timer keeps the process alive. */
  dispose(): void {
    this.clearAutoCheck()
    this.cancel()
  }

  private refreshDisabledState(): void {
    const cfg = getPrefs().update
    if (cfg.source === 'none') {
      this.setState({ status: 'disabled', current: app.getVersion() })
    } else if (this.state.status === 'disabled') {
      this.setState({ status: 'idle', current: app.getVersion() })
    }
  }

  private setState(patch: Partial<UpdateState>): void {
    this.state = { ...this.state, ...patch }
    this.emit()
  }

  private emit(): void {
    const payload = { ...this.state }
    for (const w of BrowserWindow.getAllWindows()) {
      if (!w.isDestroyed()) w.webContents.send('update:state', payload)
    }
  }

  private makeSource(cfg: UpdateConfig): Source | null {
    if (cfg.source === 'manifest') {
      if (!cfg.manifestUrl) return null
      return new ManifestSource(cfg.manifestUrl)
    }
    if (cfg.source === 'github') {
      if (!cfg.githubRepo) return null
      return new GithubSource(cfg.githubRepo, cfg.includePrerelease)
    }
    return null
  }

  /** Checks the configured source for a newer version. Never auto-downloads. */
  async check(): Promise<UpdateState> {
    const cfg = getPrefs().update
    if (cfg.source === 'none') {
      this.setState({ status: 'disabled', current: app.getVersion() })
      return this.getState()
    }
    const source = this.makeSource(cfg)
    if (!source) {
      this.setState({
        status: 'error',
        current: app.getVersion(),
        error: '更新源未配置完整',
        lastCheck: new Date().toISOString()
      })
      return this.getState()
    }

    this.setState({ status: 'checking', current: app.getVersion(), error: undefined })
    try {
      const release = await source.latest()
      const current = app.getVersion()
      const lastCheck = new Date().toISOString()
      if (compareVersions(release.version, current) > 0) {
        this.release = release
        this.setState({
          status: 'available',
          current,
          latest: {
            version: release.version,
            notes: release.notes,
            publishedAt: release.publishedAt,
            size: release.size
          },
          error: undefined,
          lastCheck
        })
      } else {
        this.release = null
        this.setState({
          status: 'upToDate',
          current,
          latest: {
            version: release.version,
            notes: release.notes,
            publishedAt: release.publishedAt,
            size: release.size
          },
          error: undefined,
          lastCheck
        })
      }
    } catch (e) {
      this.setState({
        status: 'error',
        current: app.getVersion(),
        error: e instanceof Error ? e.message : '检查更新失败',
        lastCheck: new Date().toISOString()
      })
    }
    return this.getState()
  }

  /** Downloads the available release, verifying its SHA-256. */
  async download(): Promise<UpdateState> {
    if (this.state.status !== 'available' || !this.release) {
      return this.getState()
    }
    const release = this.release
    const dir = join(tmpdir(), 'gameplus-update')
    try {
      mkdirSync(dir, { recursive: true })
    } catch {
      // ignore
    }
    const fileName = sanitizeFileName(release.url, release.version)
    const partPath = join(dir, `${fileName}.part`)
    const finalPath = join(dir, fileName)

    const cap = release.size ? release.size + 1024 * 1024 : MAX_DOWNLOAD_BYTES
    const hardCap = Math.min(cap, MAX_DOWNLOAD_BYTES)

    this.abort = new AbortController()
    this.setState({
      status: 'downloading',
      progress: { received: 0, total: release.size ?? 0 },
      error: undefined
    })

    let out: ReturnType<typeof createWriteStream> | null = null
    try {
      const res = await fetch(release.url, {
        headers: { 'User-Agent': 'GamePlus-Updater', ...(release.headers ?? {}) },
        signal: this.abort.signal,
        redirect: 'follow'
      })
      if (!res.ok) throw new Error(`下载失败（HTTP ${res.status}）`)
      const body = res.body
      if (!body) throw new Error('下载失败（无响应内容）')

      const hash = createHash('sha256')
      out = createWriteStream(partPath)
      const reader = body.getReader()
      let received = 0
      const total = (release.size ?? Number(res.headers.get('content-length') ?? 0)) || 0

      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        if (value) {
          received += value.byteLength
          if (received > hardCap) {
            throw new Error('安装包超过大小上限，已中止下载')
          }
          hash.update(value)
          await writeChunk(out, value)
          const now = Date.now()
          if (now - this.lastProgressAt >= PROGRESS_INTERVAL_MS) {
            this.lastProgressAt = now
            this.setState({ progress: { received, total } })
          }
        }
      }
      await closeStream(out)
      out = null

      const digest = hash.digest('hex')
      if (digest.toLowerCase() !== release.sha256.toLowerCase()) {
        try {
          rmSync(partPath, { force: true })
        } catch {
          // ignore
        }
        this.setState({
          status: 'error',
          error: '安装包校验失败（SHA-256 不匹配），已删除',
          progress: undefined
        })
        return this.getState()
      }

      try {
        if (existsSync(finalPath)) rmSync(finalPath, { force: true })
        renameSync(partPath, finalPath)
      } catch (e) {
        this.setState({
          status: 'error',
          error: `保存安装包失败：${e instanceof Error ? e.message : '未知错误'}`,
          progress: undefined
        })
        return this.getState()
      }
      this.downloadedPath = finalPath
      this.setState({
        status: 'ready',
        progress: { received, total: total || received },
        error: undefined
      })
    } catch (e) {
      // Close the write stream before deleting the partial file: on Windows an open handle
      // makes rmSync fail and leaks the .part file.
      if (out) {
        try {
          out.destroy()
        } catch {
          // ignore
        }
        out = null
      }
      try {
        rmSync(partPath, { force: true })
      } catch {
        // ignore
      }
      const aborted = e instanceof Error && e.name === 'AbortError'
      this.setState({
        status: aborted ? 'available' : 'error',
        error: aborted ? undefined : e instanceof Error ? e.message : '下载失败',
        progress: undefined
      })
    } finally {
      this.abort = null
    }
    return this.getState()
  }

  /** Cancels an in-flight download. */
  cancel(): void {
    if (this.abort) {
      try {
        this.abort.abort()
      } catch {
        // ignore
      }
      this.abort = null
    }
  }

  /** Launches the downloaded installer and quits the app. */
  async install(): Promise<UpdateState> {
    if (this.state.status !== 'ready' || !this.downloadedPath) {
      return this.getState()
    }
    if (process.platform !== 'win32' || !app.isPackaged) {
      this.setState({ status: 'error', error: '仅支持在已安装的 Windows 版本中安装更新' })
      return this.getState()
    }
    const release = this.release
    const installerPath = this.downloadedPath
    if (!release || !existsSync(installerPath)) {
      this.setState({ status: 'error', error: '安装包已被修改，已取消安装' })
      return this.getState()
    }
    try {
      const digest = await hashFile(installerPath)
      if (digest.toLowerCase() !== release.sha256.toLowerCase()) {
        this.setState({ status: 'error', error: '安装包已被修改，已取消安装' })
        return this.getState()
      }
    } catch {
      this.setState({ status: 'error', error: '安装包已被修改，已取消安装' })
      return this.getState()
    }

    const child = spawn(installerPath, ['--updated'], {
      detached: true,
      stdio: 'ignore'
    })

    const spawned = await new Promise<boolean>((resolve) => {
      let settled = false
      const onSpawn = (): void => {
        if (settled) return
        settled = true
        child.removeListener('error', onError)
        resolve(true)
      }
      const onError = (err: Error): void => {
        if (settled) return
        settled = true
        child.removeListener('spawn', onSpawn)
        this.setState({
          status: 'error',
          error: `启动安装程序失败：${err.message}`
        })
        resolve(false)
      }
      child.once('spawn', onSpawn)
      child.once('error', onError)
    })

    if (!spawned) {
      return this.getState()
    }

    child.unref()
    app.quit()
    return this.getState()
  }
}

/** Streams a file and returns its SHA-256 hex digest. */
function hashFile(path: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha256')
    const stream = createReadStream(path)
    stream.on('error', reject)
    stream.on('data', (chunk) => hash.update(chunk))
    stream.on('end', () => resolve(hash.digest('hex')))
  })
}

/** Writes a Uint8Array chunk to a stream, awaiting drain when the buffer is full. */
function writeChunk(stream: NodeJS.WritableStream, chunk: Uint8Array): Promise<void> {
  return new Promise((resolve, reject) => {
    const ok = stream.write(Buffer.from(chunk), (err) => {
      if (err) reject(err)
    })
    if (ok) resolve()
    else stream.once('drain', () => resolve())
  })
}

/** Closes a write stream, resolving when flushed. */
function closeStream(stream: NodeJS.WritableStream): Promise<void> {
  return new Promise((resolve) => {
    stream.end(() => resolve())
  })
}

/** Derives a safe local file name from the download URL / version. */
function sanitizeFileName(url: string, version: string): string {
  let base = ''
  try {
    const parsed = new URL(url)
    base = decodeURIComponent(parsed.pathname.split('/').pop() ?? '')
  } catch {
    // ignore
  }
  base = base.replace(/[^A-Za-z0-9._-]/g, '_')
  if (!/\.exe$/i.test(base)) {
    base = `Game+-${version.replace(/[^A-Za-z0-9._-]/g, '_')}-x64.exe`
  }
  return base
}
