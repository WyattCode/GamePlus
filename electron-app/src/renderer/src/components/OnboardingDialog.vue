<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppIcon from '@/components/AppIcon.vue'
import GameGlyph from '@/components/GameGlyph.vue'
import { useOnboardingStore } from '@/stores/onboarding'
import { useLibraryStore } from '@/stores/library'
import { useAccelStore } from '@/stores/accel'
import { useDiagnosticsStore } from '@/stores/diagnostics'
import { errorMessage } from '@/stores/toast'
import type { Game } from '@shared/types'

const onboarding = useOnboardingStore()
const library = useLibraryStore()
const accel = useAccelStore()
const diagnostics = useDiagnosticsStore()
const router = useRouter()

const steps = [
  { n: 1, label: '导入线路' },
  { n: 2, label: '选择游戏' },
  { n: 3, label: '开始加速' }
]

// --- Step 1: import lines ---
const subRemark = ref('')
const subLink = ref('')
const singleLink = ref('')
const singleMode = ref(false)
const importing = ref(false)
const importError = ref<string | null>(null)
const importResult = ref<string | null>(null)

async function importSubscription(): Promise<void> {
  if (!subLink.value.trim() || importing.value) return
  importing.value = true
  importError.value = null
  importResult.value = null
  try {
    const sub = await library.addSubscription(subRemark.value.trim(), subLink.value.trim())
    if (sub) await library.updateSubscription(sub.id)
    importResult.value = `已导入 ${library.servers.length} 条线路`
    subLink.value = ''
    subRemark.value = ''
  } catch (e) {
    importError.value = errorMessage(e)
  } finally {
    importing.value = false
  }
}

async function importSingle(): Promise<void> {
  if (!singleLink.value.trim() || importing.value) return
  importing.value = true
  importError.value = null
  importResult.value = null
  try {
    const n = await library.addServerFromLink(singleLink.value.trim())
    importResult.value = n > 0 ? `已导入 ${n} 条线路` : '未识别到可用线路'
    singleLink.value = ''
  } catch (e) {
    importError.value = errorMessage(e)
  } finally {
    importing.value = false
  }
}

// --- Step 2: pick a game ---
const gameQuery = ref('')

const gameList = computed<Game[]>(() => {
  const q = gameQuery.value.trim().toLowerCase()
  let list = library.sortedGames
  if (q) {
    list = list.filter(
      (g) =>
        g.name.toLowerCase().includes(q) ||
        (g.englishName ?? '').toLowerCase().includes(q) ||
        (g.aliases ?? []).some((a) => a.toLowerCase().includes(q))
    )
  }
  return list.slice(0, 12)
})

function pick(g: Game): void {
  accel.selectedGame = g
}

// --- Step 3: start ---
const starting = ref(false)
const startError = ref<string | null>(null)

async function start(): Promise<void> {
  if (!accel.selectedGame || starting.value) return
  starting.value = true
  startError.value = null
  try {
    await accel.start()
    await onboarding.finish()
    router.push('/accelerate')
  } catch (e) {
    startError.value = errorMessage(e)
  } finally {
    starting.value = false
  }
}

function openDiagnostics(): void {
  diagnostics.open(startError.value)
}
</script>

