import { contextBridge, ipcRenderer } from 'electron'

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
