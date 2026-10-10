import { DesktopLayout } from './types'

export const windowsModerno: DesktopLayout = {
  id: 'windows-moderno',
  name: 'Windows Moderno',
  tagline: 'Azul sóbrio para um desktop moderno',
  description:
    'Mantém a identidade do Windows 11, mas remove o excesso. Taskbar mais baixa com labels, Start compacto, accent personalizado e poucos widgets discretos. O layout mais seguro e recomendado para a maioria.',
  level: 'safe',
  tags: ['windows', 'moderno', 'seguro', 'limpo'],
  colors: {
    primary: '#1a1a2e',
    accent: '#3b5bff',
    background: '#0f1117',
    surface: 'rgba(30, 34, 45, 0.85)',
  },
  components: [
    {
      name: 'Taskbar',
      description: 'Mais baixa, com labels nos ícones e transparência sutil.',
      status: 'supported',
      tools: ['Windhawk', 'TranslucentTB'],
    },
    {
      name: 'Start Menu',
      description: 'Compacto, sem seção de recomendações e com layout limpo.',
      status: 'supported',
      tools: ['Windhawk (Start Menu Styler)'],
    },
    {
      name: 'Cores / Accent',
      description: 'Accent Zeus + dark mode consistente.',
      status: 'supported',
      tools: ['WinPaletter', 'Nativo'],
    },
    {
      name: 'Widgets desktop',
      description: 'Poucos e discretos (relógio + status do sistema).',
      status: 'supported',
      tools: ['Rainmeter (skins leves)', 'Widgets nativos'],
    },
    {
      name: 'Wallpaper',
      description: 'Estático de alta qualidade ou live wallpaper sutil.',
      status: 'supported',
      tools: ['Lively Wallpaper', 'Nativo'],
    },
    {
      name: 'Context menu',
      description: 'Menu de contexto clássico ou estilizado.',
      status: 'optional',
      tools: ['Windhawk', 'Nilesoft Shell', 'ExplorerPatcher'],
    },
  ],
  preview: {
    dock: 'hidden',
    taskbar: 'classic',
    widgets: 'minimal',
    style: 'clean',
  },
}
