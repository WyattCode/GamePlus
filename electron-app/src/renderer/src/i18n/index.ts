import { ref } from 'vue'
import { zhCN, type MessageKey } from './zh-CN'
import { en } from './en'

export type Locale = 'zh-CN' | 'en'

const STORAGE_KEY = 'gameplus.locale'

function readStoredLocale(): Locale {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === 'en' || raw === 'zh-CN') return raw
  } catch {
    // localStorage may be unavailable (SSR / restricted contexts)
  }
  return 'zh-CN'
}

export const locale = ref<Locale>(readStoredLocale())

function applyDocumentLang(l: Locale): void {
  if (typeof document !== 'undefined' && document.documentElement) {
    document.documentElement.lang = l
  }
}

applyDocumentLang(locale.value)

export function setLocale(l: Locale): void {
  locale.value = l
  try {
    localStorage.setItem(STORAGE_KEY, l)
  } catch {
    // ignore persistence failures
  }
  applyDocumentLang(l)
}

function interpolate(template: string, params?: Record<string, string | number>): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = params[name]
    return value === undefined ? match : String(value)
  })
}

export function t(key: MessageKey, params?: Record<string, string | number>): string {
  // Read locale.value so the function is reactive inside render/computed contexts.
  const current = locale.value
  let template: string | undefined
  if (current === 'en') {
    template = en[key]
  }
  if (template === undefined) {
    template = zhCN[key]
  }
  if (template === undefined) {
    return key
  }
  return interpolate(template, params)
}

export function useI18n(): {
  t: typeof t
  locale: typeof locale
  setLocale: typeof setLocale
} {
  return { t, locale, setLocale }
}

// The tray mini window is a separate page on the same origin; follow language changes live.
if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event: StorageEvent) => {
    if (event.key !== STORAGE_KEY) return
    const next: Locale = event.newValue === 'en' ? 'en' : 'zh-CN'
    if (next !== locale.value) {
      locale.value = next
      applyDocumentLang(next)
    }
  })
}
