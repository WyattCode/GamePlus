#!/usr/bin/env node
// Builds the update manifest (latest.json) consumed by the app's updater `parseManifest`.
// Zero dependencies, Node >= 20.
//
// Usage:
//   node scripts/release/make-manifest.mjs --installer <path> [options]
//
// Options:
//   --installer <path>     Installer file to hash (required)
//   --version <x.y.z>      Version string (default: electron-app/package.json version)
//   --base-url <url>       Base URL for the download link
//                          (default: https://github.com/WyattCode/GamePlus-releases/releases/download/v<version>/)
//   --notes <text>         Release notes (inline)
//   --notes-file <path>    Release notes read from a file
//   --out <path>           Output path (default: latest.json next to the installer)
//   --help                 Show this help

import { createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { readFile, stat, writeFile } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const DEFAULT_REPO = 'WyattCode/GamePlus-releases'

function usage() {
  return `Usage: node scripts/release/make-manifest.mjs --installer <path> [options]

Options:
  --installer <path>     Installer file to hash (required)
  --version <x.y.z>      Version string (default: electron-app/package.json version)
  --base-url <url>       Base URL for the download link
                         (default: https://github.com/${DEFAULT_REPO}/releases/download/v<version>/)
  --notes <text>         Release notes (inline)
  --notes-file <path>    Release notes read from a file
  --out <path>           Output path (default: latest.json next to the installer)
  --help                 Show this help
`
}

function parseArgs(argv) {
  const out = {}
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    switch (a) {
      case '--help':
      case '-h':
        out.help = true
        break
      case '--installer':
        out.installer = argv[++i]
        break
      case '--version':
        out.version = argv[++i]
        break
      case '--base-url':
        out.baseUrl = argv[++i]
        break
      case '--notes':
        out.notes = argv[++i]
        break
      case '--notes-file':
        out.notesFile = argv[++i]
        break
      case '--out':
        out.out = argv[++i]
        break
      default:
        throw new Error(`Unknown argument: ${a}`)
    }
  }
  return out
}

async function sha256File(path) {
  const hash = createHash('sha256')
  await new Promise((resolvePromise, reject) => {
    const stream = createReadStream(path)
    stream.on('data', (chunk) => hash.update(chunk))
    stream.on('error', reject)
    stream.on('end', resolvePromise)
  })
  return hash.digest('hex')
}

async function readPackageVersion() {
  const here = dirname(fileURLToPath(import.meta.url))
  const pkgPath = resolve(here, '..', '..', 'electron-app', 'package.json')
  const raw = await readFile(pkgPath, 'utf8')
  const pkg = JSON.parse(raw)
  if (!pkg.version) throw new Error(`No version in ${pkgPath}`)
  return pkg.version
}

async function main() {
  let args
  try {
    args = parseArgs(process.argv.slice(2))
  } catch (e) {
    process.stderr.write(`${e.message}\n\n${usage()}`)
    process.exit(2)
  }

  if (args.help) {
    process.stdout.write(usage())
    return
  }

  if (!args.installer) {
    process.stderr.write(`--installer is required\n\n${usage()}`)
    process.exit(2)
  }

  const installerPath = resolve(args.installer)
  let info
  try {
    info = await stat(installerPath)
  } catch {
    process.stderr.write(`Installer not found: ${installerPath}\n`)
    process.exit(1)
  }
  if (!info.isFile()) {
    process.stderr.write(`Not a file: ${installerPath}\n`)
    process.exit(1)
  }

  const version = args.version || (await readPackageVersion())
  if (!/^\d+\.\d+\.\d+/.test(version)) {
    process.stderr.write(`Invalid version: ${version}\n`)
    process.exit(1)
  }

  const baseUrl =
    args.baseUrl || `https://github.com/${DEFAULT_REPO}/releases/download/v${version}/`
  if (!baseUrl.startsWith('https://')) {
    process.stderr.write(`--base-url must be https: ${baseUrl}\n`)
    process.exit(1)
  }

  let notes = ''
  if (args.notesFile) {
    notes = await readFile(resolve(args.notesFile), 'utf8')
  } else if (args.notes) {
    notes = args.notes
  }

  const fileName = basename(installerPath)
  const url = baseUrl + encodeURIComponent(fileName)
  if (!url.startsWith('https://')) {
    process.stderr.write(`Computed url is not https: ${url}\n`)
    process.exit(1)
  }

  const sha256 = await sha256File(installerPath)
  if (!/^[0-9a-f]{64}$/.test(sha256)) {
    process.stderr.write(`Invalid sha256: ${sha256}\n`)
    process.exit(1)
  }

  const manifest = {
    version,
    url,
    sha256,
    size: info.size,
    notes,
    publishedAt: new Date().toISOString()
  }

  const outPath = args.out ? resolve(args.out) : join(dirname(installerPath), 'latest.json')
  await writeFile(outPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8')

  process.stdout.write(`Wrote ${outPath}\n`)
  process.stdout.write(JSON.stringify(manifest, null, 2) + '\n')
}

main().catch((e) => {
  process.stderr.write(`${e && e.stack ? e.stack : e}\n`)
  process.exit(1)
})
