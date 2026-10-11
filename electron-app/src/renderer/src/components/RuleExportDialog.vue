<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { Game } from '@shared/types'

const props = defineProps<{ open: boolean; game: Game | null }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const library = useLibraryStore()
const toast = useToastStore()

const busy = ref(false)
const share = ref('')
const json = ref('')
const fileName = ref('')

watch(
  () => props.open,
  async (open) => {
    if (!open || !props.game) return
    busy.value = true
    share.value = ''
    json.value = ''
    fileName.value = ''
    try {
      const res = await library.exportGameRule(props.game.id)
      share.value = res.share
      json.value = res.json
      fileName.value = res.fileName
    } catch (e) {
      toast.error('导出失败：' + errorMessage(e))
      emit('close')
    } finally {
      busy.value = false
    }
  }
)

async function copyShare(): Promise<void> {
  try {
    await navigator.clipboard.writeText(share.value)
    toast.info('已复制分享串')
  } catch {
    toast.error('无法复制，请手动选择文本复制')
  }
}

function saveJson(): void {
  const blob = new Blob([json.value], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName.value
  a.click()
  URL.revokeObjectURL(url)
}

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape' && props.open && !busy.value) emit('close')
}

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" @click.self="!busy && emit('close')">
      <div class="dialog">
        <div class="d-head">
          <h3>导出规则{{ game ? ` · ${game.name}` : '' }}</h3>
          <button class="x" :disabled="busy" @click="emit('close')">
            <AppIcon name="close" :size="16" />
          </button>
        </div>

        <div class="d-body">
          <textarea v-model="share" class="ta" rows="5" readonly placeholder="正在生成…" />

          <p class="hint">对方可在 游戏库 → 导入规则 中粘贴分享串或选择文件导入</p>
        </div>

        <div class="d-foot">
          <button class="btn" :disabled="busy || !json" @click="saveJson">保存为 .json 文件</button>
          <button class="btn btn-primary" :disabled="busy || !share" @click="copyShare">
            <AppIcon name="copy" :size="15" /> 复制分享串
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
  font-size: 12px;
  line-height: 1.5;
  outline: none;
  word-break: break-all;
}
.hint {
  font-size: 12px;
  color: var(--text-3);
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
  display: inline-flex;
  align-items: center;
  gap: 6px;
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
