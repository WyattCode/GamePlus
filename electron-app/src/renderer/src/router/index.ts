import { createRouter, createWebHashHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/accelerate' },
    {
      path: '/accelerate',
      name: 'accelerate',
      component: () => import('@/views/AccelerateView.vue')
    },
    { path: '/games', name: 'games', component: () => import('@/views/GamesView.vue') },
    { path: '/servers', name: 'servers', component: () => import('@/views/ServersView.vue') },
    {
      path: '/subscriptions',
      name: 'subscriptions',
      component: () => import('@/views/SubscriptionsView.vue')
    },
    { path: '/logs', name: 'logs', component: () => import('@/views/LogsView.vue') },
    { path: '/settings', name: 'settings', component: () => import('@/views/SettingsView.vue') },
    {
      path: '/settings/advanced',
      name: 'advanced',
      component: () => import('@/views/AdvancedSettingsView.vue'),
      meta: { section: 'settings' }
    }
  ]
})