<template>
  <Teleport to="body">
    <div v-if="onboarding.visible" class="overlay">
      <div class="dialog">
        <div class="d-head">
          <h3>新手引导</h3>
          <button class="skip" @click="onboarding.finish()">跳过引导</button>
        </div>

        <div v-if="onboarding.step >= 1" class="steps">
          <div
            v-for="s in steps"
            :key="s.n"
            class="step"
            :class="{ active: onboarding.step === s.n, done: onboarding.step > s.n }"
          >
            <span class="dot">{{ s.n }}</span>
            <span class="label">{{ s.label }}</span>
          </div>
        </div>

        <div class="d-body">
          <!-- Step 0: welcome + admin explanation -->
          <template v-if="onboarding.step === 0">
            <div class="hero">
              <div class="hero-ico"><AppIcon name="rocket" :size="26" /></div>
              <div>
                <div class="hero-title">欢迎使用 Game+</div>
                <div class="muted small">三步完成设置，让游戏走加速线路</div>
              </div>
            </div>
            <div class="notice">
              <div class="notice-title">
                <AppIcon name="info" :size="16" /> 为什么需要管理员权限？
              </div>
              <p>
                游戏通常不走系统代理，Game+ 需要安装网络驱动（进程模式 / TUN
                虚拟网卡）才能把游戏流量送进加速线路；这需要 Windows 管理员权限。
              </p>
              <p>
                接下来系统会弹出"用户账户控制"窗口，请选择"是"。如果选择"否"，Game+
                仍可运行，但只能加速支持系统代理的程序（启动器、浏览器），大部分游戏无法加速。
              </p>
              <p>Game+ 不会读取你的个人文件，只修改网络相关设置，退出时自动恢复。</p>
            </div>
          </template>

          <!-- Step 1: import lines -->
          <template v-else-if="onboarding.step === 1">
            <div class="s-title">导入线路</div>
            <template v-if="library.servers.length > 0">
              <div class="ok-box">
                <AppIcon name="check" :size="18" /> 已有 {{ library.servers.length }} 条线路
              </div>
            </template>
            <template v-else>
              <div class="field">
                <AppIcon name="edit" :size="16" />
                <input v-model="subRemark" class="bare" placeholder="备注（可选）" />
              </div>
              <div class="field">
                <AppIcon name="cloud" :size="16" />
                <input v-model="subLink" class="bare" placeholder="https://…订阅链接" />
              </div>
              <button class="btn btn-primary full" :disabled="importing" @click="importSubscription">
                <AppIcon name="download" :size="18" /> {{ importing ? '导入中…' : '导入' }}
              </button>

              <button class="link-btn" @click="singleMode = !singleMode">
                {{ singleMode ? '使用订阅链接' : '我有单个分享链接' }}
              </button>

              <template v-if="singleMode">
                <div class="field">
                  <AppIcon name="list" :size="16" />
                  <input v-model="singleLink" class="bare" placeholder="ss:// / vmess:// / trojan:// …" />
                </div>
                <button class="btn full" :disabled="importing" @click="importSingle">
                  <AppIcon name="plus" :size="18" /> 导入单条线路
                </button>
              </template>

              <div v-if="importError" class="err">{{ importError }}</div>
              <div v-else-if="importResult" class="ok-text">{{ importResult }}</div>
            </template>
          </template>

          <!-- Step 2: pick a game -->
          <template v-else-if="onboarding.step === 2">
            <div class="s-title">选择游戏</div>
            <div class="field">
              <AppIcon name="search" :size="16" />
              <input v-model="gameQuery" class="bare" placeholder="搜索游戏" />
            </div>
            <div class="grid">
              <button
                v-for="g in gameList"
                :key="g.id"
                class="tile"
                :class="{ active: accel.selectedGame?.id === g.id }"
                @click="pick(g)"
              >
                <span class="tile-ico" :style="{ background: g.accent || 'var(--brand)' }">
                  <GameGlyph :id="g.id" :name="g.name" :size="22" />
                </span>
                <span class="tile-name">{{ g.name }}</span>
              </button>
            </div>
            <div v-if="!gameList.length" class="muted small">没有匹配的游戏</div>
          </template>

          <!-- Step 3: start -->
          <template v-else>
            <div class="s-title">开始加速</div>
            <div class="summary">
              <div class="sum-row">
                <span class="muted">游戏</span>
                <span>{{ accel.selectedGame?.name ?? '未选择' }}</span>
              </div>
              <div class="sum-row">
                <span class="muted">线路</span>
                <span>智能选路</span>
              </div>
            </div>
            <div v-if="startError" class="err">
              {{ startError }}
              <button class="link-btn inline" @click="openDiagnostics">诊断</button>
            </div>
          </template>
        </div>

        <div class="d-foot">
          <button v-if="onboarding.step > 0" class="btn" @click="onboarding.back()">上一步</button>
          <div class="spacer" />
          <template v-if="onboarding.step === 0">
            <button class="btn btn-primary" @click="onboarding.next()">我知道了，继续</button>
          </template>
          <template v-else-if="onboarding.step === 1">
            <button class="btn" @click="onboarding.next()">稍后再说</button>
            <button class="btn btn-primary" :disabled="library.servers.length === 0" @click="onboarding.next()">
              下一步
            </button>
          </template>
          <template v-else-if="onboarding.step === 2">
            <button class="btn btn-primary" :disabled="!accel.selectedGame" @click="onboarding.next()">
              下一步
            </button>
          </template>
          <template v-else>
            <button class="btn" @click="onboarding.finish()">完成，稍后加速</button>
            <button class="btn btn-primary" :disabled="starting || !accel.selectedGame" @click="start">
              {{ starting ? '启动中…' : '开始加速' }}
            </button>
          </template>
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
  align-items: center;
  justify-content: center;
  padding: 20px;
  z-index: 300;
}
.dialog {
  width: 560px;
  max-height: 86vh;
  display: flex;
  flex-direction: column;
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
.skip {
  font-size: 13px;
  color: var(--text-3);
}
.skip:hover {
  color: var(--brand);
}
.steps {
  display: flex;
  gap: 20px;
  padding: 14px 20px 0;
}
.step {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--text-3);
}
.step.active {
  color: var(--brand);
  font-weight: 600;
}
.step.done {
  color: var(--ok);
}
.dot {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--surface-2);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
}
.step.active .dot {
  background: var(--brand);
  color: #fff;
}
.step.done .dot {
  background: var(--ok);
  color: #fff;
}
.d-body {
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  overflow-y: auto;
}
.hero {
  display: flex;
  align-items: center;
  gap: 14px;
}
.hero-ico {
  width: 48px;
  height: 48px;
  border-radius: 14px;
  background: var(--brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.hero-title {
  font-size: 16px;
  font-weight: 700;
}
.notice {
  padding: 14px 16px;
  border-radius: var(--radius-sm);
  background: var(--brand-soft, rgba(59, 130, 246, 0.1));
  border: 1px solid var(--brand);
}
.notice-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 700;
  color: var(--brand);
  margin-bottom: 8px;
}
.notice p {
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.6;
  margin-bottom: 8px;
}
.notice p:last-child {
  margin-bottom: 0;
}
.s-title {
  font-size: 15px;
  font-weight: 600;
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
}
.bare {
  flex: 1;
  border: none;
  background: none;
  outline: none;
  color: var(--text);
  font-size: 14px;
}
.full {
  width: 100%;
  justify-content: center;
}
.link-btn {
  font-size: 13px;
  color: var(--brand);
  align-self: flex-start;
}
.link-btn.inline {
  margin-left: 8px;
  text-decoration: underline;
}
.ok-box {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 14px;
  border-radius: var(--radius-sm);
  background: rgba(16, 185, 129, 0.1);
  color: var(--ok);
  font-weight: 600;
}
.ok-text {
  font-size: 13px;
  color: var(--ok);
}
.err {
  font-size: 13px;
  color: var(--danger);
  word-break: break-word;
}
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
}
.tile {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--surface-2);
  text-align: left;
}
.tile.active {
  border-color: var(--brand);
  background: var(--brand-soft, rgba(59, 130, 246, 0.1));
}
.tile-ico {
  width: 34px;
  height: 34px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex: none;
}
.tile-name {
  font-size: 13px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.summary {
  padding: 14px 16px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.sum-row {
  display: flex;
  justify-content: space-between;
  font-size: 14px;
}
.d-foot {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
}
.spacer {
  flex: 1;
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
.btn-primary:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
