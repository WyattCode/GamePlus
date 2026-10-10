<script setup lang="ts">
import { onMounted } from 'vue'
import TitleBar from './components/TitleBar.vue'
import SideBar from './components/SideBar.vue'
import Toast from './components/Toast.vue'
import DiagnosticsDialog from './components/DiagnosticsDialog.vue'
import OnboardingDialog from './components/OnboardingDialog.vue'
import { useAppStore } from './stores/app'
import { useAutoStore } from './stores/auto'
import { useLibraryStore } from './stores/library'
import { useLogsStore } from './stores/logs'
import { useOnboardingStore } from './stores/onboarding'
import { useUpdateStore } from './stores/update'
import { useToastStore, errorMessage } from './stores/toast'
import { allowBackendConnection } from './api/backend'
import { getPrefs, setPrefs } from './api/prefs'
import { onLaunchRequest } from './utils/launchApi'
import { useAccelerateAndLaunch } from './composables/useAccelerateAndLaunch'

const app = useAppStore()
const auto = useAutoStore()
const library = useLibraryStore()
const logs = useLogsStore()
const onboarding = useOnboardingStore()
const update = useUpdateStore()
const toast = useToastStore()

const { handleLaunchRequest } = useAccelerateAndLaunch()

let started = false

// Starts the backend connection and loads app data. Runs exactly once, either immediately (when
// onboarding isn't shown) or when the user leaves onboarding step 0 / skips / finishes.
function startApp(): void {
  if (started) return
  started = true
  allowBackendConnection()
  logs.init()
  auto.start()
  update.init()
  library.load().catch((e) => toast.error('加载数据失败：' + errorMessage(e)))
  void showLoginItemMigrationToast()
}

// The old Electron login-item was migrated to a scheduled task by the main process. Tell the user
// once, then clear the flag so the toast doesn't reappear.
async function showLoginItemMigrationToast(): Promise<void> {
  try {
    const prefs = await getPrefs()
    if (!prefs.migratedLoginItem) return
    toast.info('开机启动已改为计划任务方式，请在设置中重新开启')
    await setPrefs({ migratedLoginItem: false })
  } catch {
    // ignore
  }
}

onMounted(async () => {
  // Desktop-shortcut launches (--launch <gameId>) can arrive before data is loaded; the composable
  // loads the library on demand, so subscribe right away.
  onLaunchRequest((gameId) => {
    void handleLaunchRequest(gameId)
  })
  app.init()
  // Read prefs first: if onboarding is shown we must NOT touch the backend yet, because the
  // backend start triggers the UAC prompt and the user hasn't read the explanation.
  await onboarding.load()
  onboarding.setOnLeaveStep0(startApp)
  if (onboarding.visible) return
  startApp()
})
</script>

<template>
  <div class="app-root">
    <TitleBar />
    <div class="app-body">
      <SideBar />
      <main class="content">
        <RouterView v-slot="{ Component }">
          <Transition name="fade" mode="out-in">
            <component :is="Component" />
          </Transition>
        </RouterView>
      </main>
    </div>
    <Toast />
    <DiagnosticsDialog />
    <OnboardingDialog />
  </div>
</template>

<style scoped>
.app-root {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.app-body {
  flex: 1;
  display: flex;
  min-height: 0;
}
.content {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
  overflow-x: hidden;
  background: var(--bg);
}
</style>
