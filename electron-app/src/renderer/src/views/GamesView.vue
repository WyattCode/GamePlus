<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import GameGlyph from '@/components/GameGlyph.vue'
import GameEditorDialog from '@/components/GameEditorDialog.vue'
import LibraryScanDialog from '@/components/LibraryScanDialog.vue'
import { useLibraryStore } from '@/stores/library'
import { useAccelStore } from '@/stores/accel'
import { useToastStore, errorMessage } from '@/stores/toast'
import { gameColor } from '@/utils/gameIcon'
import type { Game } from '@shared/types'

const library = useLibraryStore()
const accel = useAccelStore()
const toast = useToastStore()
const router = useRouter()

const search = ref('')
const activeCat = ref('全部')

const editorOpen = ref(false)
const editing = ref<Game | null>(null)
const scanOpen = ref(false)

function openAdd(): void {
  editing.value = null
  editorOpen.value = true
}

function openEdit(g: Game): void {
  editing.value = g
  editorOpen.value = true
}

async function onSave(data: Partial<Game>): Promise<void> {
  try {
    if (editing.value) {
      await library.updateGame(editing.value.id, data)
    } else {
      await library.addGame(data)
    }
    editorOpen.value = false
  } catch (e) {
    toast.error('保存游戏失败：' + errorMessage(e))
  }
}

async function onRemove(g: Game): Promise<void> {
  if (!g.custom) return
  try {
    await library.removeGame(g.id)
  } catch (e) {
    toast.error('删除游戏失败：' + errorMessage(e))
  }
}

async function onToggleFavorite(id: string): Promise<void> {
  try {
    await library.toggleFavorite(id)
  } catch (e) {
    toast.error('操作失败：' + errorMessage(e))
  }
}

