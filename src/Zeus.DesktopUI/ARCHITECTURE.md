# Arquitetura Zeus PC — Personalização de Desktop

**Estado atual (10/10/2026):** React → preload com métodos específicos → Electron main → API .NET autenticada em 127.0.0.1 → motores `Zeus.Windows`. Veja `REAL-APP.md` e `../Zeus.LocalApi/README.md`. O restante deste documento preserva a proposta original; ferramentas externas não estão integradas.

## Visão

O Zeus PC evolui de ferramenta de diagnóstico para **central de personalização do Windows**.

O usuário escolhe um estilo visual (layout), vê um preview, e aplica. O sistema deve ser:
- Reversível
- Seguro (priorizar APIs e ferramentas que não quebram o sistema)
- Extensível (novos layouts = novos arquivos de definição)

## Camadas de personalização

### Camada 1 — Segura (prioridade máxima)
- Wallpaper (estático / animado)
- Cores e accent do sistema
- Ícones e cursores
- Widgets de desktop (overlay, tipo Rainmeter)
- Transparência da taskbar

### Camada 2 — Intermediária
- Dock alternativo
- Status bar / toolbar custom
- Launcher de aplicativos
- Menu Iniciar estilizado

### Camada 3 — Avançada (Modo Técnico)
- Tiling window manager
- Substituição de taskbar/Start
- Temas visuais profundos (visual styles)

## Modelo de dados de um Layout

```ts
interface DesktopLayout {
  id: string
  name: string
  tagline: string
  description: string
  level: 'safe' | 'medium' | 'advanced'
  tags: string[]
  colors: {
    primary: string
    accent: string
    background: string
    surface: string
  }
  components: {
    name: string
    description: string
    status: 'supported' | 'optional' | 'experimental' | 'conceptual'
    tools?: string[]
  }[]
  preview: {
    dock: 'bottom-center' | 'bottom-left' | 'left' | 'hidden'
    taskbar: 'hidden' | 'transparent' | 'classic' | 'minimal'
    widgets: 'none' | 'minimal' | 'rich'
    style: 'clean' | 'neon' | 'glass' | 'terminal' | 'gaming'
  }
}
```

## Ferramentas de referência (mundo real)

| Ferramenta | Uso no Zeus |
|---|---|
| Seelen UI | Engine principal de shell custom (dock, toolbar, tiling, widgets) |
| Rainmeter | Widgets e skins de desktop |
| Windhawk | Mods granulares (taskbar, Start, Explorer) |
| TranslucentTB | Transparência da taskbar |
| GlazeWM / komorebi | Tiling window manager |
| Lively Wallpaper | Wallpapers animados |
| MyDockFinder / Nexus | Dock estilo macOS |
| WinPaletter | Cores do sistema |
| PowerToys | Launcher e utilitários |

## Fluxo de aplicação de layout (futuro)

1. Usuário seleciona layout na UI
2. Preview atualiza
3. Clica "Aplicar"
4. Zeus valida compatibilidade (Windows 10/11, ferramentas instaladas)
5. Aplica componentes na ordem segura → avançada
6. Salva snapshot do estado anterior (para reverter)
7. Mostra status de cada componente aplicado

## Princípios

- Nunca aplicar mudança sem possibilidade de reverter
- Preferir overlays e hooks a patch de arquivos de sistema
- Layouts são dados, não código hard-coded na UI
- Novos layouts = novo arquivo em `src/layouts/` + export no `index.ts`
