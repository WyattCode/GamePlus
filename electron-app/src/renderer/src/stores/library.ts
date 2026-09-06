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
    ;[games.value, servers.value, subscriptions.value] = await Promise.all([
      backend.listGames(),
      backend.listServers(),
      backend.listSubscriptions()
    ])
    loaded.value = true
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

  async function updateAllSubscriptions(): Promise<void> {
    for (const s of [...subscriptions.value]) await updateSubscription(s.id)
  }

  return {
    games,
    servers,
    subscriptions,
    loaded,
    pinging,
    sortedGames,
    load,
    pingAll,
    toggleFavorite,
    addGame,
    updateGame,
    removeGame,
    addServerFromLink,
    removeServer,
    addSubscription,
    removeSubscription,
    updateSubscription,
    updateAllSubscriptions
  }
})
