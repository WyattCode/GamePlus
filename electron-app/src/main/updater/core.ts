// Pure, dependency-free auto-update logic. No electron imports so it can be unit-tested with vitest.

export interface UpdateRelease {
  version: string
  url: string
  sha256: string
  notes: string
  publishedAt?: string
  size?: number
  /** Extra request headers (e.g. GitHub auth for private repos). */
  headers?: Record<string, string>
}

/** Minimal shape of a GitHub release object we care about. */
export interface GithubReleaseLike {
  tag_name: string
  name?: string | null
  body?: string | null
  draft?: boolean
  prerelease?: boolean
  published_at?: string | null
  assets?: GithubAssetLike[]
}

export interface GithubAssetLike {
  name: string
  browser_download_url?: string
  url?: string
  size?: number
}

const SHA256_RE = /^[0-9a-f]{64}$/i

/** True for an https URL, or an http URL pointing at loopback (localhost / 127.0.0.1 / ::1). */
export function isAllowedUrl(url: string): boolean {
  if (typeof url !== 'string' || url.length === 0 || url.length > 2048) return false
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return false
  }
  if (parsed.protocol === 'https:') return true
  if (parsed.protocol !== 'http:') return false
  const host = parsed.hostname.toLowerCase()
  return host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]'
}

/** Splits a version string into numeric parts and an optional prerelease suffix. */
function splitVersion(v: string): { nums: number[]; pre: string[] } {
  let s = v.trim()
  if (s.startsWith('v') || s.startsWith('V')) s = s.slice(1)
  // Drop build metadata.
  const plus = s.indexOf('+')
  if (plus >= 0) s = s.slice(0, plus)
  const dash = s.indexOf('-')
  const core = dash >= 0 ? s.slice(0, dash) : s
  const preStr = dash >= 0 ? s.slice(dash + 1) : ''
  const nums = core.split('.').map((p) => {
    const n = parseInt(p, 10)
    return Number.isFinite(n) ? n : 0
  })
  while (nums.length < 3) nums.push(0)
  const pre = preStr.length > 0 ? preStr.split('.') : []
  return { nums, pre }
}

/** Compares two semver-ish version strings. Returns <0, 0 or >0. */
export function compareVersions(a: string, b: string): number {
  const va = splitVersion(a)
  const vb = splitVersion(b)
  for (let i = 0; i < 3; i++) {
    const d = (va.nums[i] ?? 0) - (vb.nums[i] ?? 0)
    if (d !== 0) return d < 0 ? -1 : 1
  }
  // A release (no prerelease) sorts after a prerelease.
  if (va.pre.length === 0 && vb.pre.length === 0) return 0
  if (va.pre.length === 0) return 1
  if (vb.pre.length === 0) return -1
  const len = Math.max(va.pre.length, vb.pre.length)
  for (let i = 0; i < len; i++) {
    const pa = va.pre[i]
    const pb = vb.pre[i]
    if (pa === undefined) return -1
    if (pb === undefined) return 1
    const na = /^\d+$/.test(pa)
    const nb = /^\d+$/.test(pb)
    if (na && nb) {
      const d = parseInt(pa, 10) - parseInt(pb, 10)
      if (d !== 0) return d < 0 ? -1 : 1
    } else {
      if (pa < pb) return -1
      if (pa > pb) return 1
    }
  }
  return 0
}

/** Parses and validates a manifest JSON document into an UpdateRelease. Throws on invalid input. */
export function parseManifest(json: unknown): UpdateRelease {
  if (!json || typeof json !== 'object') {
    throw new Error('更新清单格式无效')
  }
  const o = json as Record<string, unknown>
  const version = o.version
  if (typeof version !== 'string' || version.trim().length === 0) {
    throw new Error('更新清单缺少版本号')
  }
  // Ensure the version is parseable (has at least one numeric component).
  if (!/\d/.test(version)) {
    throw new Error('更新清单版本号无效')
  }
  const url = o.url
  if (typeof url !== 'string' || !isAllowedUrl(url)) {
    throw new Error('更新清单下载地址无效')
  }
  const sha256 = o.sha256
  if (typeof sha256 !== 'string' || !SHA256_RE.test(sha256)) {
    throw new Error('更新清单校验值无效')
  }
  const notes = typeof o.notes === 'string' ? o.notes : ''
  const publishedAt = typeof o.publishedAt === 'string' ? o.publishedAt : undefined
  const size = typeof o.size === 'number' && Number.isFinite(o.size) && o.size > 0 ? o.size : undefined
  return {
    version: version.trim(),
    url,
    sha256: sha256.toLowerCase(),
    notes,
    publishedAt,
    size
  }
}

/** Picks the highest-versioned non-draft (and non-prerelease unless allowed) GitHub release. */
export function pickGithubRelease(releases: unknown, includePrerelease: boolean): GithubReleaseLike | null {
  if (!Array.isArray(releases)) return null
  let best: GithubReleaseLike | null = null
  for (const r of releases) {
    if (!r || typeof r !== 'object') continue
    const rel = r as GithubReleaseLike
    if (typeof rel.tag_name !== 'string' || rel.tag_name.length === 0) continue
    if (rel.draft) continue
    if (rel.prerelease && !includePrerelease) continue
    if (!best || compareVersions(rel.tag_name, best.tag_name) > 0) {
      best = rel
    }
  }
  return best
}

/** Picks the installer .exe asset (preferring the given arch) and a checksum asset, if present. */
export function pickInstallerAsset(
  assets: unknown,
  arch: string
): { installer: GithubAssetLike | null; checksum: GithubAssetLike | null } {
  const list: GithubAssetLike[] = Array.isArray(assets)
    ? (assets.filter((a) => a && typeof a === 'object') as GithubAssetLike[])
    : []

  const exes = list.filter(
    (a) => typeof a.name === 'string' && /\.exe$/i.test(a.name) && !/\.blockmap$/i.test(a.name)
  )
  const archMatch = exes.find((a) => a.name.toLowerCase().includes(`-${arch.toLowerCase()}.exe`))
  const installer = archMatch ?? exes[0] ?? null

  let checksum: GithubAssetLike | null = null
  if (installer) {
    const want = `${installer.name}.sha256`.toLowerCase()
    checksum = list.find((a) => typeof a.name === 'string' && a.name.toLowerCase() === want) ?? null
  }
  if (!checksum) {
    checksum =
      list.find((a) => typeof a.name === 'string' && /^sha256sums(\.txt)?$/i.test(a.name)) ??
      list.find((a) => typeof a.name === 'string' && /^checksums\.txt$/i.test(a.name)) ??
      null
  }
  return { installer, checksum }
}

/** Parses a sha256sum-style checksum file, returning the hex digest for `fileName` or null. */
export function parseChecksumFile(text: string, fileName: string): string | null {
  if (typeof text !== 'string') return null
  const lines = text.split(/\r?\n/)
  const target = fileName.toLowerCase()
  let bare: string | null = null
  for (const raw of lines) {
    const line = raw.trim()
    if (line.length === 0) continue
    // "<hex>  <name>" or "<hex> *<name>"
    const m = line.match(/^([0-9a-fA-F]{64})\s+\*?(.+)$/)
    if (m) {
      const hex = m[1].toLowerCase()
      const name = m[2].trim().toLowerCase()
      if (name === target) return hex
      continue
    }
    // Bare single hex line (for .sha256 files).
    if (/^[0-9a-fA-F]{64}$/.test(line)) {
      bare = line.toLowerCase()
    }
  }
  return bare
}
