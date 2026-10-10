import { describe, it, expect } from 'vitest'
import {
  compareVersions,
  parseManifest,
  pickGithubRelease,
  pickInstallerAsset,
  parseChecksumFile,
  isAllowedUrl
} from './core'

describe('compareVersions', () => {
  it('compares numeric parts', () => {
    expect(compareVersions('3.1.0', '3.0.0')).toBeGreaterThan(0)
    expect(compareVersions('3.0.0', '3.1.0')).toBeLessThan(0)
    expect(compareVersions('3.0.0', '3.0.0')).toBe(0)
  })

  it('strips a leading v', () => {
    expect(compareVersions('v3.1.0', '3.1.0')).toBe(0)
    expect(compareVersions('v3.2.0', 'v3.1.9')).toBeGreaterThan(0)
  })

  it('treats missing parts as zero', () => {
    expect(compareVersions('3.1', '3.1.0')).toBe(0)
    expect(compareVersions('3', '3.0.1')).toBeLessThan(0)
  })

  it('sorts prereleases before the release', () => {
    expect(compareVersions('3.1.0-beta.2', '3.1.0')).toBeLessThan(0)
    expect(compareVersions('3.1.0', '3.1.0-beta.2')).toBeGreaterThan(0)
  })

  it('compares prerelease identifiers numerically when both numeric', () => {
    expect(compareVersions('3.1.0-beta.10', '3.1.0-beta.2')).toBeGreaterThan(0)
    expect(compareVersions('3.1.0-beta.2', '3.1.0-beta.10')).toBeLessThan(0)
  })

  it('compares prerelease identifiers lexically otherwise', () => {
    expect(compareVersions('3.1.0-alpha', '3.1.0-beta')).toBeLessThan(0)
  })
})

describe('parseManifest', () => {
  const valid = {
    version: '3.1.0',
    url: 'https://example.com/Game+-3.1.0-x64.exe',
    sha256: 'a'.repeat(64),
    notes: '修复若干问题'
  }

  it('parses a valid manifest', () => {
    const r = parseManifest(valid)
    expect(r.version).toBe('3.1.0')
    expect(r.url).toBe(valid.url)
    expect(r.sha256).toBe('a'.repeat(64))
    expect(r.notes).toBe('修复若干问题')
  })

  it('lowercases the sha256', () => {
    const r = parseManifest({ ...valid, sha256: 'A'.repeat(64) })
    expect(r.sha256).toBe('a'.repeat(64))
  })

  it('rejects a bad sha256', () => {
    expect(() => parseManifest({ ...valid, sha256: 'xyz' })).toThrow()
  })

  it('rejects an http URL', () => {
    expect(() => parseManifest({ ...valid, url: 'http://example.com/a.exe' })).toThrow()
  })

  it('allows a loopback http URL', () => {
    const r = parseManifest({ ...valid, url: 'http://127.0.0.1:8080/a.exe' })
    expect(r.url).toBe('http://127.0.0.1:8080/a.exe')
  })

  it('rejects a missing version', () => {
    expect(() => parseManifest({ ...valid, version: '' })).toThrow()
  })
})

describe('isAllowedUrl', () => {
  it('allows https and loopback http', () => {
    expect(isAllowedUrl('https://example.com/a')).toBe(true)
    expect(isAllowedUrl('http://localhost:3000/a')).toBe(true)
    expect(isAllowedUrl('http://127.0.0.1/a')).toBe(true)
    expect(isAllowedUrl('http://[::1]/a')).toBe(true)
  })

  it('rejects non-loopback http and other schemes', () => {
    expect(isAllowedUrl('http://example.com/a')).toBe(false)
    expect(isAllowedUrl('ftp://example.com/a')).toBe(false)
    expect(isAllowedUrl('')).toBe(false)
  })
})

describe('pickGithubRelease', () => {
  const releases = [
    { tag_name: 'v3.0.0', draft: false, prerelease: false },
    { tag_name: 'v3.2.0', draft: false, prerelease: false },
    { tag_name: 'v3.3.0', draft: true, prerelease: false },
    { tag_name: 'v3.4.0', draft: false, prerelease: true }
  ]

  it('skips drafts and prereleases by default', () => {
    const r = pickGithubRelease(releases, false)
    expect(r?.tag_name).toBe('v3.2.0')
  })

  it('includes prereleases when asked', () => {
    const r = pickGithubRelease(releases, true)
    expect(r?.tag_name).toBe('v3.4.0')
  })

  it('returns null for an empty list', () => {
    expect(pickGithubRelease([], false)).toBeNull()
    expect(pickGithubRelease(null, false)).toBeNull()
  })
})

describe('pickInstallerAsset', () => {
  const assets = [
    { name: 'Game+-3.1.0-x64.exe', browser_download_url: 'https://x/a.exe' },
    { name: 'Game+-3.1.0-arm64.exe', browser_download_url: 'https://x/b.exe' },
    { name: 'Game+-3.1.0-x64.exe.blockmap', browser_download_url: 'https://x/c.blockmap' },
    { name: 'SHA256SUMS.txt', browser_download_url: 'https://x/sums.txt' }
  ]

  it('prefers the matching arch', () => {
    const { installer } = pickInstallerAsset(assets, 'x64')
    expect(installer?.name).toBe('Game+-3.1.0-x64.exe')
  })

  it('ignores blockmap files', () => {
    const { installer } = pickInstallerAsset(assets, 'arm64')
    expect(installer?.name).toBe('Game+-3.1.0-arm64.exe')
  })

  it('finds a SHA256SUMS checksum asset', () => {
    const { checksum } = pickInstallerAsset(assets, 'x64')
    expect(checksum?.name).toBe('SHA256SUMS.txt')
  })

  it('finds a per-asset .sha256 checksum', () => {
    const withPerAsset = [
      { name: 'Game+-3.1.0-x64.exe' },
      { name: 'Game+-3.1.0-x64.exe.sha256' }
    ]
    const { checksum } = pickInstallerAsset(withPerAsset, 'x64')
    expect(checksum?.name).toBe('Game+-3.1.0-x64.exe.sha256')
  })

  it('returns null when there is no installer', () => {
    const { installer } = pickInstallerAsset([{ name: 'notes.txt' }], 'x64')
    expect(installer).toBeNull()
  })
})

describe('parseChecksumFile', () => {
  const hex = 'b'.repeat(64)

  it('parses sha256sum lines with two spaces', () => {
    const text = `${hex}  Game+-3.1.0-x64.exe\n`
    expect(parseChecksumFile(text, 'Game+-3.1.0-x64.exe')).toBe(hex)
  })

  it('parses sha256sum lines with a star', () => {
    const text = `${hex} *Game+-3.1.0-x64.exe\n`
    expect(parseChecksumFile(text, 'Game+-3.1.0-x64.exe')).toBe(hex)
  })

  it('parses a bare hex line', () => {
    expect(parseChecksumFile(`${hex}\n`, 'anything.exe')).toBe(hex)
  })

  it('returns null when the file is not listed', () => {
    const text = `${hex}  other.exe\n`
    expect(parseChecksumFile(text, 'Game+-3.1.0-x64.exe')).toBeNull()
  })
})
