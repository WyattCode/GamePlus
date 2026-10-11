<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { backend } from '@/api/backend'
import { useLibraryStore } from '@/stores/library'
import { errorMessage, useToastStore } from '@/stores/toast'
import { pickExe, validateLaunchTarget } from '@/utils/launchApi'
import type { Game } from '@shared/types'

const props = defineProps<{ open: boolean; game: Game | null }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const library = useLibraryStore()
const toast = useToastStore()

const target = ref('')
const busy = ref(false)
const detecting = ref(false)
const suggestions = ref<string[]>([])

// Empty is allowed: saving an empty target clears the launch method.
const validationError = computed(() =>
  target.value.trim() ? validateLaunchTarget(target.value) : null
)
const canSave = computed(() => !busy.value && !validationError.value)

watch(
  () => props.open,
  (open) => {
    if (open) {
      target.value = props.game?.launchTarget ?? ''
      suggestions.value = []
    }
  }
)

async function chooseExe(): Promise<void> {
  const path = await pickExe()
  if (path) target.value = path
}

async function detect(): Promise<void> {
  if (!props.game || detecting.value) return
  detecting.value = true
  try {
    suggestions.value = await backend.suggestLaunch(props.game.id)
  } catch (e) {
    toast.error('自动检测失败：' + errorMessage(e))
  } finally {
    detecting.value = false
  }
}

function clearTarget(): void {
  target.value = ''
  suggestions.value = []
}

async function save(): Promise<void> {
  if (!props.game || !canSave.value) return
  busy.value = true
  try {
    await library.setGameLaunch(props.game.id, target.value.trim())
    toast.info('已保存启动方式')
    emit('close')
  } catch (e) {
    toast.error(errorMessage(e))
  } finally {
    busy.value = false
  }
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
          <h3>启动方式{{ game ? ` · ${game.name}` : '' }}</h3>
          <button class="x" :disabled="busy" @click="emit('close')">
            <AppIcon name="close" :size="16" />
          </button>
        </div>

        <div class="d-body">
          <p class="hint muted small">
            支持 Steam / Epic 启动链接，或本机游戏 .exe 完整路径；留空保存表示清除启动方式。
          </p>

          <input
            v-model="target"
            class="input"
            type="text"
            spellcheck="false"
            placeholder="例如 steam://rungameid/1172470 或 D:\Games\game.exe"
          />

          <div v-if="validationError" class="err">{{ validationError }}</div>

          <div class="btn-row">
            <button class="btn mini" :disabled="busy" @click="chooseExe">选择 .exe…</button>
            <button class="btn mini" :disabled="busy || detecting || !game" @click="detect">
              {{ detecting ? '检测中…' : '自动检测' }}
            </button>
            <button class="btn mini" :disabled="busy || !target" @click="clearTarget">清除</button>
          </div>

          <div v-if="suggestions.length" class="sug">
            <div class="muted small sug-title">检测到以下启动方式，点击填入：</div>
            <button
              v-for="s in suggestions"
              :key="s"
              type="button"
              class="sug-item"
              @click="target = s"
            >
              {{ s }}
            </button>
          </div>
        </div>

        <div class="d-foot">
          <button class="btn" :disabled="busy" @click="emit('close')">取消</button>
          <button class="btn btn-primary" :disabled="!canSave" @click="save">保存</button>
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
.hint {
  margin: 0;
}
.input {
  width: 100%;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--bg);
  color: var(--text);
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 13px;
  outline: none;
}
.input:focus {
  border-color: var(--brand);
}
.err {
  padding: 8px 12px;
  border-radius: var(--radius-sm);
  background: rgba(220, 53, 69, 0.1);
  color: var(--danger);
  font-size: 12px;
}
.btn-row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.sug {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.sug-title {
  margin-bottom: 2px;
}
.sug-item {
  text-align: left;
  padding: 8px 12px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--text-2);
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 12px;
  word-break: break-all;
  cursor: pointer;
}
.sug-item:hover {
  background: var(--brand-soft);
  color: var(--brand);
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
