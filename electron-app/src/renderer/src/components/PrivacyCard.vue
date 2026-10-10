<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useToastStore, errorMessage } from '@/stores/toast'
import type { CrashFileInfo, TelemetryConfig, TelemetryEvent } from '@shared/types'

const toast = useToastStore()

const supported = computed(() => !!window.gameplus?.telemetry)

const config = ref<TelemetryConfig>({ enabled: false, endpoint: '', installId: null })
const loaded = ref(false)
const saving = ref(false)

const advancedOpen = ref(false)
const endpointDraft = ref('')

const eventsOpen = ref(false)
const events = ref<TelemetryEvent[]>([])
const eventsLoading = ref(false)

const crashes = ref<CrashFileInfo[]>([])

const PRIVACY_TEXT =
  '开启后，Game+ 会在本机记录加速成功/失败、使用的加速模式、失败原因（已去除 IP、域名、路径和用户名）以及崩溃报告。不会收集游戏名称、节点信息、账号或浏览内容。只有在配置了上报地址时才会发送，当前版本默认不配置上报地址，数据只保存在本机。关闭后不再记录。'

const CONFIRM_TEXT =
  '开启后，Game+ 会在本机记录加速成功/失败、使用的加速模式、失败原因（已去除 IP、域名、路径和用户名）以及崩溃报告。不会收集游戏名称、节点信息、账号或浏览内容。只有在配置了上报地址时才会发送，当前版本默认不配置上报地址，数据只保存在本机。是否开启？'

const endpointEmpty = computed(() => !config.value.endpoint.trim())

onMounted(async () => {
  if (!supported.value) {
    loaded.value = true
    return
  }
  try {
    config.value = await window.gameplus!.telemetry!.getConfig()
    endpointDraft.value = config.value.endpoint
  } catch {
    // ignore — keep defaults
  }
  try {
    crashes.value = await window.gameplus!.telemetry!.listCrashes()
  } catch {
    // ignore
  }
  loaded.value = true
})

async function persist(patch: Partial<TelemetryConfig>): Promise<void> {
  saving.value = true
  try {
    config.value = await window.gameplus!.telemetry!.setConfig(patch)
    endpointDraft.value = config.value.endpoint
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  } finally {
    saving.value = false
  }
}

async function toggleEnabled(enabled: boolean): Promise<void> {
  if (enabled) {
    const ok = window.confirm(CONFIRM_TEXT)
    if (!ok) return
  }
  await persist({ enabled })
}

/** Validates the endpoint: empty is allowed; https always; http only for loopback hosts. */
function validateEndpoint(raw: string): string | null {
  const v = raw.trim()
  if (!v) return null
  let url: URL
  try {
    url = new URL(v)
  } catch {
    return '地址格式无效'
  }
  if (url.protocol === 'https:') return null
  if (url.protocol === 'http:') {
    const host = url.hostname.toLowerCase()
    const loopback =
      host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]'
    if (loopback) return null
    return '仅允许 https，或本机回环地址的 http'
  }
  return '仅支持 https 地址'
}

async function saveEndpoint(): Promise<void> {
  const err = validateEndpoint(endpointDraft.value)
  if (err) {
    toast.error(err)
    return
  }
  await persist({ endpoint: endpointDraft.value.trim() })
  toast.info('已保存')
}

async function toggleEvents(): Promise<void> {
  eventsOpen.value = !eventsOpen.value
  if (!eventsOpen.value) return
  await loadEvents()
}

async function loadEvents(): Promise<void> {
  if (!supported.value) return
  eventsLoading.value = true
  try {
    events.value = await window.gameplus!.telemetry!.getRecent(50)
  } catch (e) {
    toast.error('读取失败：' + errorMessage(e))
  } finally {
    eventsLoading.value = false
  }
}

async function clearEvents(): Promise<void> {
  if (!window.confirm('确定清除本机已收集的诊断数据？此操作不可撤销。')) return
  try {
    await window.gameplus!.telemetry!.clear()
    events.value = []
    toast.info('已清除')
  } catch (e) {
    toast.error('清除失败：' + errorMessage(e))
  }
}

async function openCrashFolder(): Promise<void> {
  try {
    await window.gameplus!.telemetry!.openCrashFolder()
  } catch (e) {
    toast.error('打开失败：' + errorMessage(e))
  }
}

function formatEvent(e: TelemetryEvent): string {
  try {
    return JSON.stringify(e)
  } catch {
    return String(e)
  }
}
</script>

