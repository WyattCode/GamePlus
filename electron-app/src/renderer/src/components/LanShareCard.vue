<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToastStore, errorMessage } from '@/stores/toast'
import { backend } from '@/api/backend'
import type { EngineSettings, LanAddress, LanShareUpdate } from '@shared/types'

const toast = useToastStore()

const loaded = ref(false)
const saving = ref(false)

const settings = ref<EngineSettings | null>(null)
const addresses = ref<LanAddress[]>([])
const addressesLoading = ref(false)

const bindAddress = ref('')
const httpPort = ref(7899)
const socksPort = ref(0)
const allowedClients = ref('')
const username = ref('')
const password = ref('')
const clearPassword = ref(false)
const hasPassword = ref(false)

const LIMITS_TEXT =
  '仅通过 HTTP 代理转发 TCP 流量：可加速主机的商店、下载、登录和部分联机接口；游戏 UDP 对战流量、P2P、语音不会经过节点，NAT 类型也不会改善。'

const CONFIRM_TEXT = '开启后，允许列表中的局域网设备可以通过本机的节点上网。确定开启？'

const enabled = computed(() => settings.value?.lanShare.enabled ?? false)

const passwordPlaceholder = computed(() => (hasPassword.value ? '已设置（留空不修改）' : '可选'))

const instructions = computed(() => {
  const ls = settings.value?.lanShare
  if (!ls?.enabled) return null
  const auth = ls.username ? '，并填写用户名/密码' : ''
  return `在主机的网络设置中把代理服务器设为 ${ls.bindAddress}，端口 ${ls.httpPort}${auth}。仅在 Game+ 加速时生效。首次开启时 Windows 防火墙可能弹窗，请选择「专用网络」允许。`
})

onMounted(async () => {
  try {
    applySettings(await backend.getSettings())
  } catch {
    // backend not reachable (e.g. browser preview)
  }
  await refreshAddresses()
  loaded.value = true
})

function applySettings(s: EngineSettings): void {
  settings.value = s
  const ls = s.lanShare
  bindAddress.value = ls.bindAddress
  httpPort.value = ls.httpPort
  socksPort.value = ls.socksPort
  allowedClients.value = ls.allowedClients.join('\n')
  username.value = ls.username
  hasPassword.value = ls.hasPassword
  password.value = ''
  clearPassword.value = false
}

async function refreshAddresses(): Promise<void> {
  addressesLoading.value = true
  try {
    addresses.value = await backend.getLanAddresses()
    if (!bindAddress.value && addresses.value.length > 0) {
      bindAddress.value = addresses.value[0].address
    }
  } catch {
    // ignore — the select just stays empty
  } finally {
    addressesLoading.value = false
  }
}

function buildUpdate(): LanShareUpdate {
  const update: LanShareUpdate = {
    enabled: enabled.value,
    bindAddress: bindAddress.value.trim(),
    httpPort: Math.trunc(Number(httpPort.value)) || 0,
    socksPort: Math.trunc(Number(socksPort.value)) || 0,
    allowedClients: allowedClients.value
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0),
    username: username.value.trim()
  }
  if (password.value) update.password = password.value
  if (clearPassword.value) update.clearPassword = true
  return update
}

async function save(extra?: Partial<LanShareUpdate>): Promise<void> {
  if (saving.value) return
  saving.value = true
  try {
    const patch: LanShareUpdate = { ...buildUpdate(), ...extra }
    applySettings(await backend.setSettings({ lanShare: patch }))
    toast.info('已保存')
  } catch (e) {
    toast.error('保存失败：' + errorMessage(e))
  } finally {
    saving.value = false
  }
}

async function toggleEnabled(on: boolean): Promise<void> {
  if (on && !window.confirm(CONFIRM_TEXT)) return
  await save({ enabled: on })
}
</script>

