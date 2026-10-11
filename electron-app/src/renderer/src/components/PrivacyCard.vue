<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToastStore, errorMessage } from '@/stores/toast'
import { exportDiagnosticBundle } from '@/utils/diagBundle'
import type { CrashFileInfo, TelemetryConfig, TelemetryEvent } from '@shared/types'

const toast = useToastStore()

const supported = computed(() => !!window.gameplus?.telemetry)

const config = ref<TelemetryConfig>({ enabled: false, endpoint: '', installId: null })
const loaded = ref(false)
const saving = ref(false)
const exporting = ref(false)

const eventsOpen = ref(false)
const events = ref<TelemetryEvent[]>([])
const eventsLoading = ref(false)

const crashes = ref<CrashFileInfo[]>([])

const PRIVACY_TEXT =
  '开启后，Game+ 只在本机记录加速成功/失败、使用的加速模式和失败原因（已去除 IP、域名、路径和用户名），方便排查问题。Game+ 不会上传任何数据，也没有账号系统；不会收集游戏名称、节点信息或浏览内容。关闭后不再记录。'

const CONFIRM_TEXT =
  '开启后，Game+ 只在本机记录加速成功/失败、使用的加速模式和失败原因（已去除 IP、域名、路径和用户名），方便排查问题。Game+ 不会上传任何数据，也没有账号系统。是否开启？'

onMounted(async () => {
  if (!supported.value) {
    loaded.value = true
    return
  }
  try {
    config.value = await window.gameplus!.telemetry!.getConfig()
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

async function exportBundle(): Promise<void> {
  if (exporting.value) return
  exporting.value = true
  try {
    const path = await exportDiagnosticBundle()
    if (path) toast.info(`诊断包已保存：${path}`)
    else toast.info('已取消')
  } catch (e) {
    toast.error('导出失败：' + errorMessage(e))
  } finally {
    exporting.value = false
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
    <div class="g-title">本地诊断记录</div>

    <div v-if="!supported" class="setting">
      <div class="muted small">仅桌面版支持</div>
    </div>

    <template v-else>
      <div class="setting">
        <div class="privacy-info">
          <div class="s-name">记录本地诊断数据</div>
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
            本机已保存 {{ crashes.length }} 个崩溃报告文件。崩溃报告只保存在本机，不会上传。可在提交问题时手动附上。
          </div>
        </div>
        <button class="btn diag-btn" @click="openCrashFolder">打开文件夹</button>
      </div>

      <div class="setting">
        <div class="privacy-info">
          <div class="s-name">导出诊断包</div>
          <div class="muted small">
            包含日志、诊断结果与脱敏后的配置（已移除密码、密钥与订阅链接），分享前请自行检查
          </div>
        </div>
        <button class="btn diag-btn" :disabled="exporting" @click="exportBundle">
          {{ exporting ? '导出中…' : '导出诊断包' }}
        </button>
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
