import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

type Theme = 'light' | 'dark' | 'system'

const THEME_KEY = 'gameplus.theme'

export const useAppStore = defineStore('app', () => {
  const theme = ref<Theme>((localStorage.getItem(THEME_KEY) as Theme) || 'light')
  const systemDark = ref(false)

  function effective(): 'light' | 'dark' {
    if (theme.value === 'system') return systemDark.value ? 'dark' : 'light'
    return theme.value
  }

  function apply(): void {
    document.documentElement.dataset.theme = effective()
  }

  function setTheme(t: Theme): void {
    theme.value = t
    localStorage.setItem(THEME_KEY, t)
    apply()
  }

  async function init(): Promise<void> {
    try {
      systemDark.value = (await window.gameplus.theme.getSystem()) === 'dark'
      window.gameplus.theme.onSystemChange((v) => {
        systemDark.value = v === 'dark'
        apply()
      })
    } catch {
      // running outside Electron (e.g. plain vite) — ignore
    }
    apply()
  }

  watch(theme, apply)

  return { theme, systemDark, effective, setTheme, init }
})
