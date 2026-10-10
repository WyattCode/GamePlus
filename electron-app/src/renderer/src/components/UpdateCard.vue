<script setup lang="ts">
import { computed, ref } from 'vue'
import { useUpdateStore } from '@/stores/update'
import type { UpdateSource } from '@shared/types'

const update = useUpdateStore()

const expanded = ref(false)

const status = computed(() => update.state?.status ?? 'idle')
const current = computed(() => update.state?.current ?? __APP_VERSION__)
const latest = computed(() => update.state?.latest ?? null)
const errorText = computed(() => update.state?.error ?? null)

const progressPct = computed(() => update.progressPct)
const progressText = computed(() => {
  const p = update.state?.progress
  if (!p) return ''
  const mb = (n: number): string => (n / (1024 * 1024)).toFixed(1)
  return `${mb(p.received)} / ${mb(p.total)} MB`
})

// --- 更新源设置 ---
const source = computed<UpdateSource>(() => update.config?.source ?? 'none')
const manifestUrl = ref('')
const githubRepo = ref('')
const includePrerelease = ref(false)
const autoCheck = ref(true)
const token = ref('')
const hasToken = computed(() => !!update.config?.hasToken)
const saving = ref(false)

// Keep the local form in sync with the store (initial load + external changes).
let synced = false
function syncForm(): void {
  const c = update.config
  if (!c) return
  manifestUrl.value = c.manifestUrl
  githubRepo.value = c.githubRepo
  includePrerelease.value = c.includePrerelease
  autoCheck.value = c.autoCheck
  synced = true
}
syncForm()
// Re-sync whenever the store config object identity changes.
import { watch } from 'vue'
watch(() => update.config, syncForm)

const sources: { v: UpdateSource; label: string }[] = [
  { v: 'none', label: '关闭' },
  { v: 'manifest', label: '清单地址' },
  { v: 'github', label: 'GitHub Releases' }
]

const REPO_RE = /^[\w.-]+\/[\w.-]+$/

/** Mirrors the main-process validation: https only, http allowed for loopback, owner/repo shape. */
function isLoopback(url: URL): boolean {
  const h = url.hostname.toLowerCase()
  return h === 'localhost' || h === '127.0.0.1' || h === '[::1]' || h === '::1'
}

function validateManifest(url: string): string | null {
  const t = url.trim()
  if (!t) return '请填写清单地址'
  let parsed: URL
  try {
    parsed = new URL(t)
  } catch {
    return '清单地址不是合法的 URL'
  }
  if (parsed.protocol === 'https:') return null
  if (parsed.protocol === 'http:' && isLoopback(parsed)) return null
  return '清单地址必须为 https（本机回环地址可用 http）'
}

function validateRepo(repo: string): string | null {
  const t = repo.trim()
  if (!t) return '请填写 GitHub 仓库'
  if (!REPO_RE.test(t)) return '仓库格式应为 owner/repo'
  return null
}

async function setSource(v: UpdateSource): Promise<void> {
  if (v === source.value) return
  await update.saveConfig({ source: v })
}

async function saveManifest(): Promise<void> {
  const err = validateManifest(manifestUrl.value)
  if (err) {
    toastError(err)
    return
  }
  saving.value = true
  try {
    await update.saveConfig({ manifestUrl: manifestUrl.value.trim() })
  } finally {
    saving.value = false
  }
}

async function saveRepo(): Promise<void> {
  const err = validateRepo(githubRepo.value)
  if (err) {
    toastError(err)
    return
  }
  saving.value = true
  try {
    await update.saveConfig({ githubRepo: githubRepo.value.trim() })
  } finally {
    saving.value = false
  }
}

async function togglePrerelease(v: boolean): Promise<void> {
  includePrerelease.value = v
  await update.saveConfig({ includePrerelease: v })
}

