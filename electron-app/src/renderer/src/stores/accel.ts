import { defineStore } from 'pinia'
import { computed, onScopeDispose, ref } from 'vue'
import type { AccelStatus, EffectReport, Game, NodeRanking, Server } from '@shared/types'
import { backend } from '@/api/backend'
import { useLibraryStore } from '@/stores/library'
import { track } from '@/utils/telemetry'

export const useAccelStore = defineStore('accel', () => {
  const status = ref<AccelStatus>({
    state: 'idle',
    latency: 0,
    loss: 0,
    jitter: 0,
    durationSec: 0,
    downloadBps: 0,
    uploadBps: 0,
    gameId: null,
    nodeId: null,
    captureMode: null
  })

  const effect = ref<EffectReport | null>(null)

  const selectedGame = ref<Game | null>(null)
  // Bind the node select by id, not by object: after servers.ping / list refresh the server objects are
  // replaced, so an object binding would silently blank the select.
  const selectedNodeId = ref<string | null>(null)
  const smartNode = ref(true)
  // Set when the previously-selected node id disappears from the current list (e.g. subscription refresh).
  const nodeUnavailable = ref(false)

  // Smart-routing ranking for the selected game (candidates + recommended node + reason).
  const ranking = ref<NodeRanking | null>(null)
  const rankLoading = ref(false)

  const library = useLibraryStore()

  const selectedNode = computed<Server | null>(() => {
    if (!selectedNodeId.value) return null
    return library.servers.find((s) => s.id === selectedNodeId.value) ?? null
  })

  function setSelectedNodeId(id: string | null): void {
    selectedNodeId.value = id
    nodeUnavailable.value = false
  }

  // If the selected id vanishes from the list, clear the selection and fall back to smart node.
  function reconcileSelection(): void {
    if (!selectedNodeId.value) return
    if (!library.servers.some((s) => s.id === selectedNodeId.value)) {
      selectedNodeId.value = null
      nodeUnavailable.value = true
      smartNode.value = true
    }
  }

  const off = backend.onStatus((s) => (status.value = s))
  onScopeDispose(off)

  const offEffect = backend.onEffect((r) => (effect.value = r))
  onScopeDispose(offEffect)

  // Fetch once on init so the card can show the last finished report even before any event arrives.
  backend.getEffect().then(
    (r) => {
      effect.value = r
    },
    () => {
      // ignore — the event stream will populate it
    }
  )

  async function start(): Promise<void> {
    if (!selectedGame.value) return
    const nodeId = smartNode.value ? null : selectedNodeId.value
    const startedAt = Date.now()
    try {
      await backend.startAccelerate(selectedGame.value.id, nodeId)
      track({
        type: 'accel_start',
        ok: true,
        mode: status.value.captureMode ?? undefined,
        durationMs: Date.now() - startedAt
      })
    } catch (e) {
      track({
        type: 'accel_start',
        ok: false,
        mode: status.value.captureMode ?? undefined,
        reason: e instanceof Error ? e.message : String(e),
        durationMs: Date.now() - startedAt
      })
      throw e
    }
  }

  async function stop(): Promise<void> {
    const durationSec = status.value.durationSec
    await backend.stopAccelerate()
    track({ type: 'accel_stop', durationSec })
  }

  async function toggle(): Promise<void> {
    if (status.value.state === 'accelerating' || status.value.state === 'connecting') {
      await stop()
    } else {
      await start()
    }
  }

  /** Refreshes the smart-routing ranking for the selected game. Errors propagate to the caller. */
  async function refreshRanking(ping = false): Promise<void> {
    rankLoading.value = true
    try {
      ranking.value = await backend.rankNodes(selectedGame.value?.id ?? null, ping)
      if (ping) await library.reloadServers()
    } finally {
      rankLoading.value = false
    }
  }

  /**
   * Switches to a specific node: turns off smart routing, selects the node, and restarts acceleration
   * on it when currently accelerating. Errors propagate to the caller.
   */
  async function switchNode(id: string): Promise<void> {
    smartNode.value = false
    setSelectedNodeId(id)
    if (status.value.state === 'accelerating' || status.value.state === 'connecting') {
      await stop()
      await start()
    }
  }

  return {
    status,
    effect,
    selectedGame,
    selectedNodeId,
    selectedNode,
    smartNode,
    nodeUnavailable,
    ranking,
    rankLoading,
    setSelectedNodeId,
    reconcileSelection,
    refreshRanking,
    switchNode,
    start,
    stop,
    toggle
  }
})