<template>
  <div class="card group">
    <div class="g-title">隐私与诊断数据</div>

    <div v-if="!supported" class="setting">
      <div class="muted small">仅桌面版支持</div>
    </div>

    <template v-else>
      <div class="setting">
        <div class="privacy-info">
          <div class="s-name">帮助改进 Game+（匿名使用统计与崩溃报告）</div>
          <div class="muted small">{{ PRIVACY_TEXT }}</div>
        </div>
        <label class="switch" :class="{ disabled: saving || !loaded }">
          <input
            type="checkbox"
            :checked="config.enabled"
            :disabled="saving || !loaded"
            @change="toggleEnabled(($event.target as HTMLInputElement).checked)"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>

      <div class="setting">
        <div class="adv-head">
          <button class="btn diag-btn" @click="advancedOpen = !advancedOpen">
            <AppIcon :name="advancedOpen ? 'chevron-down' : 'chevron-right'" :size="14" /> 高级
          </button>
        </div>
      </div>

      <div v-if="advancedOpen" class="setting">
        <div class="endpoint-input">
          <div class="s-name">上报地址</div>
          <input
            v-model="endpointDraft"
            class="input"
            placeholder="https://…，留空则不上报"
            :disabled="saving"
          />
          <div v-if="endpointEmpty" class="muted small">未配置上报地址：数据不会离开本机</div>
        </div>
        <button class="btn diag-btn" :disabled="saving" @click="saveEndpoint">保存</button>
      </div>

      <div class="setting">
        <div>
          <div class="s-name">已收集的数据</div>
          <div class="muted small">查看本机最近记录的事件，可随时清除</div>
        </div>
        <div class="row-actions">
          <button class="btn diag-btn" :disabled="eventsLoading" @click="toggleEvents">
            {{ eventsOpen ? '收起' : '查看已收集的数据' }}
          </button>
          <button class="btn diag-btn" :disabled="eventsLoading" @click="clearEvents">
            清除本地数据
          </button>
        </div>
      </div>

      <div v-if="eventsOpen" class="setting events-block">
        <div class="events-list mono">
          <div v-if="eventsLoading" class="muted small">加载中…</div>
          <div v-else-if="events.length === 0" class="muted small">暂无记录</div>
          <div v-else v-for="(e, i) in events" :key="i" class="event-line">{{ formatEvent(e) }}</div>
        </div>
      </div>

      <div class="setting">
        <div>
          <div class="s-name">崩溃报告</div>
          <div class="muted small">
            本机已保存 {{ crashes.length }} 个崩溃报告文件。崩溃报告始终仅保存在本机，除非你开启上方选项且配置了上报地址。
          </div>
        </div>
        <button class="btn diag-btn" @click="openCrashFolder">打开文件夹</button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.group {
  padding: 8px 20px;
  margin-bottom: 16px;
}
.g-title {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 14px 0 8px;
}
.setting {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 0;
  border-top: 1px solid var(--border);
}
.s-name {
  font-weight: 600;
  font-size: 14px;
}
.small {
  font-size: 12px;
  margin-top: 2px;
}
.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.privacy-info {
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.adv-head {
  display: flex;
  align-items: center;
}
.endpoint-input {
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.endpoint-input .input {
  margin-top: 8px;
  width: 100%;
}
.row-actions {
  display: flex;
  gap: 8px;
  flex: none;
}
.diag-btn {
  flex: none;
  height: 36px;
  padding: 0 18px;
  font-size: 13px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.events-block {
  display: block;
}
.events-list {
  width: 100%;
  max-height: 260px;
  overflow: auto;
  background: var(--surface-2);
  border-radius: 10px;
  padding: 10px 12px;
  font-size: 12px;
  line-height: 1.6;
}
.event-line {
  white-space: pre-wrap;
  word-break: break-all;
}
.switch input {
  display: none;
}
.switch.disabled {
  opacity: 0.5;
}
.track {
  display: block;
  width: 44px;
  height: 26px;
  border-radius: 13px;
  background: var(--surface-3);
  position: relative;
  transition: background 0.16s;
  cursor: pointer;
}
.thumb {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: #fff;
  box-shadow: var(--shadow-sm);
  transition: transform 0.16s;
}
.switch input:checked + .track {
  background: var(--brand);
}
.switch input:checked + .track .thumb {
  transform: translateX(18px);
}
</style>
