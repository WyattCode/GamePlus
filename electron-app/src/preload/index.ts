import { contextBridge, ipcRenderer } from 'electron'
import type { LoginItemState, TrayAction, TrayInfo, UiPrefs } from '../shared/types'

// Minimal, typed surface exposed to the renderer. Window chrome + theme now; the
// backend RPC channel to the C# sidecar will be added here as it comes online.
const api = {
  window: {
    minimize: (): void => ipcRenderer.send('win:minimize'),
    toggleMaximize: (): void => ipcRenderer.send('win:toggle-maximize'),
    close: (): void => ipcRenderer.send('win:close'),
    isMaximized: (): Promise<boolean> => ipcRenderer.invoke('win:is-maximized'),
    // Returns an unsubscribe function so callers can detach the listener (e.g. on component unmount).
    onMaximizeChange: (cb: (maximized: boolean) => void): (() => void) => {
      const listener = (_e: Electron.IpcRendererEvent, v: boolean): void => cb(v)
      ipcRenderer.on('win:maximized', listener)
      return () => ipcRenderer.removeListener('win:maximized', listener)
    }
  },
  theme: {
    getSystem: (): Promise<'light' | 'dark'> => ipcRenderer.invoke('theme:get-system'),
    onSystemChange: (cb: (theme: 'light' | 'dark') => void): void => {
      ipcRenderer.on('theme:system', (_e, v: 'light' | 'dark') => cb(v))
    }
  },
  backend: {
    getConnection: (): Promise<{ port: number; token: string }> =>
      ipcRenderer.invoke('backend:get-connection'),
    // Main lost the backend host (crash / elevated host died) — reconnect.
    onLost: (cb: () => void): void => {
      ipcRenderer.on('backend:lost', () => cb())
    }
  },
  prefs: {
    get: (): Promise<UiPrefs> => ipcRenderer.invoke('prefs:get'),
    set: (patch: Partial<UiPrefs>): Promise<UiPrefs> => ipcRenderer.invoke('prefs:set', patch)
  },
  app: {
    getLoginItem: (): Promise<LoginItemState> => ipcRenderer.invoke('app:get-login-item'),
    setLoginItem: (openAtLogin: boolean): Promise<LoginItemState> =>
      ipcRenderer.invoke('app:set-login-item', openAtLogin),
    notify: (title: string, body: string): Promise<void> =>
      ipcRenderer.invoke('app:notify', title, body)
  },
  tray: {
    update: (info: TrayInfo): Promise<void> => ipcRenderer.invoke('tray:update', info),
    // Returns an unsubscribe function so callers can detach the listener.
    onAction: (cb: (action: TrayAction) => void): (() => void) => {
      const listener = (_e: Electron.IpcRendererEvent, action: TrayAction): void => cb(action)
      ipcRenderer.on('tray:action', listener)
      return () => ipcRenderer.removeListener('tray:action', listener)
    }
  }
}

if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('gameplus', api)
  } catch (error) {
    console.error(error)
  }
} else {
  // @ts-ignore fallback when contextIsolation is disabled
  window.gameplus = api
}

export type GamePlusApi = typeof api
