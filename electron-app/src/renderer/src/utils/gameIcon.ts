// Per-game identity: a distinct monogram + a distinct color for every game, so no two tiles look alike
// (we can't ship copyrighted game logos, and shared genre glyphs looked homogeneous).

// Gamer-recognizable abbreviations, hand-tuned to stay unique across the 109 seed games.
const monograms: Record<string, string> = {
  steam: 'ST', epic: 'EP', battlenet: 'BN', xbox: 'XB',
  genshin: 'GI', starrail: 'SR', valorant: 'VL', 'lol-global': 'LOL', cs2: 'CS2', apex: 'AP',
  pubg: 'PG', warzone: 'WZ', diablo4: 'D4', dota2: 'DT2', eldenring: 'ER', r6: 'R6', gtao: 'GTA',
  eft: 'EFT', destiny2: 'DS2', minecraft: 'MC', fortnite: 'FN', roblox: 'RB', naraka: 'NRK',
  uplay: 'UB', eaapp: 'EA', rockstar: 'RS', zzz: 'ZZZ', wuwa: 'WW', deltaforce: 'DF', ow2: 'OW2',
  marvelrivals: 'MR', bf2042: 'BF', ffxiv: 'FF', poe2: 'PE2', thefinals: 'TF', palworld: 'PW',
  warframe: 'WF', dbd: 'DBD', rust: 'RT', rocketleague: 'RL', fallguys: 'FG', seaofthieves: 'SOT',
  helldivers2: 'HD2', l4d2: 'L4D', hunt: 'HT', borderlands3: 'BL3', payday3: 'PD3', darktide: 'DKT',
  titanfall2: 'TF2', deeprock: 'DRG', ark: 'ARK', valheim: 'VH', '7days': '7D', enshrouded: 'EN',
  sonsoftheforest: 'SOF', phasmophobia: 'PH', lethalcompany: 'LC', oncehuman: 'OH', raft: 'RF',
  bg3: 'BG3', lostark: 'LA', gw2: 'GW2', eso: 'ESO', newworld: 'NW', nomanssky: 'NMS', fc25: 'FC',
  nba2k25: '2K', forza5: 'FH5', civ6: 'CV6', aoe4: 'AE4', hearthstone: 'HS', sc2: 'SC2', wow: 'WOW',
  haloinfinite: 'HI', mhwilds: 'MHW', mhworld: 'MH', firstdescendant: 'FD', deadlock: 'DL',
  warthunder: 'WT', worldoftanks: 'WOT', blackdesert: 'BDO', starcitizen: 'SC', throneliberty: 'TL',
  terraria: 'TR', stardew: 'SDV', dst: 'DST', amongus: 'AU', ittakestwo: 'IT2', twwarhammer3: 'WH3',
  dyinglight2: 'DL2', codbo6: 'BO6', rdr2: 'RD2', nfsunbound: 'NFS', sf6: 'SF6', tekken8: 'TK8',
  mk1: 'MK1', strinova: 'SV', division2: 'TD2', acshadows: 'AC', farcry6: 'FC6', d2r: 'D2R',
  hots: 'HOT', mtga: 'MTG', vrising: 'VR', theforest: 'FST', grounded: 'GR', overcooked2: 'OC2',
  flightsim: 'FS', aoe2de: 'AE2'
}

function hash(s: string): number {
  let h = 2166136261
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return h >>> 0
}

/** Abbreviation for a game; custom games derive from their name. */
export function monogram(id: string, name = ''): string {
  if (monograms[id]) return monograms[id]
  const latin = name.match(/[A-Za-z0-9]+/g)
  if (latin && latin.length) {
    if (latin.length === 1) return latin[0].slice(0, 3).toUpperCase()
    return latin.slice(0, 3).map((w) => w[0]).join('').toUpperCase()
  }
  // CJK / other: first 1–2 characters of the name, else the id.
  const clean = (name || id).replace(/\s+/g, '')
  return clean.slice(0, 2) || '?'
}

/**
 * A distinct, stable color per game from its id. Golden-angle hue rotation over a hashed offset gives a
 * wide, well-separated spread; saturation/lightness are jittered a little so same-hue neighbours differ too.
 */
export function gameColor(id: string): string {
  const h = hash(id)
  const hue = Math.round(((h % 997) * 137.508) % 360)
  const sat = 50 + (h % 7) * 3 // 50–68%
  const light = 43 + ((h >> 5) % 6) * 3 // 43–58%
  return `hsl(${hue} ${sat}% ${light}%)`
}
