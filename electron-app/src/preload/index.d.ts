import type { GamePlusApi } from './index'

declare global {
  interface Window {
    gameplus: GamePlusApi
  }
}

export {}
