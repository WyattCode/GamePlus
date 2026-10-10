import type { MessageKey } from './zh-CN'

export const en: Partial<Record<MessageKey, string>> = {
  // Navigation
  'nav.accelerate': 'Boost',
  'nav.games': 'Library',
  'nav.servers': 'Servers',
  'nav.subscriptions': 'Subscriptions',
  'nav.logs': 'Logs',
  'nav.settings': 'Settings',

  // Title bar
  'titlebar.theme.toggle': 'Toggle theme',
  'titlebar.window.minimize': 'Minimize',
  'titlebar.window.maximize': 'Maximize',
  'titlebar.window.close': 'Close',
  'status.idle': 'Disconnected',
  'status.connecting': 'Connecting',
  'status.accelerating': 'Boosting',
  'status.stopping': 'Stopping',

  // Mini window
  'mini.status.idle': 'Not boosted',
  'mini.status.connecting': 'Connecting',
  'mini.status.accelerating': 'Boosting',
  'mini.status.stopping': 'Stopping',
  'mini.latency.idle': 'Not boosting',
  'mini.game.none': 'No game selected',
  'mini.action.start': 'Start boost',
  'mini.action.stop': 'Stop boost',
  'mini.action.openMain': 'Open main window',

  // Settings — page
  'settings.title': 'Settings',

  // Settings — appearance
  'settings.appearance.title': 'Appearance',
  'settings.theme.name': 'Theme',
  'settings.theme.desc': 'Choose light / dark, or follow the system',
  'settings.theme.light': 'Light',
  'settings.theme.dark': 'Dark',
  'settings.theme.system': 'System',

  // Settings — language
  'settings.language.title': 'Language / 语言',
  'settings.language.name': 'Language / 语言',
  'settings.language.desc': 'Choose the interface language',
  'settings.language.zhCN': '简体中文',
  'settings.language.en': 'English',

  // Settings — general
  'settings.general.title': 'General',
  'settings.minimizeToTray.name': 'Minimize to tray',
  'settings.minimizeToTray.desc': 'Hide to the system tray instead of quitting when closing',
  'settings.onboarding.name': 'Onboarding',
  'settings.onboarding.desc': 'Review the first-run guide steps again',
  'settings.onboarding.button': 'Show onboarding again',

  // Settings — startup & tray
  'settings.startup.title': 'Startup & tray',
  'settings.autoStart.name': 'Launch at login',
  'settings.autoStart.desc':
    'Starts via a Windows scheduled task with administrator rights, so no permission prompt appears at sign-in.',
  'settings.startHidden.name': 'Start minimized to tray',
  'settings.startHidden.desc': 'Do not show the main window on launch; run in the tray only',
  'settings.autoAccelerate.name': 'Auto-boost when a game starts (master switch)',
  'settings.autoAccelerate.desc': 'When off, auto-boost is disabled for all games',

  // Settings — rules
  'settings.rules.title': 'Game rules',
  'settings.rules.version': 'Current version: v{version} ({source})',
  'settings.rules.gameCount': '{count} games',
  'settings.rules.lastCheck': 'Last checked: {time}',
  'settings.rules.source.bundled': 'Bundled',
  'settings.rules.source.cached': 'Local cache',
  'settings.rules.source.remote': 'Online update',
  'settings.rules.url.name': 'Rules update URL',
  'settings.rules.url.placeholder': 'https://…/game-rules.json — leave empty to disable online updates',
  'settings.rules.useDefault': 'Use default source',
  'settings.rules.save': 'Save',
  'settings.rules.check': 'Check now',
  'settings.rules.checking': 'Checking…',

  // Settings — diagnostics
  'settings.diagnostics.title': 'Diagnostics',
  'settings.diagnostics.name': 'Network diagnostics',
  'settings.diagnostics.desc': 'Check common issues with driver, permissions, ports, servers and DNS',
  'settings.diagnostics.button': 'Run diagnostics',

  // Settings — advanced
  'settings.advanced.title': 'Advanced',
  'settings.advanced.name': 'Advanced settings',
  'settings.advanced.desc': 'TUN DNS, local port, routing policy and other advanced options',
  'settings.advanced.button': 'Open',

  // Settings — about
  'settings.about.title': 'About',
  'settings.about.desc': 'Electron + Vue 3 · Game accelerator',

  // Toasts / errors
  'toast.saved': 'Saved',
  'toast.saveFailed': 'Save failed: {error}',
  'toast.checkFailed': 'Check failed: {error}',
  'toast.setFailed': 'Update failed: {error}',
  'error.backendUnreachable': 'Cannot reach the backend service'
}
