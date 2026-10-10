<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import jsQR from 'jsqr'
import AppIcon from '@/components/AppIcon.vue'
import { useLibraryStore } from '@/stores/library'
import { useToastStore, errorMessage } from '@/stores/toast'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const library = useLibraryStore()
const toast = useToastStore()

const text = ref('')
const busy = ref(false)
const resultLine = ref('')
const qrNotice = ref('')
const fileInput = ref<HTMLInputElement | null>(null)

const canImport = computed(() => text.value.trim().length > 0 && !busy.value)

function flashQr(msg: string): void {
  qrNotice.value = msg
  setTimeout(() => {
    if (qrNotice.value === msg) qrNotice.value = ''
  }, 3000)
}

/** Decodes a QR code from an image source, downscaling so the longer side is at most 1600 px. */
async function decodeQr(src: string): Promise<string | null> {
  const img = new Image()
  await new Promise<void>((resolve, reject) => {
    img.onload = () => resolve()
    img.onerror = () => reject(new Error('图片加载失败'))
    img.src = src
  })

  const maxSide = 1600
  const longer = Math.max(img.naturalWidth, img.naturalHeight)
  const scale = longer > maxSide ? maxSide / longer : 1
  const w = Math.max(1, Math.round(img.naturalWidth * scale))
  const h = Math.max(1, Math.round(img.naturalHeight * scale))

  const canvas = document.createElement('canvas')
  canvas.width = w
  canvas.height = h
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('无法创建画布')
  ctx.drawImage(img, 0, 0, w, h)

  const data = ctx.getImageData(0, 0, w, h)
  const code = jsQR(data.data, data.width, data.height)
  return code?.data ?? null
}

function appendDecoded(decoded: string): void {
  const trimmed = decoded.trim()
  if (!trimmed) {
    flashQr('未识别到二维码')
    return
  }
  text.value = text.value.trim() ? text.value.replace(/\s*$/, '') + '\n' + trimmed : trimmed
  flashQr('已识别二维码')
}

async function onFileChange(e: Event): Promise<void> {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  busy.value = true
  try {
    const url = URL.createObjectURL(file)
    try {
      const decoded = await decodeQr(url)
      if (decoded) appendDecoded(decoded)
      else flashQr('未识别到二维码')
    } finally {
      URL.revokeObjectURL(url)
    }
  } catch (err) {
    flashQr('未识别到二维码')
  } finally {
    busy.value = false
  }
}

async function pasteFromClipboard(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.read !== 'function') {
      flashQr('当前环境不支持读取剪贴板图片')
      return
    }
    let items: ClipboardItems
    try {
      items = await navigator.clipboard.read()
    } catch {
      flashQr('无法读取剪贴板，请检查权限或改用「识别二维码图片」')
      return
    }
    let imageBlob: Blob | null = null
    for (const item of items) {
      const type = item.types.find((t) => t.startsWith('image/'))
      if (type) {
        imageBlob = await item.getType(type)
        break
      }
    }
    if (!imageBlob) {
      flashQr('剪贴板中没有图片')
      return
    }
    const url = URL.createObjectURL(imageBlob)
    try {
      const decoded = await decodeQr(url)
      if (decoded) appendDecoded(decoded)
      else flashQr('未识别到二维码')
    } finally {
      URL.revokeObjectURL(url)
    }
  } catch (err) {
    flashQr('未识别到二维码')
  } finally {
    busy.value = false
  }
}

async function pasteClipboardText(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.readText !== 'function') {
      flashQr('无法读取剪贴板')
      return
    }
    let raw: string
    try {
      raw = await navigator.clipboard.readText()
    } catch {
      flashQr('无法读取剪贴板')
      return
    }
    const trimmed = raw.trim()
    if (!trimmed) {
      flashQr('剪贴板为空')
      return
    }
    text.value = text.value.trim() ? text.value.replace(/\s*$/, '') + '\n' + trimmed : trimmed
    flashQr('已粘贴剪贴板文本')
  } finally {
    busy.value = false
  }
}

async function doImport(): Promise<void> {
  if (!canImport.value) return
  busy.value = true
  resultLine.value = ''
  try {
    const result = await library.importNodes(text.value)
    let line: string
    if (result.format === 'subscription') {
      line = `已添加订阅并更新，共 ${result.added} 个节点`
    } else {
      line = `新增 ${result.added} 个节点 · 重复 ${result.duplicates} 个 · 无效 ${result.invalid} 个`
    }
    resultLine.value = line
    toast.info(line)
    text.value = ''
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
      resultLine.value = ''
      qrNotice.value = ''
    }
  }
)
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" @click.self="!busy && emit('close')">
      <div class="dialog">
        <div class="d-head">
          <h3>导入节点</h3>
          <button class="x" :disabled="busy" @click="emit('close')">
            <AppIcon name="close" :size="16" />
          </button>
        </div>

        <div class="d-body">
          <textarea
            v-model="text"
            class="ta"
            rows="6"
            placeholder="粘贴订阅链接、分享链接（ss:// vmess:// trojan:// vless:// hysteria2://）、Base64 订阅内容或 Clash 配置"
          />

          <div class="qr-row">
            <button class="btn mini" :disabled="busy" @click="fileInput?.click()">
              <AppIcon name="image" :size="15" /> 识别二维码图片
            </button>
            <button class="btn mini" :disabled="busy" @click="pasteFromClipboard">
              <AppIcon name="copy" :size="15" /> 粘贴剪贴板图片
            </button>
            <button class="btn mini" :disabled="busy" @click="pasteClipboardText">
              <AppIcon name="copy" :size="15" /> 粘贴剪贴板文本
            </button>
            <span v-if="qrNotice" class="muted small">{{ qrNotice }}</span>
          </div>

          <input
            ref="fileInput"
            type="file"
            accept="image/*"
            class="hidden-file"
            @change="onFileChange"
          />

          <div v-if="resultLine" class="result">{{ resultLine }}</div>
        </div>

        <div class="d-foot">
          <button class="btn" :disabled="busy" @click="emit('close')">取消</button>
          <button class="btn btn-primary" :disabled="!canImport" @click="doImport">
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
.qr-row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.hidden-file {
  display: none;
}
.result {
  padding: 10px 14px;
  border-radius: var(--radius-sm);
  background: var(--brand-soft);
  color: var(--brand-600);
  font-size: 13px;
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
