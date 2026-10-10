<script setup lang="ts">
import { onMounted } from 'vue'
import TitleBar from './components/TitleBar.vue'
import SideBar from './components/SideBar.vue'
import Toast from './components/Toast.vue'
import DiagnosticsDialog from './components/DiagnosticsDialog.vue'
import OnboardingDialog from './components/OnboardingDialog.vue'
import { useAppStore } from './stores/app'
import { useLibraryStore } from './stores/library'
import { useLogsStore } from './stores/logs'
import { useOnboardingStore } from './stores/onboarding'
import { useToastStore, errorMessage } from './stores/toast'
import { allowBackendConnection } from './api/backend'

const app = useAppStore()
const library = useLibraryStore()
const logs = useLogsStore()
const onboarding = useOnboardingStore()
const toast = useToastStore()

let started = false

// Starts the backend connection and loads app data. Runs exactly once, either immediately (when
// onboarding isn't shown) or when the user leaves onboarding step 0 / skips / finishes.
function startApp(): void {
  if (started) return
  started = true
  allowBackendConnection()
  logs.init()
  library.load().catch((e) => toast.error('加载数据失败：' + errorMessage(e)))
}

onMounted(async () => {
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
