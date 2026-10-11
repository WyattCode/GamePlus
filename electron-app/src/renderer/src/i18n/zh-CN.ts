export const zhCN = {
  // Navigation
  'nav.accelerate': '加速',
  'nav.games': '游戏库',
  'nav.servers': '线路',
  'nav.subscriptions': '订阅',
  'nav.logs': '日志',
  'nav.settings': '设置',

  // Title bar
  'titlebar.theme.toggle': '切换主题',
  'titlebar.window.minimize': '最小化',
  'titlebar.window.maximize': '最大化',
  'titlebar.window.close': '关闭',
  'status.idle': '未连接',
  'status.connecting': '连接中',
  'status.accelerating': '加速中',
  'status.stopping': '停止中',

  // Mini window
  'mini.status.idle': '未加速',
  'mini.status.connecting': '连接中',
  'mini.status.accelerating': '加速中',
  'mini.status.stopping': '停止中',
  'mini.latency.idle': '未加速',
  'mini.game.none': '未选择游戏',
  'mini.action.start': '开始加速',
  'mini.action.stop': '停止加速',
  'mini.action.openMain': '打开主界面',

  // Settings — page
  'settings.title': '设置',

  // Settings — appearance
  'settings.appearance.title': '外观',
  'settings.theme.name': '主题',
  'settings.theme.desc': '选择浅色 / 深色，或跟随系统',
  'settings.theme.light': '浅色',
  'settings.theme.dark': '深色',
  'settings.theme.system': '跟随系统',

  // Settings — language
  'settings.language.title': '语言 / Language',
  'settings.language.name': '语言 / Language',
  'settings.language.desc': '选择界面显示语言',
  'settings.language.zhCN': '简体中文',
  'settings.language.en': 'English',

  // Settings — general
  'settings.general.title': '通用',
  'settings.minimizeToTray.name': '最小化到托盘',
  'settings.minimizeToTray.desc': '点击关闭时隐藏到系统托盘而非退出',
  'settings.onboarding.name': '新手引导',
  'settings.onboarding.desc': '重新查看首次使用的引导步骤',
  'settings.onboarding.button': '重新查看新手引导',

  // Settings — startup & tray
  'settings.startup.title': '启动与托盘',
  'settings.autoStart.name': '开机自动启动',
  'settings.autoStart.desc': '通过 Windows 计划任务以管理员权限启动，登录时不再弹出权限确认。',
  'settings.startHidden.name': '开机启动时最小化到托盘',
  'settings.startHidden.desc': '开机启动后不显示主窗口，仅在托盘运行',
  'settings.launchNode.name': '加速并启动时的节点',
  'settings.launchNode.desc': '「加速并启动」按钮和桌面快捷方式使用哪个节点。没有手动选过节点时总是智能选节点。',
  'settings.launchNode.keep': '保留当前选择',
  'settings.launchNode.smart': '始终使用智能选节点',
  'settings.autoAccelerate.name': '游戏启动时自动加速（总开关）',
  'settings.autoAccelerate.desc': '关闭后所有游戏的自动加速都不会生效',

  // Settings — rules
  'settings.rules.title': '游戏规则库',
  'settings.rules.version': '当前版本：v{version}（{source}）',
  'settings.rules.gameCount': '{count} 个游戏',
  'settings.rules.lastCheck': '上次检查：{time}',
  'settings.rules.source.bundled': '内置',
  'settings.rules.source.cached': '本地缓存',
  'settings.rules.source.remote': '在线更新',
  'settings.rules.url.name': '规则更新地址',
  'settings.rules.url.placeholder': 'https://…/game-rules.json，留空则不联网更新',
  'settings.rules.useDefault': '使用默认发布源',
  'settings.rules.save': '保存',
  'settings.rules.check': '立即检查',
  'settings.rules.checking': '检查中…',

  // Settings — diagnostics
  'settings.diagnostics.title': '诊断',
  'settings.diagnostics.name': '网络诊断',
  'settings.diagnostics.desc': '检查驱动、权限、端口、线路和 DNS 等常见问题',
  'settings.diagnostics.button': '开始诊断',

  // Settings — advanced
  'settings.advanced.title': '高级',
  'settings.advanced.name': '高级设置',
  'settings.advanced.desc': 'TUN DNS、本地端口、选路策略等进阶选项',
  'settings.advanced.button': '打开',

  // Settings — about
  'settings.about.title': '关于',
  'settings.about.desc': 'Electron + Vue 3 · 游戏加速器',

  // Toasts / errors
  'toast.saved': '已保存',
  'toast.saveFailed': '保存失败：{error}',
  'toast.checkFailed': '检查失败：{error}',
  'toast.setFailed': '设置失败：{error}',
  'error.backendUnreachable': '无法连接后台服务'
} as const

export type MessageKey = keyof typeof zhCN
