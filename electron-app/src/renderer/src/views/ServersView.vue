<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { delayColor, fmtDelay } from '@/utils/format'

const library = useLibraryStore()
const search = ref('')
const showAdd = ref(false)
const addLink = ref('')
const busy = ref(false)
const notice = ref('')

const filtered = computed(() =>
  library.servers.filter((s) => {
    const q = search.value.trim().toLowerCase()
    return !q || s.remark.toLowerCase().includes(q) || s.hostname.toLowerCase().includes(q)
  })
)

function flash(msg: string): void {
  notice.value = msg
  setTimeout(() => (notice.value = ''), 3000)
}

async function importFromClipboard(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    const text = await navigator.clipboard.readText()
    if (!text.trim()) return flash('剪贴板为空')
    const added = await library.addServerFromLink(text.trim())
    flash(added > 0 ? `已导入 ${added} 条线路` : '未识别到线路')
  } catch (e) {
    flash('导入失败：' + (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function addFromInput(): Promise<void> {
  if (!addLink.value.trim() || busy.value) return
  busy.value = true
  try {
    const added = await library.addServerFromLink(addLink.value.trim())
    flash(added > 0 ? `已添加 ${added} 条线路` : '未识别到线路')
    addLink.value = ''
    showAdd.value = false
  } catch (e) {
    flash('添加失败：' + (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function remove(id: string): Promise<void> {
  await library.removeServer(id)
}

const typeColor: Record<string, string> = {
  VLESS: '#2e8bff',
  VMess: '#7a5cff',
  Trojan: '#e0533d',
  SS: '#17b26a',
  HY2: '#f79009',
  Socks5: '#9aa0ac',
  WireGuard: '#d946a0'
}
</script>

<template>
  <div class="page">
    <div class="head">
      <div>
        <h1 class="page-title">线路</h1>
        <p class="page-sub">{{ library.servers.length }} 条线路</p>
      </div>
      <div class="actions">
        <button class="btn" :disabled="library.pinging" @click="library.pingAll()">
          <AppIcon name="pulse" :size="18" /> {{ library.pinging ? '测速中…' : '测速' }}
        </button>
        <button class="btn" :disabled="busy" @click="importFromClipboard">
          <AppIcon name="copy" :size="18" /> 从剪贴板导入
        </button>
        <button class="btn btn-primary" @click="showAdd = !showAdd">
          <AppIcon name="plus" :size="18" /> 添加
        </button>
      </div>
    </div>

    <div v-if="showAdd" class="add-row card">
      <AppIcon name="server" :size="16" />
      <input
        v-model="addLink"
        class="bare"
        placeholder="粘贴节点分享链接 (vless:// ss:// trojan:// hysteria2:// …)"
        @keyup.enter="addFromInput"
      />
      <button class="btn btn-primary" :disabled="busy" @click="addFromInput">确认</button>
    </div>

    <div v-if="notice" class="notice">{{ notice }}</div>

    <div class="search">
      <AppIcon name="search" :size="18" />
      <input v-model="search" placeholder="搜索线路…" />
    </div>

    <div class="list">
      <div v-for="s in filtered" :key="s.id" class="node card">
        <span class="type" :style="{ background: typeColor[s.type] || 'var(--text-3)' }">{{
          s.type
        }}</span>
        <div class="node-main">
          <div class="node-name">{{ s.remark }}</div>
          <div class="node-addr">{{ s.hostname }}:{{ s.port }}</div>
        </div>
        <span v-if="s.group !== 'NONE'" class="grp">
          <AppIcon name="cloud" :size="12" /> {{ s.group }}
        </span>
        <span class="delay" :style="{ color: delayColor(s.delay) }">
          <span class="d-dot" :style="{ background: delayColor(s.delay) }" />
          {{ fmtDelay(s.delay) }}
        </span>
        <button class="del" title="删除" @click="remove(s.id)">
          <AppIcon name="trash" :size="16" />
        </button>
      </div>
    </div>
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
  margin-bottom: 16px;
}
.actions {
  display: flex;
  gap: 10px;
}
.btn:disabled {
  opacity: 0.6;
  cursor: default;
}
.add-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  margin-bottom: 12px;
  color: var(--text-3);
}
.add-row .bare {
  flex: 1;
  border: none;
  background: none;
  outline: none;
  color: var(--text);
  font-size: 14px;
}
.notice {
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  background: var(--brand-soft);
  color: var(--brand-600);
  font-size: 13px;
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
  margin-bottom: 16px;
}
.search input {
  flex: 1;
  border: none;
  background: none;
  outline: none;
  color: var(--text);
  font-size: 14px;
}
.list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.node {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 14px 16px;
  transition: border-color 0.14s, box-shadow 0.14s;
}
.node:hover {
  border-color: var(--brand);
  box-shadow: var(--shadow-sm);
}
.type {
  min-width: 56px;
  text-align: center;
  color: #fff;
  font-size: 12px;
  font-weight: 700;
  padding: 6px 10px;
  border-radius: 8px;
}
.node-main {
  flex: 1;
  min-width: 0;
}
.node-name {
  font-weight: 600;
  font-size: 15px;
}
.node-addr {
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 12px;
  color: var(--text-3);
  margin-top: 3px;
}
.grp {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11px;
  color: var(--text-2);
  background: var(--surface-2);
  padding: 4px 9px;
  border-radius: 6px;
}
.delay {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-width: 82px;
  justify-content: center;
  font-weight: 600;
  font-size: 13px;
  background: var(--surface-2);
  padding: 6px 12px;
  border-radius: 8px;
}
.d-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
}
.del {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  color: var(--text-3);
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.14s, background 0.14s, color 0.14s;
}
.node:hover .del {
  opacity: 1;
}
.del:hover {
  background: rgba(240, 68, 56, 0.12);
  color: var(--danger);
}
</style>
