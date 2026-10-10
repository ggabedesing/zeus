import { DesktopLayout } from './types'

export const minimalista: DesktopLayout = {
  id: 'minimalista',
  name: 'Minimalista',
  tagline: 'Fundo discreto para manter o foco',
  description:
    'Desktop limpo ao extremo. Taskbar auto-hide, ícones escondidos, widgets discretos e launcher rápido. Foco total no que você está fazendo.',
  level: 'safe',
  tags: ['minimal', 'produtividade', 'clean', 'foco'],
  colors: {
    primary: '#111113',
    accent: '#6366f1',
    background: '#09090b',
    surface: 'rgba(24, 24, 27, 0.8)',
  },
  components: [
    {
      name: 'Taskbar',
      description: 'Auto-hide total. Aparece só no hover da borda inferior.',
      status: 'supported',
      tools: ['Nativo Windows', 'Windhawk'],
    },
    {
      name: 'Ícones do desktop',
      description: 'Todos ocultos. Desktop limpo.',
      status: 'supported',
      tools: ['Nativo Windows'],
    },
    {
      name: 'Widgets',
      description: 'Apenas relógio discreto no canto. Nada mais.',
      status: 'supported',
      tools: ['Rainmeter (Mond, Paper Thin, Ageo)'],
    },
    {
      name: 'Launcher',
      description: 'PowerToys Run ou Seelen launcher com atalho rápido.',
      status: 'supported',
      tools: ['PowerToys Run', 'Seelen App Launcher'],
    },
    {
      name: 'Workspaces',
      description: 'Desktops virtuais organizados por contexto (Trabalho, Pessoal, etc).',
      status: 'optional',
      tools: ['Nativo Windows', 'Seelen', 'GlazeWM'],
    },
    {
      name: 'Wallpaper',
      description: 'Imagem escura e simples, com bastante espaço negativo.',
      status: 'supported',
      tools: ['Nativo', 'Lively Wallpaper'],
    },
  ],
  preview: {
    dock: 'hidden',
    taskbar: 'minimal',
    widgets: 'minimal',
    style: 'clean',
  },
}
