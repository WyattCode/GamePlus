<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { backend } from '@/api/backend'
import { useLibraryStore } from '@/stores/library'
import { useToastStore, errorMessage } from '@/stores/toast'

const library = useLibraryStore()
const toast = useToastStore()
const name = ref('')
const link = ref('')
const busy = ref(false)
const updatingId = ref<string | null>(null)
const autoUpdateHours = ref(0)
const savingAuto = ref(false)

const AUTO_UPDATE_OPTIONS: Array<{ value: number; label: string }> = [
  { value: 0, label: '关闭' },
  { value: 6, label: '每 6 小时' },
  { value: 12, label: '每 12 小时' },
  { value: 24, label: '每 24 小时' },
  { value: 48, label: '每 48 小时' }
]

onMounted(async () => {
  try {
    const settings = await backend.getSettings()
    autoUpdateHours.value = settings.subscriptionAutoUpdateHours
  } catch (e) {
    toast.error('读取设置失败：' + errorMessage(e))
  }
})

async function saveAutoUpdate(): Promise<void> {
  if (savingAuto.value) return
  savingAuto.value = true
  try {
    const settings = await backend.setSettings({
      subscriptionAutoUpdateHours: autoUpdateHours.value
    })
    autoUpdateHours.value = settings.subscriptionAutoUpdateHours
    toast.info('已保存')
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  } finally {
    savingAuto.value = false
  }
}

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

    <div class="card auto-row">
      <div class="auto-label">
        <AppIcon name="refresh" :size="16" />
        <span>自动更新</span>
      </div>
      <select
        v-model.number="autoUpdateHours"
        class="input"
        :disabled="savingAuto"
        @change="saveAutoUpdate"
      >
        <option v-for="opt in AUTO_UPDATE_OPTIONS" :key="opt.value" :value="opt.value">
          {{ opt.label }}
        </option>
      </select>
      <span class="muted small">后台按间隔自动更新所有订阅；更新失败只记录日志</span>
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
.auto-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  margin-bottom: 12px;
}
.auto-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-2);
}
.auto-row .input {
  height: 34px;
  padding: 0 10px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--bg);
  color: var(--text);
  font-size: 13px;
  outline: none;
}
.auto-row .input:focus {
  border-color: var(--brand);
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
