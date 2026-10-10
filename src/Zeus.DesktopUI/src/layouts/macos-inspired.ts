import { DesktopLayout } from './types'

export const macosInspired: DesktopLayout = {
  id: 'macos-inspired',
  name: 'macOS Inspired Clean',
  tagline: 'Tons azuis e profundidade suave',
  description:
    'Dock central com magnificação sutil, barra superior discreta, widgets de vidro e animações suaves. Mantém a produtividade do Windows com a estética refinada do macOS.',
  level: 'medium',
  tags: ['macOS', 'glass', 'dock', 'clean', 'premium'],
  colors: {
    primary: '#1c1c1e',
    accent: '#0a84ff',
    background: '#000000',
    surface: 'rgba(28, 28, 30, 0.72)',
  },
  components: [
    {
      name: 'Dock',
      description: 'Dock inferior central com ícones grandes, magnificação no hover e auto-hide opcional.',
      status: 'supported',
      tools: ['Seelen UI (Weg)', 'MyDockFinder', 'Winstep Nexus'],
    },
    {
      name: 'Barra superior',
      description: 'Status bar fina no topo com relógio, rede, bateria e controles de mídia.',
      status: 'supported',
      tools: ['Seelen Fancy Toolbar'],
    },
    {
      name: 'Taskbar nativa',
      description: 'Ocultada ou substituída pelo dock.',
      status: 'supported',
      tools: ['Seelen UI', 'nativo (auto-hide)'],
    },
    {
      name: 'Widgets de desktop',
      description: 'Relógio, clima e player de mídia com efeito de vidro fosco.',
      status: 'supported',
      tools: ['Rainmeter (Big Sur / Monterey / Frost Glass)', 'Seelen Widgets'],
    },
    {
      name: 'Wallpaper',
      description: 'Imagem estática de alta qualidade ou live wallpaper sutil.',
      status: 'supported',
      tools: ['Lively Wallpaper', 'nativo'],
    },
    {
      name: 'Animações de janela',
      description: 'Minimize/maximize suaves e hover effects no dock.',
      status: 'optional',
      tools: ['Seelen UI', 'MyDockFinder'],
    },
    {
      name: 'Launcher de apps',
      description: 'Launcher rápido estilo Spotlight / Rofi.',
      status: 'optional',
      tools: ['Seelen App Launcher', 'PowerToys Run'],
    },
    {
      name: 'Temas de ícones',
      description: 'Ícones arredondados e consistentes.',
      status: 'optional',
      tools: ['Icon packs via 7tsp / IconPackager'],
    },
  ],
  preview: {
    dock: 'bottom-center',
    taskbar: 'hidden',
    widgets: 'minimal',
    style: 'glass',
  },
}
