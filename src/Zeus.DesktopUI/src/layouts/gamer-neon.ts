import { DesktopLayout } from './types'

export const gamerNeon: DesktopLayout = {
  id: 'gamer-neon',
  name: 'Gamer Neon',
  tagline: 'Ciano vibrante sobre fundo escuro',
  description:
    'Visual gamer moderno com widgets de CPU, GPU, temperatura e rede em estilo neon. Taskbar transparente, wallpaper animado e cores vibrantes (ciano, roxo, verde).',
  level: 'safe',
  tags: ['gamer', 'neon', 'monitoramento', 'rgb'],
  colors: {
    primary: '#0a0a12',
    accent: '#00f0ff',
    background: '#05050a',
    surface: 'rgba(10, 15, 30, 0.75)',
  },
  components: [
    {
      name: 'Widgets de hardware',
      description: 'CPU, GPU, RAM, temperatura e rede com visual neon/HUD.',
      status: 'supported',
      tools: ['Rainmeter (Cyberpunk, Neon suites, SysDash)'],
    },
    {
      name: 'Taskbar',
      description: 'Transparente ou com glow sutil.',
      status: 'supported',
      tools: ['TranslucentTB', 'Windhawk'],
    },
    {
      name: 'Wallpaper',
      description: 'Animado ou estático com estética cyber/neon.',
      status: 'supported',
      tools: ['Lively Wallpaper', 'Wallpaper Engine'],
    },
    {
      name: 'Dock (opcional)',
      description: 'Dock inferior com ícones neon se preferir substituir a taskbar.',
      status: 'optional',
      tools: ['Seelen Weg', 'Winstep Nexus'],
    },
    {
      name: 'Cores do sistema',
      description: 'Accent ciano/roxo + dark mode profundo.',
      status: 'supported',
      tools: ['WinPaletter', 'Nativo'],
    },
    {
      name: 'Visualizadores de áudio',
      description: 'Barras de áudio reativas para música/jogos.',
      status: 'optional',
      tools: ['Rainmeter (Fountain of Colors, Monstercat)'],
    },
  ],
  preview: {
    dock: 'bottom-center',
    taskbar: 'transparent',
    widgets: 'rich',
    style: 'neon',
  },
}