async function onToggleAuto(g: Game): Promise<void> {
  try {
    await library.setGameAuto(g.id, !g.autoAccelerate)
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

async function onToggleServer(g: Game): Promise<void> {
  const next = g.serverChoice === 'cn' ? 'intl' : 'cn'
  try {
    await library.setGameServer(g.id, next)
  } catch (e) {
    toast.error(errorMessage(e))
  }
}

const categories = computed(() => {
  const set = new Set<string>()
  library.games.forEach((g) => g.categories.forEach((c) => set.add(c)))
  return ['全部', '收藏', ...[...set].sort()]
})

const filtered = computed(() =>
  library.sortedGames.filter((g) => {
    const q = search.value.trim().toLowerCase()
    const matchQ = !q || g.name.toLowerCase().includes(q)
    const matchC =
      activeCat.value === '全部' ||
      (activeCat.value === '收藏' ? g.favorite : g.categories.includes(activeCat.value))
    return matchQ && matchC
  })
)

function pick(g: Game): void {
  accel.selectedGame = g
  router.push('/accelerate')
}
</script>

<template>
  <div class="page">
    <div class="head">
      <div>
        <h1 class="page-title">游戏库</h1>
        <p class="page-sub">{{ library.games.length }} 款游戏 · 点击卡片开始加速</p>
      </div>
      <div class="head-actions">
        <button class="btn" @click="scanOpen = true"><AppIcon name="search" :size="18" /> 扫描本机游戏</button>
        <button class="btn btn-primary" @click="openAdd"><AppIcon name="plus" :size="18" /> 添加游戏</button>
      </div>
    </div>

    <div class="toolbar">
      <div class="search">
        <AppIcon name="search" :size="18" />
        <input v-model="search" placeholder="搜索游戏…" />
      </div>
    </div>

    <div class="cats">
      <button
        v-for="c in categories"
        :key="c"
        class="cat"
        :class="{ active: activeCat === c }"
        @click="activeCat = c"
      >
        {{ c }}
      </button>
    </div>

    <div class="grid">
      <div v-for="g in filtered" :key="g.id" class="tile" @click="pick(g)">
        <button class="fav" :class="{ on: g.favorite }" @click.stop="onToggleFavorite(g.id)">
          <AppIcon name="star" :size="16" />
        </button>
        <div v-if="g.custom" class="tile-actions" @click.stop>
          <button class="tile-btn" title="编辑" @click="openEdit(g)"><AppIcon name="edit" :size="14" /></button>
          <button class="tile-btn danger" title="删除" @click="onRemove(g)"><AppIcon name="trash" :size="14" /></button>
        </div>
        <div class="tile-glyph" :style="{ '--a': gameColor(g.id) }">
          <GameGlyph :id="g.id" :name="g.name" :size="34" />
        </div>
        <div class="tile-name">{{ g.name }}</div>
        <div class="tile-region">{{ g.regionText }}</div>
        <div class="tile-tags">
          <span v-if="g.custom" class="tag custom">自定义</span>
          <span v-for="c in g.categories.slice(0, 2)" :key="c" class="tag">{{ c }}</span>
          <button
            v-if="g.hasServerChoice"
            class="tag server-tag"
            title="点击切换服务器"
            @click.stop="onToggleServer(g)"
          >
            {{ g.serverChoice === 'cn' ? '国服' : '国际服' }}
          </button>
        </div>
        <button
          class="auto-chip"
          :class="{ on: g.autoAccelerate, disabled: !g.proxyProcesses || g.proxyProcesses.length === 0 }"
          :title="
            !g.proxyProcesses || g.proxyProcesses.length === 0
              ? '该游戏没有进程规则，无法自动加速'
              : '游戏启动时自动加速'
          "
          :disabled="!g.proxyProcesses || g.proxyProcesses.length === 0"
          @click.stop="onToggleAuto(g)"
        >
          <AppIcon name="bolt" :size="12" /> 自动加速
        </button>
      </div>
    </div>

    <GameEditorDialog :open="editorOpen" :game="editing" @close="editorOpen = false" @save="onSave" />
    <LibraryScanDialog :open="scanOpen" @close="scanOpen = false" />
  </div>
</template>

<style scoped>
.page {
  padding: 26px 28px 40px;
}
.head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 18px;
}
.head-actions {
  display: flex;
  gap: 10px;
}
.toolbar {
  margin-bottom: 14px;
}
.search {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 42px;
  padding: 0 14px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text-3);
}
.search input {
  flex: 1;
  border: none;
  background: none;
  outline: none;
  color: var(--text);
  font-size: 14px;
}
.cats {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 18px;
}
.cat {
  height: 32px;
  padding: 0 14px;
  border-radius: 16px;
  font-size: 13px;
  background: var(--surface-2);
  color: var(--text-2);
  transition: all 0.14s;
}
.cat:hover {
  background: var(--surface-3);
}
.cat.active {
  background: var(--brand);
  color: #fff;
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 14px;
}
.tile {
  position: relative;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 18px;
  cursor: pointer;
  transition: transform 0.14s, border-color 0.14s;
}
.tile:hover {
  transform: translateY(-3px);
  border-color: var(--brand);
}
.fav {
  position: absolute;
  top: 12px;
  right: 12px;
  color: var(--text-3);
  transition: color 0.14s;
}
.fav.on {
  color: #f5b301;
}
.fav.on :deep(svg) {
  fill: #f5b301;
}
.tile-actions {
  position: absolute;
  top: 10px;
  left: 10px;
  display: flex;
  gap: 6px;
  opacity: 0;
  transition: opacity 0.14s;
}
.tile:hover .tile-actions {
  opacity: 1;
}
.tile-btn {
  width: 26px;
  height: 26px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--surface-3);
  color: var(--text-2);
}
.tile-btn:hover {
  color: var(--text);
}
.tile-btn.danger:hover {
  color: #e5484d;
}
.tile-glyph {
  width: 56px;
  height: 56px;
  border-radius: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  background: var(--a);
}
.tile-name {
  font-weight: 600;
  font-size: 15px;
  margin-top: 14px;
}
.tile-region {
  color: var(--text-3);
  font-size: 12px;
  margin-top: 3px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tile-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 12px;
}
.tag {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 6px;
  background: var(--surface-2);
  color: var(--text-2);
}
.tag.custom {
  background: var(--brand-soft);
  color: var(--brand-600);
}
.server-tag {
  background: var(--brand-soft);
  color: var(--brand-600);
  font-weight: 600;
  cursor: pointer;
  transition: all 0.14s;
}
.server-tag:hover {
  background: var(--brand);
  color: #fff;
}
.auto-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-top: 10px;
  height: 24px;
  padding: 0 8px;
  border-radius: 6px;
  font-size: 11px;
  background: var(--surface-2);
  color: var(--text-3);
  transition: all 0.14s;
}
.auto-chip:hover:not(:disabled) {
  color: var(--text);
}
.auto-chip.on {
  background: var(--brand-soft);
  color: var(--brand-600);
  font-weight: 600;
}
.auto-chip:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
