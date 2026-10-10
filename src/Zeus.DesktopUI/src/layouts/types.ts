export type LayoutLevel = 'safe' | 'medium' | 'advanced'

export type ComponentStatus = 'supported' | 'optional' | 'experimental' | 'conceptual'

export interface LayoutComponent {
  name: string
  description: string
  status: ComponentStatus
  tools?: string[]
}

export interface DesktopLayout {
  id: string
  name: string
  tagline: string
  description: string
  level: LayoutLevel
  tags: string[]
  colors: {
    primary: string
    accent: string
    background: string
    surface: string
  }
  components: LayoutComponent[]
  preview: {
    dock: 'bottom-center' | 'bottom-left' | 'left' | 'hidden'
    taskbar: 'hidden' | 'transparent' | 'classic' | 'minimal'
    widgets: 'none' | 'minimal' | 'rich'
    style: 'clean' | 'neon' | 'glass' | 'terminal' | 'gaming'
  }
}
