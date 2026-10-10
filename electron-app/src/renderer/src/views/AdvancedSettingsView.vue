<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import { backend } from '@/api/backend'
import { errorMessage, useToastStore } from '@/stores/toast'
import { validateDns, validateLocalPort, validateRegionPenalty } from '@/utils/advanced'
import type { AdvancedSettings, CaptureMode, EngineSettings } from '@shared/types'

const router = useRouter()
const toast = useToastStore()

const settings = ref<EngineSettings | null>(null)
const saving = ref(false)

// Local editable copies (kept in sync with the backend response after every save).
const dns = ref('')
const socksPort = ref(0)
const measureEffect = ref(false)
const regionPenalty = ref(60)
const defaultCaptureMode = ref<CaptureMode>('auto')

const loaded = ref(false)

onMounted(async () => {
  try {
    const s = await backend.getSettings()
    apply(s)
  } catch {
    // backend not reachable (e.g. browser preview)
  } finally {
    loaded.value = true
  }
})

function apply(s: EngineSettings): void {
  settings.value = s
  dns.value = s.advanced.tunDns
  socksPort.value = s.advanced.localSocksPort
  measureEffect.value = s.advanced.measureEffect
  regionPenalty.value = s.advanced.regionPenaltyMs
  defaultCaptureMode.value = s.advanced.defaultCaptureMode
}

const dnsError = computed(() => (dns.value.trim() ? validateDns(dns.value) : null))
const dnsDirty = computed(() => !!settings.value && dns.value.trim() !== settings.value.advanced.tunDns)
const dnsCanSave = computed(() => !dnsError.value && dnsDirty.value && !saving.value)

const portError = computed(() => validateLocalPort(socksPort.value))
const portDirty = computed(
  () => !!settings.value && socksPort.value !== settings.value.advanced.localSocksPort
)

// Sends only the changed advanced field so a stale/invalid sibling value can't block the save.
async function saveAdvanced(patch: Partial<AdvancedSettings>): Promise<void> {
  saving.value = true
  try {
    const s = await backend.setSettings({ advanced: patch })
    apply(s)
    toast.info('已保存')
  } catch (e) {
    toast.error(errorMessage(e))
    // Reload the authoritative values so the UI doesn't keep an invalid edit.
    try {
      apply(await backend.getSettings())
    } catch {
      // ignore
    }
  } finally {
    saving.value = false
  }
}

function saveDns(): void {
  if (!dnsCanSave.value) return
  void saveAdvanced({ tunDns: dns.value.trim() })
}

function savePort(): void {
  if (portError.value || !portDirty.value) return
  void saveAdvanced({ localSocksPort: socksPort.value })
}

function onPortInput(e: Event): void {
  const raw = (e.target as HTMLInputElement).value
  const n = raw === '' ? 0 : Number(raw)
  socksPort.value = Number.isFinite(n) ? Math.trunc(n) : 0
}

function toggleMeasure(v: boolean): void {
  measureEffect.value = v
  void saveAdvanced({ measureEffect: v })
}

function onPenaltyInput(e: Event): void {
  const n = Number((e.target as HTMLInputElement).value)
  regionPenalty.value = Number.isFinite(n) ? n : 0
}

function onPenaltyChange(): void {
  if (validateRegionPenalty(regionPenalty.value)) return
  void saveAdvanced({ regionPenaltyMs: regionPenalty.value })
}

function onCaptureModeChange(e: Event): void {
  const v = (e.target as HTMLSelectElement).value as CaptureMode
  defaultCaptureMode.value = v
  void saveAdvanced({ defaultCaptureMode: v })
}

