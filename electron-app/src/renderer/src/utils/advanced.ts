// Pure validators for the advanced settings page. Mirrors the server-side validation in
// Netch.Host (settings.set) so the UI can show inline errors before the RPC round-trip.
// Every function returns a Chinese error message, or null when the value is acceptable.

/** Defaults used by the mock backend and the "restore defaults" action. */
export const ADVANCED_DEFAULTS = {
  tunDns: '1.1.1.1',
  measureEffect: true,
  regionPenaltyMs: 60,
  localSocksPort: 0
} as const

const IPV4_RE = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/

function isValidIpv4(s: string): boolean {
  const m = IPV4_RE.exec(s)
  if (!m) return false
  for (let i = 1; i <= 4; i++) {
    const n = Number(m[i])
    if (!Number.isInteger(n) || n < 0 || n > 255) return false
    // Reject leading zeros such as "01" (ambiguous octal-looking input).
    if (m[i].length > 1 && m[i].startsWith('0')) return false
  }
  return true
}

function isValidPort(s: string): boolean {
  if (!/^\d{1,5}$/.test(s)) return false
  const n = Number(s)
  return n >= 1 && n <= 65535
}

/** Loose IPv6 check: hex groups separated by colons, optional "::" compression, optional zone. */
function isValidIpv6(s: string): boolean {
  let body = s
  const zone = body.indexOf('%')
  if (zone >= 0) body = body.slice(0, zone)
  if (!body.includes(':')) return false
  if (!/^[0-9A-Fa-f:]+$/.test(body)) return false
  const doubleColon = body.indexOf('::')
  if (doubleColon >= 0 && body.indexOf('::', doubleColon + 1) >= 0) return false
  const groups = body.split(':')
  let count = 0
  for (const g of groups) {
    if (g === '') continue
    if (g.length > 4) return false
    count++
  }
  if (doubleColon >= 0) return count <= 7
  return count === 8
}

/**
 * Validates a TUN DNS value: strict IPv4, IPv6, "[IPv6]:port" or "IPv4:port".
 * Returns a Chinese error message, or null when valid.
 */
export function validateDns(s: string): string | null {
  const v = (s ?? '').trim()
  if (!v) return 'DNS 地址不能为空'

  // [IPv6]:port
  if (v.startsWith('[')) {
    const end = v.indexOf(']')
    if (end < 0) return 'IPv6 地址缺少右方括号，例如 [2606:4700:4700::1111]:53'
    const host = v.slice(1, end)
    const rest = v.slice(end + 1)
    if (!isValidIpv6(host)) return '无效的 IPv6 地址'
    if (rest === '') return null
    if (!rest.startsWith(':')) return '端口格式不正确，应为 [IPv6]:端口'
    if (!isValidPort(rest.slice(1))) return '端口需为 1–65535 之间的整数'
    return null
  }

  // Bare IPv6 (contains a colon but is not IPv4:port)
  if (v.includes(':')) {
    const lastColon = v.lastIndexOf(':')
    const head = v.slice(0, lastColon)
    const tail = v.slice(lastColon + 1)
    // IPv4:port — head must be a valid IPv4 and tail a valid port.
    if (isValidIpv4(head)) {
      if (!isValidPort(tail)) return '端口需为 1–65535 之间的整数'
      return null
    }
    if (isValidIpv6(v)) return null
    return '无效的 IP 地址，可填写 IPv4、IPv6 或带端口的地址'
  }

  if (isValidIpv4(v)) return null
  return '无效的 IPv4 地址，例如 223.5.5.5'
}

/** Validates the region-preference penalty (0–300 ms, step 10). */
export function validateRegionPenalty(n: number): string | null {
  if (!Number.isFinite(n) || !Number.isInteger(n)) return '惩罚值需为整数'
  if (n < 0 || n > 300) return '惩罚值需在 0–300 ms 之间'
  return null
}

/** Validates the fixed local SOCKS5 port (0 = auto, otherwise 1–65535). */
export function validateLocalPort(n: number): string | null {
  if (!Number.isFinite(n) || !Number.isInteger(n)) return '端口需为整数'
  if (n < 0 || n > 65535) return '端口需在 0–65535 之间（0 = 自动选择）'
  return null
}
