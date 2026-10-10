import { app, shell, BrowserWindow, ipcMain, nativeTheme, Tray, Menu, nativeImage, Notification } from 'electron'
import { readFileSync } from 'fs'
import { join } from 'path'
import { electronApp, optimizer, is } from '@electron-toolkit/utils'
import { startSidecar, stopSidecar } from './sidecar'
import { getPrefs, setPrefs } from './prefs'
import { trayIcon32 } from './trayIcon'
import type { TrayInfo } from '../shared/types'

let mainWindow: BrowserWindow | null = null
let tray: Tray | null = null
let isQuitting = false

/** Honour the backend's MinimizeToTray setting (default on) so the close button hides to tray. */
function minimizeToTrayEnabled(): boolean {
  try {
    const p = join(app.getPath('appData'), 'Game+', 'settings.json')
    return JSON.parse(readFileSync(p, 'utf8')).MinimizeToTray !== false
  } catch {
    return true
  }
}

function showMainWindow(): void {
  if (!mainWindow || mainWindow.isDestroyed()) {
    createWindow()
    return
  }
  if (mainWindow.isMinimized()) mainWindow.restore()
  mainWindow.show()
  mainWindow.focus()
}

/** Sends a tray quick action to the renderer (which owns the accel/diagnostics stores). */
function sendTrayAction(action: 'toggle' | 'diagnose'): void {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('tray:action', action)
  }
}

/** Human-readable status line shared by the tray menu and tooltip. */
function trayStatusLine(info: TrayInfo | null): string {
  if (!info) return '未加速'
  if (info.state === 'connecting') return '连接中…'
  if (info.state === 'accelerating' || info.state === 'stopping') {
    const name = info.gameName ?? '游戏'
    const latency = info.latency > 0 ? ` · ${info.latency} ms` : ''
    return `加速中：${name}${latency}`
  }
  return '未加速'
}

/** Rebuilds the tray context menu from the latest renderer status snapshot. */
function updateTray(info: TrayInfo | null): void {
  if (!tray) return
  const status = trayStatusLine(info)
  tray.setToolTip(`Game+ 游戏加速器 · ${status}`)

  const active = info?.state === 'accelerating' || info?.state === 'connecting' || info?.state === 'stopping'
  const hasGame = !!info?.gameName
  const toggleLabel = active
    ? '停止加速'
    : hasGame
      ? `开始加速 ${info!.gameName}`
      : '开始加速（请先选择游戏）'

  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: status, enabled: false },
      { type: 'separator' },
      {
        label: toggleLabel,
        enabled: active || hasGame,
        click: () => sendTrayAction('toggle')
      },
      {
        label: '网络诊断',
        click: () => {
          showMainWindow()
          sendTrayAction('diagnose')
        }
      },
      { type: 'separator' },
      { label: '显示 Game+', click: showMainWindow },
      {
        label: '退出',
        click: () => {
          isQuitting = true
          app.quit()
        }
      }
    ])
  )
}

function createTray(): void {
  if (tray) return
  tray = new Tray(nativeImage.createFromDataURL(trayIcon32))
  updateTray(null)
  tray.on('click', showMainWindow)
  tray.on('double-click', showMainWindow)
}

/** Open an http(s) URL externally; ignore anything else (file:, javascript:, custom schemes). */
function openExternalIfAllowed(url: string): void {
  try {
    const parsed = new URL(url)
    if (parsed.protocol === 'https:' || parsed.protocol === 'http:') {
      shell.openExternal(url)
    }
  } catch {
    // not a valid URL — ignore
  }
}

