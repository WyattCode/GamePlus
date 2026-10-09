<script setup lang="ts">
import { computed } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { useLogsStore } from '@/stores/logs'

const logs = useLogsStore()
const entries = computed(() => logs.entries)

function clear(): void {
  logs.clear()
}
function time(ts: string): string {
  return new Date(ts).toLocaleTimeString('zh-CN', { hour12: false })
}
</script>

<template>
  <div class="page">
    <div class="head">
      <div>
        <h1 class="page-title">日志</h1>
        <p class="page-sub">{{ entries.length }} 条记录</p>
      </div>
      <button class="btn" @click="clear"><AppIcon name="trash" :size="18" /> 清空</button>
    </div>

    <div class="card console">
      <div v-if="!entries.length" class="empty muted">暂无日志。开始加速后这里会显示运行记录。</div>
      <div v-for="(l, i) in entries" :key="i" class="line" :class="l.level">
        <span class="ts">{{ time(l.ts) }}</span>
        <span class="lv">{{ l.level.toUpperCase() }}</span>
        <span class="msg">{{ l.message }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page {
  padding: 26px 28px 40px;
  height: 100%;
  display: flex;
  flex-direction: column;
}
.head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 16px;
}
.console {
  flex: 1;
  overflow-y: auto;
  padding: 14px 16px;
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 12.5px;
  background: var(--surface);
}
.empty {
  padding: 40px;
  text-align: center;
  font-family: var(--font);
}
.line {
  display: flex;
  gap: 12px;
  padding: 3px 0;
  border-bottom: 1px solid var(--border);
}
.ts {
  color: var(--text-3);
  flex-shrink: 0;
}
.lv {
  flex-shrink: 0;
  width: 60px;
  font-weight: 700;
  color: var(--text-2);
}
.line.info .lv {
  color: var(--info);
}
.line.warning .lv {
  color: var(--warn);
}
.line.error .lv {
  color: var(--danger);
}
.msg {
  color: var(--text);
  word-break: break-all;
}
</style>
