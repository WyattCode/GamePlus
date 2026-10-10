import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import type { Game, Server, Subscription } from '@shared/types'
import { backend } from '@/api/backend'

export const useLibraryStore = defineStore('library', () => {
  const games = ref<Game[]>([])
  const servers = ref<Server[]>([])
  const subscriptions = ref<Subscription[]>([])
  const loaded = ref(false)
  const pinging = ref(false)

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

  async function addSubscription(remark: string, link: string): Promise<void> {
    subscriptions.value = await backend.addSubscription(remark, link)
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

  return {
    games,
    servers,
    subscriptions,
    loaded,
    pinging,
    sortedGames,
    load,
    reloadServers,
    pingAll,
    toggleFavorite,
    addGame,
    updateGame,
    removeGame,
    exportServers,
    addServerFromLink,
    removeServer,
    addSubscription,
    removeSubscription,
    updateSubscription,
    updateAllSubscriptions
  }
})
