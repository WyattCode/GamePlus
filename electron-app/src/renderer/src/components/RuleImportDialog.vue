<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { RuleConflict } from '@shared/types'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const library = useLibraryStore()
const toast = useToastStore()

const text = ref('')
const busy = ref(false)
const notice = ref('')
const conflict = ref<RuleConflict | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

const MAX_BYTES = 64 * 1024

const canImport = computed(() => text.value.trim().length > 0 && !busy.value)

async function onFileChange(e: Event): Promise<void> {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > MAX_BYTES) {
    notice.value = '规则内容过大'
    return
  }
  try {
    text.value = await file.text()
    notice.value = ''
  } catch {
    notice.value = '无法读取文件'
  }
}

async function pasteClipboardText(): Promise<void> {
  if (busy.value) return
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.readText !== 'function') {
      notice.value = '无法读取剪贴板'
      return
    }
    const raw = await navigator.clipboard.readText()
    const trimmed = raw.trim()
    if (!trimmed) {
      notice.value = '剪贴板为空'
      return
    }
    text.value = trimmed
    notice.value = ''
  } catch {
    notice.value = '无法读取剪贴板'
  }
}

async function doImport(resolution?: 'replace' | 'copy'): Promise<void> {
  if (!canImport.value) return
  busy.value = true
  try {
    const result = await library.importGameRule(text.value, resolution ?? 'ask')
    if (result.status === 'conflict') {
      conflict.value = result.conflict
      return
    }
    const game = result.games.find((g) => g.id === result.importedId)
    toast.info(`已导入规则：${game?.name ?? result.importedId}`)
    text.value = ''
    conflict.value = null
    emit('close')
  } catch (e) {
    toast.error('导入失败：' + errorMessage(e))
  } finally {
    busy.value = false
  }
}

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape' && props.open && !busy.value) emit('close')
}

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))

watch(
  () => props.open,
  (open) => {
    if (open) {
      conflict.value = null
      notice.value = ''
    }
  }
)
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" @click.self="!busy && emit('close')">
      <div class="dialog">
        <div class="d-head">
          <h3>导入规则</h3>
          <button class="x" :disabled="busy" @click="emit('close')">
            <AppIcon name="close" :size="16" />
          </button>
        </div>

        <div class="d-body">
          <textarea
            v-model="text"
            class="ta"
            rows="6"
            placeholder="粘贴 Game+ 规则 JSON 或 gameplus-rule: 分享串"
          />

          <div class="row">
            <button class="btn mini" :disabled="busy" @click="fileInput?.click()">
              选择 .json 文件
            </button>
            <button class="btn mini" :disabled="busy" @click="pasteClipboardText">
              <AppIcon name="copy" :size="15" /> 粘贴剪贴板
            </button>
            <span v-if="notice" class="muted small">{{ notice }}</span>
          </div>

          <input
            ref="fileInput"
            type="file"
            accept=".json,application/json"
            class="hidden-file"
            @change="onFileChange"
          />

          <div v-if="conflict" class="conflict">
            <template v-if="conflict.kind === 'builtin'">
              <p class="conflict-text">已存在内置游戏「{{ conflict.existingName }}」，内置游戏不能被替换。</p>
              <div class="conflict-actions">
                <button class="btn mini" :disabled="busy" @click="doImport('copy')">另存为副本</button>
                <button class="btn mini" :disabled="busy" @click="conflict = null">取消</button>
              </div>
            </template>
            <template v-else>
              <p class="conflict-text">已存在自定义游戏「{{ conflict.existingName }}」。</p>
              <div class="conflict-actions">
                <button class="btn mini" :disabled="busy" @click="doImport('replace')">替换</button>
                <button class="btn mini" :disabled="busy" @click="doImport('copy')">另存为副本</button>
                <button class="btn mini" :disabled="busy" @click="conflict = null">取消</button>
              </div>
            </template>
          </div>
        </div>

        <div class="d-foot">
          <button class="btn" :disabled="busy" @click="emit('close')">取消</button>
          <button class="btn btn-primary" :disabled="!canImport" @click="doImport()">
            {{ busy ? '导入中…' : '导入' }}
          </button>
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
  gap: 12px;
}
.ta {
  width: 100%;
  resize: vertical;
  padding: 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--bg);
  color: var(--text);
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 13px;
  line-height: 1.5;
  outline: none;
}
.ta:focus {
  border-color: var(--brand);
}
.row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.hidden-file {
  display: none;
}
.conflict {
  padding: 12px 14px;
  border-radius: var(--radius-sm);
  background: var(--brand-soft);
  border: 1px solid var(--border);
}
.conflict-text {
  font-size: 13px;
  color: var(--text-2);
  margin-bottom: 10px;
}
.conflict-actions {
  display: flex;
  gap: 8px;
}
.mini {
  flex: none;
  height: 32px;
  padding: 0 14px;
  font-size: 12px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.muted {
  color: var(--text-3);
}
.small {
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