async function resetDefaults(): Promise<void> {
  if (!window.confirm('确定将高级设置恢复为默认值？')) return
  saving.value = true
  try {
    const s = await backend.resetAdvancedSettings()
    apply(s)
    toast.info('已保存')
  } catch (e) {
    toast.error(errorMessage(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page">
    <div class="head">
      <button class="back" @click="router.push('/settings')">
        <AppIcon name="chevron" :size="16" class="back-ico" />
        返回设置
      </button>
      <h1 class="page-title">高级设置</h1>
    </div>

    <div class="muted small note">修改将在下次开始加速时生效。</div>

    <div class="card group">
      <div class="g-title">网络</div>

      <div class="setting">
        <div class="field">
          <div class="s-name">TUN 模式 DNS</div>
          <div class="muted small">
            仅 TUN 模式使用。填写 IP 地址，可带端口，例如 223.5.5.5 或 [2606:4700:4700::1111]:53
          </div>
          <input
            v-model="dns"
            class="input mono"
            placeholder="1.1.1.1"
            spellcheck="false"
            @keyup.enter="saveDns"
          />
          <div v-if="dnsError" class="small danger">{{ dnsError }}</div>
        </div>
        <button class="btn diag-btn" :disabled="!dnsCanSave" @click="saveDns">保存</button>
      </div>

      <div class="setting">
        <div class="field">
          <div class="s-name">本地 SOCKS5 端口</div>
          <div class="muted small">
            0 = 自动选择空闲端口。固定端口后，可让其它程序手动使用 127.0.0.1:端口 走加速线路（仅加速期间可用）
          </div>
          <input
            class="input port"
            type="number"
            min="0"
            max="65535"
            step="1"
            :value="socksPort"
            @input="onPortInput"
            @blur="savePort"
            @keyup.enter="savePort"
          />
          <div v-if="portError" class="small danger">{{ portError }}</div>
        </div>
      </div>

      <div class="setting">
        <div class="field">
          <div class="s-name">默认加速模式</div>
          <div class="muted small">
            自动：进程 → TUN → 系统代理 依次尝试；手动指定后失败不会自动降级。单个游戏可在加速页单独设置。
          </div>
          <select
            class="input select"
            :value="defaultCaptureMode"
            :disabled="!loaded || saving"
            @change="onCaptureModeChange"
          >
            <option value="auto">自动（推荐）</option>
            <option value="process">进程模式</option>
            <option value="tun">TUN 模式</option>
            <option value="proxy">系统代理</option>
          </select>
        </div>
      </div>
    </div>

    <div class="card group">
      <div class="g-title">选路与测量</div>

      <div class="setting">
        <div>
          <div class="s-name">每次加速前测量直连基线</div>
          <div class="muted small">
            默认仅在每个游戏首次加速时测量一次（约 3 秒），之后复用该结果；开启后每次加速前都会重新测量。
          </div>
        </div>
        <label class="switch" :class="{ disabled: !loaded || saving }">
          <input
            type="checkbox"
            :checked="measureEffect"
            :disabled="!loaded || saving"
            @change="toggleMeasure(($event.target as HTMLInputElement).checked)"
          />
          <span class="track"><span class="thumb" /></span>
        </label>
      </div>

      <div class="setting">
        <div class="field">
          <div class="s-name">地区偏好惩罚</div>
          <div class="muted small">
            智能选路时，非游戏所在地区的线路会被加上该延迟惩罚。0 = 只看延迟。
          </div>
          <div class="slider-row">
            <input
              class="slider"
              type="range"
              min="0"
              max="300"
              step="10"
              :value="regionPenalty"
              @input="onPenaltyInput"
              @change="onPenaltyChange"
            />
            <span class="slider-val">{{ regionPenalty }} ms</span>
          </div>
        </div>
      </div>
    </div>

    <div class="footer">
      <button class="btn diag-btn" :disabled="saving" @click="resetDefaults">恢复默认</button>
    </div>
  </div>
</template>

<style scoped>
.page {
  max-width: 760px;
  margin: 0 auto;
  padding: 26px 28px 40px;
}
.head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 6px;
}
.back {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 10px 0 6px;
  border-radius: var(--radius-sm);
  color: var(--text-2);
  font-size: 13px;
  cursor: pointer;
}
.back:hover {
  background: var(--surface-2);
  color: var(--text);
}
.back-ico {
  transform: rotate(90deg);
}
.page-title {
  margin: 0;
}
.note {
  margin-bottom: 16px;
}
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
  gap: 14px;
  padding: 16px 0;
  border-top: 1px solid var(--border);
}
.field {
  flex: 1;
  min-width: 0;
}
.s-name {
  font-weight: 600;
  font-size: 14px;
}
.small {
  font-size: 12px;
  margin-top: 2px;
}
.danger {
  color: var(--danger);
}
.diag-btn {
  flex: none;
  height: 36px;
  padding: 0 18px;
  font-size: 13px;
}
.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.input {
  margin-top: 8px;
  width: 100%;
}
.select {
  cursor: pointer;
}
.port {
  max-width: 160px;
}
.slider-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 10px;
}
.slider {
  flex: 1;
  accent-color: var(--brand);
}
.slider-val {
  flex: none;
  min-width: 64px;
  text-align: right;
  font-size: 13px;
  font-weight: 600;
}
.footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 4px;
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
