<script setup lang="ts">
import { ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { useToastStore, errorMessage } from '@/stores/toast'

const library = useLibraryStore()
const toast = useToastStore()
const name = ref('')
const link = ref('')
const busy = ref(false)
const updatingId = ref<string | null>(null)

async function add(): Promise<void> {
  if (!link.value.trim() || busy.value) return
  busy.value = true
  try {
    await library.addSubscription(name.value.trim(), link.value.trim())
    name.value = ''
    link.value = ''
  } catch (e) {
    toast.error('添加订阅失败：' + errorMessage(e))
  } finally {
    busy.value = false
  }
}

async function remove(id: string): Promise<void> {
  try {
    await library.removeSubscription(id)
  } catch (e) {
    toast.error('删除订阅失败：' + errorMessage(e))
  }
}

async function update(id: string): Promise<void> {
  updatingId.value = id
  try {
    await library.updateSubscription(id)
  } catch (e) {
    toast.error('更新订阅失败：' + errorMessage(e))
  } finally {
    updatingId.value = null
  }
}

async function updateAll(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    const failed = await library.updateAllSubscriptions()
    if (failed > 0) toast.error(`有 ${failed} 个订阅更新失败`)
  } catch (e) {
    toast.error('更新订阅失败：' + errorMessage(e))
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="page">
    <div class="head">
      <div>
        <h1 class="page-title">订阅</h1>
        <p class="page-sub">{{ library.subscriptions.length }} 个订阅</p>
      </div>
      <button class="btn btn-primary" :disabled="busy" @click="updateAll">
        <AppIcon name="refresh" :size="18" /> {{ busy ? '更新中…' : '全部更新' }}
      </button>
    </div>

    <div class="card add">
      <div class="field">
        <AppIcon name="edit" :size="16" />
        <input v-model="name" class="bare" placeholder="名称" />
      </div>
      <div class="field grow">
        <AppIcon name="cloud" :size="16" />
        <input v-model="link" class="bare" placeholder="https://…订阅链接" />
      </div>
      <button class="btn btn-primary" :disabled="busy" @click="add">
        <AppIcon name="plus" :size="18" /> 添加
      </button>
    </div>

    <div v-if="library.subscriptions.length" class="list">
      <div v-for="s in library.subscriptions" :key="s.id" class="sub card">
        <div class="sub-ico"><AppIcon name="cloud" :size="20" /></div>
        <div class="sub-main">
          <div class="sub-name">{{ s.remark }}</div>
          <div class="sub-link">{{ s.link }}</div>
        </div>
        <span class="count">{{ s.serverCount }} 节点</span>
        <button class="icon-btn" :disabled="updatingId === s.id" @click="update(s.id)">
          <AppIcon name="refresh" :size="17" />
        </button>
        <button class="icon-btn danger" @click="remove(s.id)"><AppIcon name="trash" :size="17" /></button>
      </div>
    </div>

    <div v-else class="empty muted">
      <AppIcon name="cloud" :size="30" />
      <p>还没有订阅。粘贴机场订阅链接即可批量导入线路。</p>
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
.add {
  display: flex;
  gap: 12px;
  padding: 16px;
  align-items: center;
}
.field {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 42px;
  padding: 0 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--bg);
  color: var(--text-3);
  min-width: 180px;
}
.field.grow {
  flex: 1;
}
.bare {
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
  margin-top: 16px;
}
.sub {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 14px 16px;
}
.sub-ico {
  width: 40px;
  height: 40px;
  border-radius: 12px;
  background: var(--brand-soft);
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
}
.sub-main {
  flex: 1;
  min-width: 0;
}
.sub-name {
  font-weight: 600;
}
.sub-link {
  font-size: 12px;
  color: var(--text-3);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.count {
  font-size: 12px;
  color: var(--text-2);
  background: var(--surface-2);
  padding: 4px 10px;
  border-radius: 6px;
}
.icon-btn {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  color: var(--text-2);
  display: flex;
  align-items: center;
  justify-content: center;
}
.icon-btn:hover {
  background: var(--surface-2);
  color: var(--text);
}
.icon-btn.danger:hover {
  background: rgba(240, 68, 56, 0.12);
  color: var(--danger);
}
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 60px;
  text-align: center;
}
</style>
