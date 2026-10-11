// Default public release channel for Game+. The app ships with both URLs EMPTY (features off); the
// user opts in by clicking 「使用默认发布源」 in Settings, which only fills the input field.
export const DEFAULT_RELEASE_CHANNEL = {
  repo: 'WyattCode/GamePlus-releases',
  manifestUrl:
    'https://github.com/WyattCode/GamePlus-releases/releases/latest/download/latest.json',
  rulesUrl:
    'https://raw.githubusercontent.com/WyattCode/GamePlus-releases/main/rules/game-rules.json'
} as const
