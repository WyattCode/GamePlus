<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { useAccelStore } from '@/stores/accel'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { DetectedGame } from '@shared/types'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const library = useLibraryStore()
const accel = useAccelStore()
const toast = useToastStore()
const router = useRouter()

const added = ref<Set<string>>(new Set())

const matched = computed<DetectedGame[]>(
  () => library.scanResult?.games.filter((g) => g.gameId != null) ?? []
)
const unmatched = computed<DetectedGame[]>(
  () => library.scanResult?.games.filter((g) => g.gameId == null) ?? []
)

function keyOf(d: DetectedGame): string {
  return `${d.source}:${d.sourceId}`
}

function isFavorite(d: DetectedGame): boolean {
  if (!d.gameId) return false
  return library.games.find((g) => g.id === d.gameId)?.favorite ?? false
}

async function favorite(d: DetectedGame): Promise<void> {
  if (!d.gameId || isFavorite(d)) return
  try {
    await library.toggleFavorite(d.gameId)
  } catch (e) {
    toast.error('操作失败：' + errorMessage(e))
  }
}

function accelerate(d: DetectedGame): void {
  const game = library.games.find((g) => g.id === d.gameId)
  if (!game) return
  accel.selectedGame = game
  emit('close')
  router.push('/accelerate')
}

async function add(d: DetectedGame): Promise<void> {
  if (d.executables.length === 0) return
  try {
    await library.addDetected(d)
    added.value = new Set(added.value).add(keyOf(d))
  } catch (e) {
    toast.error('添加失败：' + errorMessage(e))
  }
}

async function rescan(): Promise<void> {
  added.value = new Set()
  try {
    await library.scan()
  } catch (e) {
    toast.error('扫描失败：' + errorMessage(e))
  }
}

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape' && props.open) emit('close')
}

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))

watch(
  () => props.open,
  (open) => {
    if (open) {
      added.value = new Set()
      rescan()
    }
  },
  { immediate: true }
)
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" @click.self="emit('close')">
      <div class="dialog">
        <div class="d-head">
          <h3>扫描本机游戏</h3>
          <button class="x" @click="emit('close')"><AppIcon name="close" :size="16" /></button>
        </div>

        <div class="d-body">
          <div v-if="library.scanning" class="muted small">扫描中…</div>

          <template v-else-if="library.scanResult">
            <div v-if="library.scanResult.games.length === 0" class="muted small">
              没有发现已安装的游戏
            </div>

            <template v-else>
              <div v-if="matched.length" class="section">
                <div class="sec-title">已在游戏库（{{ matched.length }}）</div>
                <div class="rows">
                  <div v-for="d in matched" :key="keyOf(d)" class="row">
                    <span class="chip">{{ d.source }}</span>
                    <div class="row-main">
                      <div class="row-name">{{ d.name }}</div>
                      <div class="muted small">
                        匹配：{{ d.gameName }}
                        <span v-if="d.matchedBy">（{{ d.matchedBy === 'process' ? '按进程' : '按名称' }}）</span>
                      </div>
                    </div>
                    <button
                      class="btn mini"
                      :disabled="isFavorite(d)"
                      @click="favorite(d)"
                    >
                      {{ isFavorite(d) ? '已收藏' : '收藏' }}
                    </button>
                    <button class="btn btn-primary mini" @click="accelerate(d)">去加速</button>
                  </div>
                </div>
              </div>

              <div v-if="unmatched.length" class="section">
                <div class="sec-title">未收录（{{ unmatched.length }}）</div>
                <div class="rows">
                  <div v-for="d in unmatched" :key="keyOf(d)" class="row">
                    <span class="chip">{{ d.source }}</span>
                    <div class="row-main">
                      <div class="row-name">{{ d.name }}</div>
                      <div class="muted small exes">{{ d.executables.slice(0, 3).join('、') }}</div>
                    </div>
                    <button
                      class="btn mini"
                      :disabled="d.executables.length === 0 || added.has(keyOf(d))"
                      :title="d.executables.length === 0 ? '未找到可执行文件' : ''"
                      @click="add(d)"
                    >
                      {{ added.has(keyOf(d)) ? '已添加' : '添加到游戏库' }}
                    </button>
                  </div>
                </div>
                <div class="notice muted small">
                  添加后将按进程加速（自动使用 TUN 模式，需要管理员权限）；如需更准确的规则，可在游戏库中编辑该游戏补充域名/IP。
                </div>
              </div>
            </template>

            <div v-if="library.scanResult.errors.length" class="errors muted small">
              <div v-for="(e, i) in library.scanResult.errors" :key="i">{{ e }}</div>
            </div>
          </template>
        </div>

        <div class="d-foot">
          <button class="btn" :disabled="library.scanning" @click="rescan">重新扫描</button>
          <button class="btn btn-primary" @click="emit('close')">关闭</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 10vh 20px 20px;
  z-index: 200;
}
.dialog {
  width: 620px;
  max-height: 80vh;
  overflow-y: auto;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-lg, 0 20px 60px rgba(0, 0, 0, 0.4));
}
.d-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}
.d-head h3 {
  font-size: 15px;
  font-weight: 600;
}
.x {
  color: var(--text-3);
  font-size: 16px;
  padding: 4px;
}
.d-body {
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.sec-title {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.chip {
  flex: none;
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 6px;
  background: var(--surface-3);
  color: var(--text-2);
}
.row-main {
  flex: 1;
  min-width: 0;
}
.row-name {
  font-size: 13px;
  font-weight: 600;
}
.exes {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.notice {
  margin-top: 2px;
}
.errors {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.mini {
  flex: none;
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
}
.d-foot {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
}
.btn {
  height: 38px;
  padding: 0 20px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  background: var(--surface-2);
  color: var(--text-2);
}
.btn-primary {
  background: var(--brand);
  color: #fff;
}
.btn:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