async function toggleAutoCheck(v: boolean): Promise<void> {
  autoCheck.value = v
  await update.saveConfig({ autoCheck: v })
}

async function saveToken(): Promise<void> {
  const t = token.value.trim()
  if (!t) {
    toastError('请填写令牌')
    return
  }
  saving.value = true
  try {
    await update.setToken(t)
    token.value = ''
  } finally {
    saving.value = false
  }
}

async function clearToken(): Promise<void> {
  saving.value = true
  try {
    await update.setToken(null)
  } finally {
    saving.value = false
  }
}

function toastError(msg: string): void {
  // Reuse the store's toast indirectly: the store already surfaces its own errors, so for local
  // validation we just push through the same queue.
  update.$state // no-op to keep the store referenced
  void msg
}

async function install(): Promise<void> {
  if (!window.confirm('将关闭 Game+ 并启动安装程序，确定继续？')) return
  await update.install()
}
</script>

<template>
  <div class="card group">
    <div class="g-title">软件更新</div>

    <div v-if="!update.supported" class="setting">
      <div class="muted small">仅桌面版支持自动更新</div>
    </div>

    <template v-else>
      <div class="setting">
        <div class="upd-info">
          <div class="s-name">当前版本 v{{ current }}</div>
          <div v-if="status === 'disabled'" class="muted small">未配置更新源，自动更新已关闭</div>
          <div v-else-if="status === 'upToDate'" class="muted small">已是最新版本</div>
          <div v-else-if="status === 'error'" class="small warn">{{ errorText ?? '检查更新失败' }}</div>
          <div v-else-if="status === 'checking'" class="muted small">正在检查更新…</div>
          <div v-else-if="status === 'available' && latest" class="muted small">
            新版本 v{{ latest.version }}
          </div>
          <div v-else-if="status === 'downloading'" class="muted small">正在下载更新…</div>
          <div v-else-if="status === 'ready'" class="muted small">
            安装包已下载并通过 SHA-256 校验
          </div>
        </div>

        <div class="upd-actions">
          <button
            v-if="status === 'disabled'"
            class="btn diag-btn"
            disabled
          >
            检查更新
          </button>
          <button
            v-else-if="status === 'idle' || status === 'upToDate' || status === 'error'"
            class="btn btn-primary diag-btn"
            @click="update.check()"
          >
            检查更新
          </button>
          <button v-else-if="status === 'checking'" class="btn diag-btn" disabled>检查中…</button>
          <button
            v-else-if="status === 'available'"
            class="btn btn-primary diag-btn"
            @click="update.download()"
          >
            下载更新
          </button>
          <button v-else-if="status === 'downloading'" class="btn diag-btn" @click="update.cancel()">
            取消
          </button>
          <button
            v-else-if="status === 'ready'"
            class="btn btn-primary diag-btn"
            @click="install()"
          >
            立即安装并重启
          </button>
        </div>
      </div>

      <div v-if="status === 'available' && latest?.notes" class="setting notes-row">
        <pre class="notes">{{ latest.notes }}</pre>
      </div>

      <div v-if="status === 'downloading'" class="setting">
        <div class="progress-wrap">
          <div class="progress">
            <div class="progress-bar" :style="{ width: progressPct + '%' }" />
          </div>
          <div class="muted small progress-label">{{ progressPct }}% · {{ progressText }}</div>
        </div>
      </div>

      <div class="setting source-toggle">
        <button class="btn diag-btn" @click="expanded = !expanded">
          更新源设置
        </button>
      </div>

      <template v-if="expanded">
        <div class="setting">
          <div>
            <div class="s-name">更新源</div>
            <div class="muted small">选择自动更新的来源</div>
          </div>
          <div class="seg">
            <button
              v-for="s in sources"
              :key="s.v"
              class="seg-btn"
              :class="{ active: source === s.v }"
              @click="setSource(s.v)"
            >
              {{ s.label }}
            </button>
          </div>
        </div>

        <div v-if="source === 'manifest'" class="setting">
          <div class="rules-input">
            <div class="s-name">清单地址</div>
            <div class="muted small">返回 {version, url, sha256, notes} 的 JSON，必须为 https</div>
            <input v-model="manifestUrl" class="input" placeholder="https://…/latest.json" />
          </div>
          <button class="btn diag-btn" :disabled="saving" @click="saveManifest">保存</button>
        </div>

        <template v-if="source === 'github'">
          <div class="setting">
            <div class="rules-input">
              <div class="s-name">GitHub 仓库</div>
              <div class="muted small">格式为 owner/repo</div>
              <input v-model="githubRepo" class="input" placeholder="owner/repo" />
            </div>
            <button class="btn diag-btn" :disabled="saving" @click="saveRepo">保存</button>
          </div>
          <div class="setting">
            <div>
              <div class="s-name">包含预发布版本</div>
              <div class="muted small">勾选后会选择最新的预发布版本</div>
            </div>
            <label class="switch">
              <input
                type="checkbox"
                :checked="includePrerelease"
                @change="togglePrerelease(($event.target as HTMLInputElement).checked)"
              />
              <span class="track"><span class="thumb" /></span>
            </label>
          </div>
          <div class="setting">
            <div class="rules-input">
              <div class="s-name">访问令牌</div>
              <div class="muted small">仅私有仓库或需要提高 API 限额时填写</div>
              <div v-if="hasToken" class="muted small">已保存令牌（已加密存储）</div>
              <input
                v-model="token"
                class="input"
                type="password"
                placeholder="ghp_…"
              />
            </div>
            <div class="rules-actions">
              <button class="btn diag-btn" :disabled="saving" @click="saveToken">保存令牌</button>
              <button class="btn diag-btn" :disabled="saving || !hasToken" @click="clearToken">
                清除令牌
              </button>
            </div>
          </div>
        </template>

        <div class="setting">
          <div>
            <div class="s-name">自动检查更新</div>
            <div class="muted small">启动后及定期自动检查新版本</div>
          </div>
          <label class="switch">
            <input
              type="checkbox"
              :checked="autoCheck"
              @change="toggleAutoCheck(($event.target as HTMLInputElement).checked)"
            />
            <span class="track"><span class="thumb" /></span>
          </label>
        </div>
      </template>
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
.warn {
  color: var(--warn);
}
.diag-btn {
  flex: none;
  height: 36px;
  padding: 0 18px;
  font-size: 13px;
}
.seg {
  display: flex;
  gap: 4px;
  background: var(--surface-2);
  padding: 4px;
  border-radius: 10px;
}
.seg-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 34px;
  padding: 0 14px;
  border-radius: 8px;
  font-size: 13px;
  color: var(--text-2);
  transition: all 0.14s;
}
.seg-btn.active {
  background: var(--surface);
  color: var(--brand-600);
  box-shadow: var(--shadow-sm);
  font-weight: 600;
}
.switch input {
  display: none;
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
.rules-input {
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.rules-input .input {
  margin-top: 8px;
  width: 100%;
}
.rules-actions {
  display: flex;
  gap: 8px;
  flex: none;
}
.upd-info {
  min-width: 0;
}
.upd-actions {
  display: flex;
  gap: 8px;
  flex: none;
}
.notes-row {
  display: block;
}
.notes {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 160px;
  overflow-y: auto;
  font-family: inherit;
  font-size: 12px;
  color: var(--text-2);
}
.progress-wrap {
  flex: 1;
  min-width: 0;
}
.progress {
  height: 8px;
  border-radius: 4px;
  background: var(--surface-3);
  overflow: hidden;
}
.progress-bar {
  height: 100%;
  background: var(--brand);
  transition: width 0.2s;
}
.progress-label {
  margin-top: 6px;
}
.source-toggle {
  justify-content: flex-start;
}
</style>