function createWindow(): void {
  const win = new BrowserWindow({
    width: 1120,
    height: 720,
    minWidth: 940,
    minHeight: 600,
    show: false,
    frame: false, // Thunder-style custom chrome; titlebar is drawn in the renderer.
    backgroundColor: '#f5f6f8',
    autoHideMenuBar: true,
    // Dev taskbar icon; the packaged build uses the exe's embedded icon.
    ...(is.dev ? { icon: join(app.getAppPath(), 'build', 'icon.png') } : {}),
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: true,
      contextIsolation: true
    }
  })
  mainWindow = win

  // When launched at login with "start hidden" enabled, keep the window in the tray only.
  const startHidden = process.argv.includes('--hidden') && getPrefs().startHidden
  win.on('ready-to-show', () => {
    if (!startHidden) win.show()
  })

  // Close button hides to the tray (unless the user turned MinimizeToTray off, or is really quitting).
  win.on('close', (e) => {
    if (!isQuitting && minimizeToTrayEnabled()) {
      e.preventDefault()
      win.hide()
    }
  })
  win.on('closed', () => {
    if (mainWindow === win) mainWindow = null
  })

  win.webContents.setWindowOpenHandler((details) => {
    openExternalIfAllowed(details.url)
    return { action: 'deny' }
  })

  // Block in-app navigation away from our own renderer; open allowed http(s) links externally instead.
  win.webContents.on('will-navigate', (e, url) => {
    const current = win.webContents.getURL()
    if (url === current) return
    // Allow navigation within the dev server / packaged renderer origin.
    try {
      const target = new URL(url)
      const base = new URL(current)
      if (target.origin === base.origin && target.protocol !== 'file:') return
      if (target.protocol === 'file:' && base.protocol === 'file:') return
    } catch {
      // fall through to deny
    }
    e.preventDefault()
    openExternalIfAllowed(url)
  })

  // Reflect the OS theme so the renderer can honour "system". Remove the listener when the window closes
  // so re-creating the window doesn't accumulate dead nativeTheme listeners.
  const pushTheme = (): void =>
    win.webContents.send('theme:system', nativeTheme.shouldUseDarkColors ? 'dark' : 'light')
  nativeTheme.on('updated', pushTheme)
  win.on('closed', () => nativeTheme.off('updated', pushTheme))

  // Notify this window's renderer when maximize state changes so the button glyph can update.
  // Attached per-window (not via app 'browser-window-created') so the first/main window is covered too.
  win.on('maximize', () => win.webContents.send('win:maximized', true))
  win.on('unmaximize', () => win.webContents.send('win:maximized', false))

  if (is.dev && process.env['ELECTRON_RENDERER_URL']) {
    win.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

// --- Window control IPC (custom titlebar buttons) ---
function registerWindowControls(): void {
  ipcMain.on('win:minimize', () => mainWindow?.minimize())
  ipcMain.on('win:toggle-maximize', () => {
    if (!mainWindow) return
    mainWindow.isMaximized() ? mainWindow.unmaximize() : mainWindow.maximize()
  })
  ipcMain.on('win:close', () => mainWindow?.close())
  ipcMain.handle('win:is-maximized', () => mainWindow?.isMaximized() ?? false)
  ipcMain.handle('theme:get-system', () =>
    nativeTheme.shouldUseDarkColors ? 'dark' : 'light'
  )
  // Renderer asks for the backend WebSocket coordinates; resolves once the sidecar is up.
  ipcMain.handle('backend:get-connection', () => startSidecar())
  // Renderer-only UI prefs (onboarding etc.).
  ipcMain.handle('prefs:get', () => getPrefs())
  ipcMain.handle('prefs:set', (_e, patch) => setPrefs(patch ?? {}))

  // --- Login item (auto-start at login) ---
  ipcMain.handle('app:get-login-item', () => {
    if (!app.isPackaged) return { openAtLogin: false, unsupported: true }
    try {
      return { openAtLogin: app.getLoginItemSettings().openAtLogin }
    } catch {
      return { openAtLogin: false, unsupported: true }
    }
  })
  ipcMain.handle('app:set-login-item', (_e, openAtLogin: unknown) => {
    if (typeof openAtLogin !== 'boolean') return { openAtLogin: false, unsupported: true }
    if (!app.isPackaged) return { openAtLogin: false, unsupported: true }
    try {
      app.setLoginItemSettings({ openAtLogin, args: ['--hidden'] })
      return { openAtLogin: app.getLoginItemSettings().openAtLogin }
    } catch {
      return { openAtLogin: false, unsupported: true }
    }
  })

  // --- Desktop notifications (only when the window isn't visible/focused) ---
  ipcMain.handle('app:notify', (_e, title: unknown, body: unknown) => {
    if (typeof title !== 'string' || typeof body !== 'string') return
    if (title.length === 0 || title.length > 200 || body.length > 500) return
    if (!Notification.isSupported()) return
    const win = mainWindow
    const visible = !!win && !win.isDestroyed() && win.isVisible() && win.isFocused()
    if (visible) return
    new Notification({ title, body }).show()
  })

  // --- Tray status sync from the renderer ---
  ipcMain.handle('tray:update', (_e, info: unknown) => {
    if (!info || typeof info !== 'object') return
    const i = info as Record<string, unknown>
    const state = i.state
    if (state !== 'idle' && state !== 'connecting' && state !== 'accelerating' && state !== 'stopping') return
    const gameName = typeof i.gameName === 'string' && i.gameName.length <= 100 ? i.gameName : null
    const latency = typeof i.latency === 'number' && Number.isFinite(i.latency) ? i.latency : 0
    updateTray({ state, gameName, latency })
  })
}

// Single-instance: a second launch must not spawn another sidecar / proxy chain. Focus the running
// window instead, then exit immediately. Request the lock before whenReady so the second instance
// never reaches whenReady (which would start a second elevated sidecar and create a window/tray).
const gotSingleInstanceLock = app.requestSingleInstanceLock()

if (!gotSingleInstanceLock) {
  // Nothing has started yet (no sidecar, no window, no tray), so exit synchronously.
  app.exit(0)
} else {
  app.on('second-instance', () => {
    showMainWindow()
  })

  app.whenReady().then(() => {
    electronApp.setAppUserModelId('com.gameplus.app')

    app.on('browser-window-created', (_, window) => {
      optimizer.watchWindowShortcuts(window)
    })

    registerWindowControls()
    // Start the backend early only when the user has already finished onboarding (or in dev).
    // Otherwise the renderer triggers it lazily on its first backend:get-connection, i.e. after
    // the user has read the permission explanation — so no UAC prompt before the app says anything.
    if (getPrefs().onboardingDone || is.dev) {
      startSidecar().catch((e) => console.error('sidecar failed to start:', e))
    }
    createWindow()
    createTray()

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow()
    })
  })

  // Any real quit — tray "退出", OS logoff/shutdown, auto-updater — must NOT be vetoed by the close→hide-to-tray
  // handler. before-quit fires for all of those (but not for a plain window X-close), so mark the intent here.
  app.on('before-quit', () => (isQuitting = true))

  // Hold the quit until the backend has gracefully torn down (restore system proxy, stop cores), then exit.
  // will-quit can fire more than once (tray-exit → app.quit → window-all-closed → app.quit); always hold the
  // quit, but run the teardown only once — its app.exit(0) is what actually terminates.
  let quitStarted = false
  app.on('will-quit', (e) => {
    e.preventDefault()
    if (quitStarted) return
    quitStarted = true
    stopSidecar().finally(() => app.exit(0))
  })

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit()
  })
}
