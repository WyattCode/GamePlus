<script setup lang="ts">
import { useRoute } from 'vue-router'
import AppIcon from './AppIcon.vue'
import { useI18n } from '@/i18n'

const route = useRoute()
const { t } = useI18n()

const nav = [
  { to: '/accelerate', icon: 'rocket', label: 'nav.accelerate' },
  { to: '/games', icon: 'gamepad', label: 'nav.games' },
  { to: '/servers', icon: 'server', label: 'nav.servers' },
  { to: '/subscriptions', icon: 'cloud', label: 'nav.subscriptions' }
] as const
const footNav = [
  { to: '/logs', icon: 'list', label: 'nav.logs' },
  { to: '/settings', icon: 'settings', label: 'nav.settings' }
] as const

// RouterLink's router-link-active only matches the exact route (or a nested child), so sibling
// routes such as /settings/advanced would leave 设置 unhighlighted. Match by path prefix instead.
function isActive(to: string): boolean {
  return route.path === to || route.path.startsWith(to + '/')
}
</script>

<template>
  <aside class="sidebar">
    <nav class="nav">
      <RouterLink
        v-for="item in nav"
        :key="item.to"
        :to="item.to"
        class="nav-item"
        :class="{ active: isActive(item.to) }"
        draggable="false"
      >
        <span class="ind" />
        <AppIcon :name="item.icon" :size="20" />
        <span class="label">{{ t(item.label) }}</span>
      </RouterLink>
    </nav>

    <div class="spacer" />

    <nav class="nav">
      <RouterLink
        v-for="item in footNav"
        :key="item.to"
        :to="item.to"
        class="nav-item"
        :class="{ active: isActive(item.to) }"
        draggable="false"
      >
        <span class="ind" />
        <AppIcon :name="item.icon" :size="20" />
        <span class="label">{{ t(item.label) }}</span>
      </RouterLink>
    </nav>
  </aside>
</template>

<style scoped>
.sidebar {
  width: var(--sidebar-w);
  flex-shrink: 0;
  background: var(--sidebar);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  padding: 14px 12px;
}
.nav {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.spacer {
  flex: 1;
}
.nav-item {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  height: 44px;
  padding: 0 14px;
  border-radius: var(--radius-sm);
  color: var(--text-2);
  font-size: 14px;
  font-weight: 500;
  transition: background 0.14s, color 0.14s;
}
.nav-item:hover {
  background: var(--surface-2);
  color: var(--text);
}
.nav-item.router-link-active,
.nav-item.active {
  background: var(--brand-soft);
  color: var(--brand-600);
  font-weight: 600;
}
.ind {
  position: absolute;
  left: -12px;
  top: 50%;
  transform: translateY(-50%) scaleY(0);
  width: 3px;
  height: 22px;
  border-radius: 0 3px 3px 0;
  background: var(--brand);
  transition: transform 0.16s;
}
.nav-item.router-link-active .ind,
.nav-item.active .ind {
  transform: translateY(-50%) scaleY(1);
}
.label {
  flex: 1;
}
</style>
