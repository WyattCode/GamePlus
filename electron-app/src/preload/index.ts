import { contextBridge, ipcRenderer } from 'electron'
import type {
  CrashFileInfo,
  TelemetryConfig,
  TelemetryEvent,
  TrayAction,
  TrayInfo,
  UiPrefs,
  UpdateConfig,
  UpdateConfigView,
  UpdateState
} from '../shared/types'

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
  },
  mini: {
    // Returns an unsubscribe function so callers can detach the listener.
    onInfo: (cb: (info: TrayInfo) => void): (() => void) => {
      const listener = (_e: Electron.IpcRendererEvent, info: TrayInfo): void => cb(info)
      ipcRenderer.on('mini:info', listener)
      return () => ipcRenderer.removeListener('mini:info', listener)
    },
    action: (a: TrayAction): Promise<void> => ipcRenderer.invoke('mini:action', a),
    openMain: (): Promise<void> => ipcRenderer.invoke('mini:open-main'),
    hide: (): Promise<void> => ipcRenderer.invoke('mini:hide')
  },
  update: {
    getState: (): Promise<UpdateState | null> => ipcRenderer.invoke('update:get-state'),
    check: (): Promise<UpdateState | null> => ipcRenderer.invoke('update:check'),
    download: (): Promise<UpdateState | null> => ipcRenderer.invoke('update:download'),
    cancel: (): Promise<void> => ipcRenderer.invoke('update:cancel'),
    install: (): Promise<UpdateState | null> => ipcRenderer.invoke('update:install'),
    getConfig: (): Promise<UpdateConfigView | null> => ipcRenderer.invoke('update:get-config'),
    setConfig: (patch: Partial<UpdateConfig>): Promise<UpdateConfigView | null> =>
      ipcRenderer.invoke('update:set-config', patch),
    setToken: (token: string | null): Promise<boolean> => ipcRenderer.invoke('update:set-token', token),
    // Returns an unsubscribe function so callers can detach the listener.
    onState: (cb: (state: UpdateState) => void): (() => void) => {
      const listener = (_e: Electron.IpcRendererEvent, state: UpdateState): void => cb(state)
      ipcRenderer.on('update:state', listener)
      return () => ipcRenderer.removeListener('update:state', listener)
    }
  },
  telemetry: {
    getConfig: (): Promise<TelemetryConfig> => ipcRenderer.invoke('telemetry:get-config'),
    setConfig: (patch: Partial<TelemetryConfig>): Promise<TelemetryConfig> =>
      ipcRenderer.invoke('telemetry:set-config', patch),
    record: (event: TelemetryEvent): Promise<void> => ipcRenderer.invoke('telemetry:record', event),
    getRecent: (limit: number): Promise<TelemetryEvent[]> =>
      ipcRenderer.invoke('telemetry:get-recent', limit),
    clear: (): Promise<void> => ipcRenderer.invoke('telemetry:clear'),
    listCrashes: (): Promise<CrashFileInfo[]> => ipcRenderer.invoke('telemetry:list-crashes'),
    openCrashFolder: (): Promise<void> => ipcRenderer.invoke('telemetry:open-crash-folder')
  },
  diag: {
    collectClient: (): Promise<unknown> => ipcRenderer.invoke('diag:collect-client'),
    saveBundle: (fileName: string, base64: string): Promise<string | null> =>
      ipcRenderer.invoke('diag:save-bundle', fileName, base64)
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
