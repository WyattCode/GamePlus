import { defineStore } from 'pinia'
import { onScopeDispose, ref } from 'vue'
import type { AccelStatus, Game, Server } from '@shared/types'
import { backend } from '@/api/backend'

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
    nodeId: null
  })

  const selectedGame = ref<Game | null>(null)
  const selectedNode = ref<Server | null>(null)
  const smartNode = ref(true)

  const off = backend.onStatus((s) => (status.value = s))
  onScopeDispose(off)

  async function start(): Promise<void> {
    if (!selectedGame.value) return
    const nodeId = smartNode.value ? null : selectedNode.value?.id ?? null
    await backend.startAccelerate(selectedGame.value.id, nodeId)
  }

  async function stop(): Promise<void> {
    await backend.stopAccelerate()
  }

  async function toggle(): Promise<void> {
    if (status.value.state === 'accelerating' || status.value.state === 'connecting') {
      await stop()
    } else {
      await start()
    }
  }

  return { status, selectedGame, selectedNode, smartNode, start, stop, toggle }
})
