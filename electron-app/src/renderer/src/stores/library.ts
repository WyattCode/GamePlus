import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import type {
  CaptureMode,
  DetectedGame,
  Game,
  ImportResult,
  LibraryScanResult,
  RuleExport,
  RuleImportResult,
  RulesStatus,
  RulesUpdateResult,
  Server,
  Subscription
} from '@shared/types'
import { backend } from '@/api/backend'
import { useAccelStore } from '@/stores/accel'

export const useLibraryStore = defineStore('library', () => {
  const games = ref<Game[]>([])
  const servers = ref<Server[]>([])
  const subscriptions = ref<Subscription[]>([])
  const loaded = ref(false)
  const pinging = ref(false)

  const scanResult = ref<LibraryScanResult | null>(null)
  const scanning = ref(false)
  const rulesStatus = ref<RulesStatus | null>(null)

  async function load(): Promise<void> {
    if (loaded.value) return
    // Only mark loaded once every call succeeded, so a failed load can be retried.
    const [g, s, subs] = await Promise.all([
      backend.listGames(),
      backend.listServers(),
      backend.listSubscriptions()
    ])
    games.value = g
    servers.value = s
    subscriptions.value = subs
    loaded.value = true
  }

  async function reloadServers(): Promise<void> {
    servers.value = await backend.listServers()
  }

  async function pingAll(): Promise<void> {
    pinging.value = true
    try {
      servers.value = await backend.pingAll()
    } finally {
      pinging.value = false
    }
  }

  // Custom games first, then favourites, then the rest — mirrors the WPF sort.
  const sortedGames = computed(() =>
    [...games.value].sort((a, b) => {
      if (a.custom !== b.custom) return a.custom ? -1 : 1
      if (a.favorite !== b.favorite) return a.favorite ? -1 : 1
      return a.name.localeCompare(b.name, 'zh-CN')
    })
  )

  async function toggleFavorite(id: string): Promise<void> {
    games.value = await backend.toggleFavorite(id)
  }

  async function setGameAuto(id: string, enabled: boolean): Promise<void> {
    games.value = await backend.setGameAuto(id, enabled)
  }

  /**
   * Persists the per-game 国服/国际服 choice. When the game is the one currently selected in the
   * accel store, keeps that snapshot in sync and refreshes the smart-routing ranking so the
   * recommendation reflects the new server.
   */
  async function setGameServer(id: string, server: 'cn' | 'intl'): Promise<void> {
    games.value = await backend.setGameServer(id, server)

    const accel = useAccelStore()
    if (accel.selectedGame?.id === id) {
      const updated = games.value.find((g) => g.id === id)
      if (updated) accel.selectedGame = updated
      await accel.refreshRanking(false)
    }
  }

  /**
   * Persists the per-game capture-mode override ("default" clears it). Keeps the accel store's
   * selected-game snapshot in sync so the mode picker reflects the new value immediately.
   */
  async function setGameMode(id: string, mode: CaptureMode | 'default'): Promise<void> {
    games.value = await backend.setGameMode(id, mode)

    const accel = useAccelStore()
    if (accel.selectedGame?.id === id) {
      const updated = games.value.find((g) => g.id === id)
      if (updated) accel.selectedGame = updated
    }
  }

  /**
   * Persists whether the game's download (CDN) traffic also goes through the node. Keeps the accel
   * store's selected-game snapshot in sync so the toggle reflects the new value immediately.
   */
  async function setGameDownloadProxy(id: string, enabled: boolean): Promise<void> {
    games.value = await backend.setGameDownloadProxy(id, enabled)

    const accel = useAccelStore()
    if (accel.selectedGame?.id === id) {
      const updated = games.value.find((g) => g.id === id)
      if (updated) accel.selectedGame = updated
    }
  }

  /**
   * Persists the per-game launch target (empty string clears it). Keeps the accel store's
   * selected-game snapshot in sync so the launch dialog reflects the new value immediately.
   */
  async function setGameLaunch(id: string, target: string): Promise<void> {
    games.value = await backend.setGameLaunch(id, target)

    const accel = useAccelStore()
    if (accel.selectedGame?.id === id) {
      const updated = games.value.find((g) => g.id === id)
      if (updated) accel.selectedGame = updated
    }
  }

  async function addGame(data: Partial<Game>): Promise<void> {
    games.value = await backend.addGame(data)
  }

  async function updateGame(id: string, data: Partial<Game>): Promise<void> {
    games.value = await backend.updateGame(id, data)
  }

  async function removeGame(id: string): Promise<void> {
    games.value = await backend.removeGame(id)
  }

  async function exportServers(): Promise<string[]> {
    return backend.exportServers()
  }

  async function addServerFromLink(link: string): Promise<number> {
    const before = servers.value.length
    servers.value = await backend.addServerFromLink(link)
    return servers.value.length - before
  }

  async function removeServer(id: string): Promise<void> {
    servers.value = await backend.removeServer(id)
  }

  /** Adds a subscription and returns the newly-created entry (so callers can immediately update it). */
  async function addSubscription(remark: string, link: string): Promise<Subscription | null> {
    const before = new Set(subscriptions.value.map((s) => s.id))
    subscriptions.value = await backend.addSubscription(remark, link)
    return subscriptions.value.find((s) => !before.has(s.id)) ?? null
  }

  async function removeSubscription(id: string): Promise<void> {
    const { subscriptions: subs, servers: srv } = await backend.removeSubscription(id)
    subscriptions.value = subs
    servers.value = srv
  }

  async function updateSubscription(id: string): Promise<void> {
    const { subscriptions: subs, servers: srv } = await backend.updateSubscription(id)
    subscriptions.value = subs
    servers.value = srv
  }

  /**
   * Updates every subscription, continuing past individual failures. Returns the number that failed so
   * the caller can surface a summary (the per-subscription error is not thrown, so one bad link can't
   * abort the whole batch).
   */
  async function updateAllSubscriptions(): Promise<number> {
    let failed = 0
    for (const s of [...subscriptions.value]) {
      try {
        await updateSubscription(s.id)
      } catch {
        failed++
      }
    }
    return failed
  }

  /**
   * Imports nodes from pasted text (share links, subscription blob, Clash YAML or a subscription URL).
   * Refreshes the server list and, when the backend created a subscription, reloads subscriptions too.
   */
  async function importNodes(text: string): Promise<ImportResult> {
    const result = await backend.importNodes(text)
    servers.value = result.servers
    if (result.format === 'subscription') {
      subscriptions.value = await backend.listSubscriptions()
    }
    return result
  }

  /** Exports one game's rules (JSON + share string) without touching local state. */
  async function exportGameRule(id: string): Promise<RuleExport> {
    return backend.exportGameRule(id)
  }

  /** Imports a single-game rule; refreshes the game list when something was imported. */
  async function importGameRule(
    text: string,
    onConflict?: 'ask' | 'replace' | 'copy'
  ): Promise<RuleImportResult> {
    const result = await backend.importGameRule(text, onConflict)
    if (result.status === 'imported') games.value = result.games
    return result
  }

  /** Scans the local machine for installed games. Result is kept in `scanResult`. */
  async function scan(): Promise<LibraryScanResult> {
    scanning.value = true
    try {
      const result = await backend.scanLibrary()
      scanResult.value = result
      return result
    } finally {
      scanning.value = false
    }
  }

  /** Adds a detected game to the library and refreshes the game list. */
  async function addDetected(d: DetectedGame): Promise<void> {
    games.value = await backend.addDetectedGame(d.name, d.executables, d.source)
  }

  async function refreshRulesStatus(): Promise<void> {
    rulesStatus.value = await backend.rulesStatus()
  }

  /** Checks the remote rules source; replaces the game list when the rules were updated. */
  async function updateRules(): Promise<RulesUpdateResult> {
    const result = await backend.updateRules()
    if (result.status === 'updated') games.value = result.games
    await refreshRulesStatus()
    return result
  }

  return {
    games,
    servers,
    subscriptions,
    loaded,
    pinging,
    scanResult,
    scanning,
    rulesStatus,
    sortedGames,
    load,
    reloadServers,
    pingAll,
    toggleFavorite,
    setGameAuto,
    setGameServer,
    setGameMode,
    setGameDownloadProxy,
    setGameLaunch,
    addGame,
    updateGame,
    removeGame,
    exportServers,
    addServerFromLink,
    removeServer,
    addSubscription,
    removeSubscription,
    updateSubscription,
    updateAllSubscriptions,
    importNodes,
    exportGameRule,
    importGameRule,
    scan,
    addDetected,
    refreshRulesStatus,
    updateRules
  }
})
