<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import type { Game } from '@shared/types'

const props = defineProps<{ open: boolean; game: Game | null }>()
const emit = defineEmits<{ close: []; save: [data: Partial<Game>] }>()

const CATS = ['PC', '主机', '平台', '热门'] as const
const PLATFORMS = ['Steam', 'Epic', 'Xbox', 'PlayStation', 'Switch', 'Riot', '暴雪', 'EA', 'R星', '其他'] as const

const form = reactive({
  name: '',
  englishName: '',
  category: 'PC',
  platforms: [] as string[],
  regions: [] as string[],
  proxyDomains: [] as string[],
  proxyIps: [] as string[],
  proxyProcesses: [] as string[]
})

const domainsText = ref('')
const ipsText = ref('')
const processesText = ref('')
const regionsText = ref('')

// Line-separated text helper: bind textarea ↔ array.
const splitLines = (s: string): string[] =>
  s
    .split(/[\n,，;；]+/)
    .map((x) => x.trim())
    .filter(Boolean)
const joinLines = (a?: string[]): string => (a || []).join('\n')

watch(
  () => props.open,
  (open) => {
    if (!open) return
    const g = props.game
    form.name = g?.name ?? ''
    form.englishName = g?.englishName ?? ''
    form.category = g?.category ?? 'PC'
    form.platforms = g?.platforms ? [...g.platforms] : []
    form.regions = g?.regions ? [...g.regions] : []
    form.proxyDomains = g?.proxyDomains ? [...g.proxyDomains] : []
    form.proxyIps = g?.proxyIps ? [...g.proxyIps] : []
    form.proxyProcesses = g?.proxyProcesses ? [...g.proxyProcesses] : []
    domainsText.value = joinLines(g?.proxyDomains)
    ipsText.value = joinLines(g?.proxyIps)
    processesText.value = joinLines(g?.proxyProcesses)
    regionsText.value = joinLines(g?.regions)
  }
)

function togglePlatform(p: string): void {
  const i = form.platforms.indexOf(p)
  if (i >= 0) form.platforms.splice(i, 1)
  else form.platforms.push(p)
}

function cancel(): void {
  emit('close')
}

function save(): void {
  const name = form.name.trim()
  if (!name) return
  emit('save', {
    name,
    englishName: form.englishName.trim(),
    category: form.category,
    platforms: [...form.platforms],
    regions: splitLines(regionsText.value),
    proxyDomains: splitLines(domainsText.value),
    proxyIps: splitLines(ipsText.value),
    proxyProcesses: splitLines(processesText.value)
  })
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" @click.self="cancel">
      <div class="dialog">
        <div class="d-head">
          <h3>{{ game ? '编辑游戏' : '添加游戏' }}</h3>
          <button class="x" @click="cancel"><span>✕</span></button>
        </div>

        <div class="d-body">
          <div class="field">
            <label>游戏名称 <em>*</em></label>
            <input v-model="form.name" placeholder="如：我的世界" @keyup.enter="save" />
          </div>

          <div class="field-row">
            <div class="field">
              <label>英文名（用于搜索）</label>
              <input v-model="form.englishName" placeholder="如：Minecraft" />
            </div>
            <div class="field">
              <label>分类</label>
              <select v-model="form.category">
                <option v-for="c in CATS" :key="c" :value="c">{{ c }}</option>
              </select>
            </div>
          </div>

          <div class="field">
            <label>平台</label>
            <div class="chips">
              <button
                v-for="p in PLATFORMS"
                :key="p"
                type="button"
                class="chip"
                :class="{ on: form.platforms.includes(p) }"
                @click="togglePlatform(p)"
              >
                {{ p }}
              </button>
            </div>
          </div>

          <div class="field">
            <label>加速区域（每行一个，如 亚服 / 美服）</label>
            <textarea v-model="regionsText" rows="2" placeholder="亚服&#10;美服" />
          </div>

          <div class="rules">
            <div class="r-title">加速规则</div>
            <div class="field">
              <label>域名（每行一个）</label>
              <textarea v-model="domainsText" rows="3" placeholder="play.example.com&#10;*.example.net" />
            </div>
            <div class="field">
              <label>IP / CIDR（每行一个）</label>
              <textarea v-model="ipsText" rows="3" placeholder="1.2.3.4&#10;10.0.0.0/8" />
            </div>
            <div class="field">
              <label>进程名（每行一个，进程捕获用）</label>
              <textarea v-model="processesText" rows="2" placeholder="game.exe" />
            </div>
          </div>
        </div>

        <div class="d-foot">
          <button class="btn ghost" @click="cancel">取消</button>
          <button class="btn primary" :disabled="!form.name.trim()" @click="save">保存</button>
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
  width: 520px;
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
  gap: 14px;
}
.field-row {
  display: flex;
  gap: 12px;
}
.field-row .field {
  flex: 1;
}
.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.field label {
  font-size: 12px;
  color: var(--text-3);
  font-weight: 600;
}
.field label em {
  color: var(--brand);
  font-style: normal;
}
.input-stub,
.field input,
.field select,
.field textarea {
  height: 38px;
  padding: 0 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--surface-2);
  color: var(--text);
  font-size: 14px;
  outline: none;
}
.field input:focus,
.field select:focus,
.field textarea:focus {
  border-color: var(--brand);
}
.field textarea {
  height: auto;
  padding: 8px 12px;
  resize: vertical;
  line-height: 1.5;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.chip {
  height: 30px;
  padding: 0 12px;
  border-radius: 15px;
  font-size: 13px;
  background: var(--surface-2);
  color: var(--text-2);
  border: 1px solid var(--border);
}
.chip.on {
  background: var(--brand-soft);
  color: var(--brand-600);
  border-color: transparent;
}
.rules {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 4px;
  border-top: 1px solid var(--border);
}
.r-title {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
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
}
.btn.ghost {
  background: var(--surface-2);
  color: var(--text-2);
}
.btn.primary {
  background: var(--brand);
  color: #fff;
}
.btn.primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
