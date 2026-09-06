import type { Game, Server, Subscription } from '@shared/types'

// Seed content shown until the C# backend is wired in. Mirrors the WPF SampleData set.
export const sampleGames: Game[] = [
  { id: 'diablo4', name: '暗黑破坏神 IV', icon: '', accent: '#8b1e1e', regionText: '亚服 / 美服 / 欧服', ruleCount: 12, categories: ['动作', '暴雪'], favorite: true, custom: false },
  { id: 'ow2', name: '守望先锋 2', icon: '', accent: '#f99e1a', regionText: '亚服 / 国际服', ruleCount: 9, categories: ['射击', '暴雪'], favorite: false, custom: false },
  { id: 'valorant', name: '无畏契约', icon: '', accent: '#fa4454', regionText: '亚太 / 北美', ruleCount: 8, categories: ['射击', 'Riot'], favorite: true, custom: false },
  { id: 'lol-global', name: '英雄联盟', icon: '', accent: '#1e6fd9', regionText: '国服 / 外服', ruleCount: 10, categories: ['MOBA', 'Riot'], favorite: false, custom: false },
  { id: 'apex', name: 'Apex 英雄', icon: '', accent: '#da292a', regionText: '亚服 / 美西 / 美东', ruleCount: 11, categories: ['射击', 'EA'], favorite: false, custom: false },
  { id: 'steam', name: 'Steam 平台', icon: '', accent: '#1b6dc1', regionText: '全球', ruleCount: 6, categories: ['平台'], favorite: false, custom: false },
  { id: 'gtao', name: 'GTA 线上模式', icon: '', accent: '#3a9d3a', regionText: '全球', ruleCount: 7, categories: ['开放世界', 'R星'], favorite: false, custom: false },
  { id: 'eldenring', name: '艾尔登法环', icon: '', accent: '#c9a227', regionText: '全球', ruleCount: 5, categories: ['动作', 'FS'], favorite: false, custom: false },
  { id: 'mhwilds', name: '怪物猎人：荒野', icon: '', accent: '#2f8f6f', regionText: '亚服 / 国际服', ruleCount: 8, categories: ['动作', 'Capcom'], favorite: false, custom: false },
  { id: 'warzone', name: '使命召唤：战区', icon: '', accent: '#4b5320', regionText: '亚服 / 美服 / 欧服', ruleCount: 12, categories: ['射击', '动视'], favorite: false, custom: false },
  { id: 'ffxiv', name: '最终幻想 XIV', icon: '', accent: '#5b6ee1', regionText: '国际服 / 日服', ruleCount: 9, categories: ['MMO', 'SE'], favorite: false, custom: false },
  { id: 'poe2', name: '流放之路 2', icon: '', accent: '#7a3b2e', regionText: '国际服', ruleCount: 6, categories: ['ARPG'], favorite: false, custom: false }
]

export const sampleServers: Server[] = [
  { id: 's1', kind: 'vless', type: 'VLESS', remark: 'HK（TX）', hostname: '43.155.70.99', port: 36975, group: 'NONE', delay: 68 },
  { id: 's2', kind: 'hysteria2', type: 'HY2', remark: 'HK-Hysteria2', hostname: '43.155.70.99', port: 36976, group: 'NONE', delay: 71 },
  { id: 's3', kind: 'trojan', type: 'Trojan', remark: '日本 东京 01', hostname: 'jp1.example.net', port: 443, group: 'NONE', delay: 96 },
  { id: 's4', kind: 'ss', type: 'SS', remark: '新加坡 01', hostname: 'sg1.example.net', port: 8388, group: 'NONE', delay: 88 }
]

export const sampleSubscriptions: Subscription[] = []
