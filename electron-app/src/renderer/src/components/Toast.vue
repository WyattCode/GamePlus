<script setup lang="ts">
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
</script>

<template>
  <div class="toast-host">
    <TransitionGroup name="toast">
      <div
        v-for="t in toast.items"
        :key="t.id"
        class="toast"
        :class="t.kind"
        @click="toast.dismiss(t.id)"
      >
        {{ t.message }}
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-host {
  position: fixed;
  right: 20px;
  bottom: 20px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  z-index: 500;
  pointer-events: none;
}
.toast {
  pointer-events: auto;
  max-width: 360px;
  padding: 12px 16px;
  border-radius: var(--radius-sm);
  font-size: 13px;
  color: #fff;
  background: var(--text-2);
  box-shadow: var(--shadow-md);
  cursor: pointer;
  word-break: break-word;
}
.toast.error {
  background: var(--danger);
}
.toast.info {
  background: var(--brand);
}
.toast-enter-active,
.toast-leave-active {
  transition: opacity 0.18s, transform 0.18s;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
