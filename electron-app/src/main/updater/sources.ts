import { getToken } from './token'
import {
  parseManifest,
  pickGithubRelease,
  pickInstallerAsset,
  parseChecksumFile,
  type UpdateRelease,
  type GithubReleaseLike,
  type GithubAssetLike
} from './core'

// Update sources: a direct JSON manifest, or the GitHub releases API. Both return an UpdateRelease
// (version + download URL + expected SHA-256) or throw a Chinese-language Error.

const USER_AGENT = 'GamePlus-Updater'

/** Fetches a URL as JSON with a timeout and a hard byte cap. Throws on non-2xx / oversize / bad JSON. */
export async function fetchJson(
  url: string,
  headers: Record<string, string> = {},
  timeoutMs = 15000,
  maxBytes = 1024 * 1024
): Promise<unknown> {
  const text = await fetchText(url, headers, timeoutMs, maxBytes)
  try {
    return JSON.parse(text)
  } catch {
    throw new Error('更新源返回的数据格式无效')
  }
}

/** Fetches a URL as text with a timeout and a hard byte cap. */
export async function fetchText(
  url: string,
  headers: Record<string, string> = {},
  timeoutMs = 15000,
  maxBytes = 1024 * 1024
): Promise<string> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeoutMs)
  try {
    const res = await fetch(url, {
      headers: { 'User-Agent': USER_AGENT, ...headers },
      signal: controller.signal,
      redirect: 'follow'
    })
    if (!res.ok) {
      throw new Error(`更新源请求失败（HTTP ${res.status}）`)
    }
    const lenHeader = res.headers.get('content-length')
    if (lenHeader) {
      const len = parseInt(lenHeader, 10)
      if (Number.isFinite(len) && len > maxBytes) {
        throw new Error('更新源返回的数据过大')
      }
    }
    const body = res.body
    if (!body) {
      const text = await res.text()
      if (Buffer.byteLength(text, 'utf8') > maxBytes) throw new Error('更新源返回的数据过大')
      return text
    }
    const reader = body.getReader()
    const chunks: Uint8Array[] = []
    let received = 0
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      if (value) {
        received += value.byteLength
        if (received > maxBytes) {
          try {
            await reader.cancel()
          } catch {
            // ignore
          }
          throw new Error('更新源返回的数据过大')
        }
        chunks.push(value)
      }
    }
    const merged = new Uint8Array(received)
    let off = 0
    for (const c of chunks) {
      merged.set(c, off)
      off += c.byteLength
    }
    return new TextDecoder('utf-8').decode(merged)
  } catch (e) {
    if (e instanceof Error && e.name === 'AbortError') {
      throw new Error('更新源请求超时')
    }
    throw e
  } finally {
    clearTimeout(timer)
  }
}

/** Fetches the latest release from a direct JSON manifest URL. */
export class ManifestSource {
  constructor(private readonly manifestUrl: string) {}

  async latest(): Promise<UpdateRelease> {
    const json = await fetchJson(this.manifestUrl)
    return parseManifest(json)
  }
}

/** Fetches the latest release from the GitHub releases API. */
export class GithubSource {
  constructor(
    private readonly repo: string,
    private readonly includePrerelease: boolean
  ) {}

  private authHeaders(): Record<string, string> {
    const token = getToken()
    const headers: Record<string, string> = {
      Accept: 'application/vnd.github+json',
      'User-Agent': USER_AGENT
    }
    if (token) headers['Authorization'] = `Bearer ${token}`
    return headers
  }

  async latest(): Promise<UpdateRelease> {
    const token = getToken()
    const listUrl = `https://api.github.com/repos/${this.repo}/releases?per_page=20`
    const json = await fetchJson(listUrl, this.authHeaders())
    const release = pickGithubRelease(json, this.includePrerelease)
    if (!release) {
      throw new Error('未找到可用的发布版本')
    }

    const { installer, checksum } = pickInstallerAsset(release.assets, process.arch)
    if (!installer) {
      throw new Error('该版本没有提供 Windows 安装包')
    }
    if (!checksum) {
      throw new Error('该版本没有提供 SHA-256 校验文件，出于安全考虑不自动安装')
    }

    // Download the checksum file text (small) and extract the digest for the installer.
    const checksumUrl = token && checksum.url ? checksum.url : checksum.browser_download_url
    if (!checksumUrl) {
      throw new Error('该版本没有提供 SHA-256 校验文件，出于安全考虑不自动安装')
    }
    const checksumHeaders: Record<string, string> = token
      ? { ...this.authHeaders(), Accept: 'application/octet-stream' }
      : {}
    const checksumText = await fetchText(checksumUrl, checksumHeaders, 15000, 64 * 1024)
    const sha256 = parseChecksumFile(checksumText, installer.name)
    if (!sha256) {
      throw new Error('校验文件中没有找到安装包的 SHA-256 值')
    }

    // With a token, use the asset API URL (works for private repos); otherwise the public download URL.
    const downloadUrl = token && installer.url ? installer.url : installer.browser_download_url
    if (!downloadUrl) {
      throw new Error('该版本没有提供 Windows 安装包')
    }
    const headers: Record<string, string> = token
      ? { ...this.authHeaders(), Accept: 'application/octet-stream' }
      : {}

    return {
      version: release.tag_name,
      url: downloadUrl,
      sha256,
      notes: typeof release.body === 'string' ? release.body : '',
      publishedAt: release.published_at ?? undefined,
      size: typeof installer.size === 'number' ? installer.size : undefined,
      headers
    }
  }
}

export type { UpdateRelease, GithubReleaseLike, GithubAssetLike }