<template>
  <div class="card group">
    <div class="g-title">局域网共享（主机加速）</div>

    <div class="setting">
      <div class="lan-info">
        <div class="s-name">开启局域网共享</div>
        <div class="muted small">
          允许 Switch / PlayStation / Xbox 等主机通过本机的 HTTP 代理走当前节点（默认关闭）
        </div>
      </div>
      <label class="switch" :class="{ disabled: saving || !loaded }">
        <input
          type="checkbox"
          :checked="enabled"
          :disabled="saving || !loaded"
          @change="toggleEnabled(($event.target as HTMLInputElement).checked)"
        />
        <span class="track"><span class="thumb" /></span>
      </label>
    </div>

    <div class="limits-box">{{ LIMITS_TEXT }}</div>

    <div class="setting">
      <div class="lan-info">
        <div class="s-name">共享地址</div>
        <div class="muted small">本机局域网 IPv4，主机的代理服务器将指向此地址</div>
      </div>
      <div class="addr-row">
        <select v-model="bindAddress" class="input addr-select" :disabled="saving || !loaded">
          <option v-if="addresses.length === 0" value="">未找到局域网地址</option>
          <option
            v-for="a in addresses"
            :key="a.address"
            :value="a.address"
          >
            {{ a.address }}（{{ a.interfaceName }}）
          </option>
          <option v-if="bindAddress && !addresses.some((a) => a.address === bindAddress)" :value="bindAddress">
            {{ bindAddress }}
          </option>
        </select>
        <button class="btn diag-btn" :disabled="addressesLoading" @click="refreshAddresses">
          {{ addressesLoading ? '刷新中…' : '刷新' }}
        </button>
      </div>
    </div>

    <div class="setting">
      <div class="lan-info">
        <div class="s-name">HTTP 端口</div>
        <div class="muted small">主机代理设置中填写的端口（1024–65535）</div>
      </div>
      <input v-model.number="httpPort" class="input port-input" type="number" min="1024" max="65535" :disabled="saving || !loaded" />
    </div>

    <div class="setting">
      <div class="lan-info">
        <div class="s-name">SOCKS5 端口</div>
        <div class="muted small">可选，供电脑/手机使用；0 = 关闭</div>
      </div>
      <input v-model.number="socksPort" class="input port-input" type="number" min="0" max="65535" :disabled="saving || !loaded" />
    </div>

    <div class="setting block">
      <div class="lan-info">
        <div class="s-name">允许的设备</div>
        <div class="muted small">每行一个局域网 IP 或网段（如 192.168.1.0/24），必填，最多 32 条；列表外的设备无法使用</div>
      </div>
      <textarea
        v-model="allowedClients"
        class="input clients-input"
        rows="4"
        placeholder="192.168.1.50&#10;192.168.1.0/24"
        :disabled="saving || !loaded"
      />
    </div>

    <div class="setting">
      <div class="lan-info">
        <div class="s-name">用户名 / 密码</div>
        <div class="muted small">可选；设置后主机代理也需填写相同的用户名和密码</div>
      </div>
      <div class="auth-row">
        <input v-model="username" class="input auth-input" type="text" placeholder="用户名（可选）" :disabled="saving || !loaded" />
        <input v-model="password" class="input auth-input" type="password" :placeholder="passwordPlaceholder" :disabled="saving || !loaded" />
        <button v-if="hasPassword" class="link-btn" :disabled="saving" @click="clearPassword = true">
          清除密码
        </button>
      </div>
    </div>

    <div class="setting">
      <div class="muted small">修改端口、允许列表或密码后需点击保存；开关切换时会一并保存当前填写的内容。</div>
      <button class="btn btn-primary diag-btn" :disabled="saving || !loaded" @click="save()">
        {{ saving ? '保存中…' : '保存设置' }}
      </button>
    </div>

    <div v-if="instructions" class="setup-box">{{ instructions }}</div>
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
.setting.block {
  display: block;
}
.setting.block .lan-info {
  margin-bottom: 8px;
}
.s-name {
  font-weight: 600;
  font-size: 14px;
}
.small {
  font-size: 12px;
  margin-top: 2px;
}
.lan-info {
  flex: 1;
  min-width: 0;
  margin-right: 14px;
}
.limits-box {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius, 10px);
  background: rgba(255, 193, 7, 0.12);
  color: var(--warn);
  font-size: 12px;
  line-height: 1.6;
}
.setup-box {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--radius, 10px);
  background: var(--surface-2);
  color: var(--text-2);
  font-size: 12px;
  line-height: 1.6;
  word-break: break-word;
}
.addr-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: none;
}
.addr-select {
  min-width: 220px;
}
.port-input {
  width: 110px;
  flex: none;
}
.clients-input {
  width: 100%;
  resize: vertical;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
}
.auth-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: none;
}
.auth-input {
  width: 150px;
}
.link-btn {
  font-size: 12px;
  color: var(--brand);
  cursor: pointer;
  white-space: nowrap;
}
.link-btn:hover {
  text-decoration: underline;
}
.diag-btn {
  flex: none;
  height: 36px;
  padding: 0 18px;
  font-size: 13px;
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
