import { DesktopLayout } from './types'
import { macosInspired } from './macos-inspired'
import { minimalista } from './minimalista'
import { gamerNeon } from './gamer-neon'
import { windowsModerno } from './windows-moderno'

export const layouts: DesktopLayout[] = [
  windowsModerno,
  macosInspired,
  minimalista,
  gamerNeon,
]

export function getLayoutById(id: string): DesktopLayout | undefined {
  return layouts.find((l) => l.id === id)
}

export * from './types'
export { macosInspired, minimalista, gamerNeon, windowsModerno }
