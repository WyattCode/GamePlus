<script setup lang="ts">
import { onMounted } from 'vue'
import TitleBar from './components/TitleBar.vue'
import SideBar from './components/SideBar.vue'
import { useAppStore } from './stores/app'
import { useLibraryStore } from './stores/library'

const app = useAppStore()
const library = useLibraryStore()

onMounted(() => {
  app.init()
  library.load()
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
