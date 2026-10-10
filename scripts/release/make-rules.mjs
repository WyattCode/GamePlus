#!/usr/bin/env node
// Validates and normalizes a game-rules file (see NetchModern/docs/game-rules.md), then writes it
// plus a `<out>.sha256` sidecar. Zero dependencies, Node >= 20.
//
// Usage:
//   node scripts/release/make-rules.mjs --in <rules.json> [options]
//
// Options:
//   --in <path>        Input rules file (e.g. from `gameplus-host.exe --export-rules`) (required)
//   --version <int>    Rules version (default: current UTC time as YYYYMMDDHH)
//   --out <path>       Output path (default: game-rules.json)
//   --help             Show this help

import { createHash } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { basename, resolve } from 'node:path'

const ID_RE = /^[a-z0-9][a-z0-9._-]{0,63}$/
const MAX_GAMES = 2000
const MAX_LIST = 500

function usage() {
  return `Usage: node scripts/release/make-rules.mjs --in <rules.json> [options]

Options:
  --in <path>        Input rules file (e.g. from \`gameplus-host.exe --export-rules\`) (required)
  --version <int>    Rules version (default: current UTC time as YYYYMMDDHH)
  --out <path>       Output path (default: game-rules.json)
  --help             Show this help
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
      case '--in':
        out.input = argv[++i]
        break
      case '--version':
        out.version = argv[++i]
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

function defaultVersion() {
  const d = new Date()
  const y = d.getUTCFullYear()
  const m = String(d.getUTCMonth() + 1).padStart(2, '0')
  const day = String(d.getUTCDate()).padStart(2, '0')
  const h = String(d.getUTCHours()).padStart(2, '0')
  return Number(`${y}${m}${day}${h}`)
}

function isIpv4(s) {
  const parts = s.split('.')
  if (parts.length !== 4) return false
  return parts.every((p) => /^\d{1,3}$/.test(p) && Number(p) <= 255)
}

function isIpv6(s) {
  // Loose check: hex groups separated by ':', optional '::', optional embedded IPv4.
  if (!/^[0-9a-fA-F:]+$/.test(s)) return false
  if (!s.includes(':')) return false
  const doubleColon = s.split('::').length - 1
  if (doubleColon > 1) return false
  const groups = s.split(':').filter((g) => g.length > 0)
  if (doubleColon === 0 && groups.length !== 8) return false
  if (doubleColon === 1 && groups.length > 7) return false
  return groups.every((g) => /^[0-9a-fA-F]{1,4}$/.test(g))
}

function isIpOrCidr(value) {
  if (typeof value !== 'string' || value.length === 0) return false
  const slash = value.indexOf('/')
  if (slash < 0) return isIpv4(value) || isIpv6(value)
  const addr = value.slice(0, slash)
  const prefix = value.slice(slash + 1)
  if (!/^\d+$/.test(prefix)) return false
  const bits = Number(prefix)
  if (isIpv4(addr)) return bits >= 0 && bits <= 32
  if (isIpv6(addr)) return bits >= 0 && bits <= 128
  return false
}

function validate(file) {
  const errors = []
  if (!file || typeof file !== 'object') {
    errors.push('root is not an object')
    return errors
  }
  if (file.schema !== 1) errors.push(`schema must be 1 (got ${JSON.stringify(file.schema)})`)
  if (!Number.isInteger(file.version) || file.version <= 0) {
    errors.push(`version must be a positive integer (got ${JSON.stringify(file.version)})`)
  }
  if (!Array.isArray(file.games)) {
    errors.push('games must be an array')
    return errors
  }
  if (file.games.length < 1 || file.games.length > MAX_GAMES) {
    errors.push(`games must contain 1..${MAX_GAMES} entries (got ${file.games.length})`)
  }

  const seen = new Set()
  file.games.forEach((game, i) => {
    const idx = i + 1
    if (!game || typeof game !== 'object') {
      errors.push(`game #${idx}: not an object`)
      return
    }
    const id = game.Id
    if (typeof id !== 'string' || !ID_RE.test(id)) {
      errors.push(`game #${idx}: invalid Id ${JSON.stringify(id)}`)
    } else {
      if (id.startsWith('custom-')) errors.push(`game #${idx}: Id must not start with "custom-": ${id}`)
      if (seen.has(id)) errors.push(`game #${idx}: duplicate Id ${id}`)
      seen.add(id)
    }
    const name = game.Name
    if (typeof name !== 'string' || name.trim().length === 0) {
      errors.push(`game #${idx}: Name must be non-empty`)
    } else if (name.length > 64) {
      errors.push(`game #${idx}: Name too long (${name.length} > 64)`)
    }

    for (const field of ['Aliases', 'Regions', 'Platforms', 'ProxyDomains', 'ProxyIps', 'ProxyProcesses']) {
      const list = game[field]
      if (list === undefined || list === null) continue
      if (!Array.isArray(list)) {
        errors.push(`game #${idx}: ${field} must be an array`)
        continue
      }
      if (list.length > MAX_LIST) {
        errors.push(`game #${idx}: ${field} has too many entries (${list.length} > ${MAX_LIST})`)
      }
    }

    const procs = Array.isArray(game.ProxyProcesses) ? game.ProxyProcesses : []
    procs.forEach((p) => {
      if (typeof p !== 'string' || p.trim().length === 0) {
        errors.push(`game #${idx}: ProxyProcesses contains an empty entry`)
        return
      }
      if (!p.toLowerCase().endsWith('.exe')) {
        errors.push(`game #${idx}: ProxyProcesses entry must end with .exe: ${p}`)
      }
      if (p.includes('\\') || p.includes('/')) {
        errors.push(`game #${idx}: ProxyProcesses entry must not contain path separators: ${p}`)
      }
    })

    const ips = Array.isArray(game.ProxyIps) ? game.ProxyIps : []
    ips.forEach((ip) => {
      if (!isIpOrCidr(ip)) errors.push(`game #${idx}: invalid ProxyIps entry: ${JSON.stringify(ip)}`)
    })

    const domains = Array.isArray(game.ProxyDomains) ? game.ProxyDomains : []
    domains.forEach((d) => {
      if (typeof d !== 'string' || d.trim().length === 0) {
        errors.push(`game #${idx}: ProxyDomains contains an empty entry`)
        return
      }
      if (/\s/.test(d)) errors.push(`game #${idx}: ProxyDomains entry must not contain whitespace: ${d}`)
    })
  })

  return errors
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

  if (!args.input) {
    process.stderr.write(`--in is required\n\n${usage()}`)
    process.exit(2)
  }

  const inputPath = resolve(args.input)
  let raw
  try {
    raw = await readFile(inputPath, 'utf8')
  } catch {
    process.stderr.write(`Input not found: ${inputPath}\n`)
    process.exit(1)
  }

  let file
  try {
    file = JSON.parse(raw)
  } catch (e) {
    process.stderr.write(`Invalid JSON: ${e.message}\n`)
    process.exit(1)
  }

  const errors = validate(file)
  if (errors.length > 0) {
    process.stderr.write(`Validation failed (${errors.length} error(s)):\n`)
    for (const err of errors) process.stderr.write(`  - ${err}\n`)
    process.exit(1)
  }

  let version
  if (args.version !== undefined) {
    version = Number(args.version)
    if (!Number.isInteger(version) || version <= 0) {
      process.stderr.write(`--version must be a positive integer (got ${args.version})\n`)
      process.exit(1)
    }
  } else {
    version = defaultVersion()
  }

  const outPath = resolve(args.out || 'game-rules.json')
  const normalized = {
    schema: 1,
    version,
    updatedAt: new Date().toISOString(),
    games: file.games
  }

  const json = JSON.stringify(normalized, null, 2) + '\n'
  await writeFile(outPath, json, 'utf8')

  const hash = createHash('sha256').update(json, 'utf8').digest('hex')
  const shaPath = `${outPath}.sha256`
  await writeFile(shaPath, `${hash}  ${basename(outPath)}\n`, 'utf8')

  process.stdout.write(`Wrote ${outPath} (version ${version}, ${file.games.length} games)\n`)
  process.stdout.write(`Wrote ${shaPath}\n`)
}

main().catch((e) => {
  process.stderr.write(`${e && e.stack ? e.stack : e}\n`)
  process.exit(1)
})
