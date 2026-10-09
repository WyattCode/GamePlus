import { app, shell, BrowserWindow, ipcMain, nativeTheme, Tray, Menu, nativeImage } from 'electron'
import { readFileSync } from 'fs'
import { join } from 'path'
import { electronApp, optimizer, is } from '@electron-toolkit/utils'
import { startSidecar, stopSidecar } from './sidecar'
import { trayIcon32 } from './trayIcon'

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

function createTray(): void {
  if (tray) return
  tray = new Tray(nativeImage.createFromDataURL(trayIcon32))
  tray.setToolTip('Game+ 游戏加速器')
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: '显示 Game+', click: showMainWindow },
      { type: 'separator' },
      {
        label: '退出',
        click: () => {
          isQuitting = true
          app.quit()
        }
      }
    ])
  )
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

  win.on('ready-to-show', () => win.show())

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
    // Kick the backend off early so it's ready by the time the renderer connects.
    startSidecar().catch((e) => console.error('sidecar failed to start:', e))
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
