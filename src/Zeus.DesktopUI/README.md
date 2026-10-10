# Zeus PC

**Aplicativo Windows real (0.2.0):** abra `release/Zeus-PC-0.2.0-Windows.exe`. Há dados reais do PC e aplicação/reversão de papel de parede. Veja [REAL-APP.md](REAL-APP.md) para construir e conhecer os limites. O briefing original abaixo continua como referência de objetivos; a lista completa de funcionalidades ainda não está entregue.

Central local premium para Windows — Diagnóstico, monitoramento, otimização contextual e **personalização visual do desktop**.

## Stack

- React 19 + TypeScript
- Vite 6
- Tailwind CSS 3
- Lucide React
- Recharts (preparado)

## Como rodar

```bash
npm install
npm run dev
```

Abre em `http://localhost:5173`

## O que já existe

### UI
- Dashboard limpo (CPU, GPU, RAM, Storage)
- Recomendações baseadas em evidências
- Sidebar minimalista
- Toggle Modo Técnico
- Design system dark premium (cores Zeus + surfaces)

### Personalização de Desktop
- Sistema de **layouts** estruturado
- 4 layouts prontos:
  1. **Windows Moderno** (safe)
  2. **macOS Inspired Clean** (medium)
  3. **Minimalista** (safe)
  4. **Gamer Neon** (safe)
- Preview visual de cada layout
- Página de Personalização com seleção, paleta e componentes
- Animações (fade-in, slide-up, scale-in)

## Estrutura do projeto

```
src/
├── components/
│   ├── layout/           # Sidebar, Header
│   ├── dashboard/        # MetricCard, Recommendations
│   ├── layouts/          # LayoutPreview (preview visual do desktop)
│   └── ui/               # Card base
├── layouts/              # Definições dos layouts de desktop
│   ├── types.ts          # Tipos (DesktopLayout, ComponentStatus, etc.)
│   ├── index.ts          # Exporta todos os layouts
│   ├── windows-moderno.ts
│   ├── macos-inspired.ts
│   ├── minimalista.ts
│   └── gamer-neon.ts
├── pages/
│   ├── Dashboard.tsx
│   └── Personalization.tsx
├── lib/utils.ts
├── App.tsx
├── main.tsx
└── index.css
```

## Sistema de Layouts

Cada layout (`DesktopLayout`) define:

- `id`, `name`, `tagline`, `description`
- `level`: `safe` | `medium` | `advanced`
- `tags`
- `colors` (primary, accent, background, surface)
- `components[]` — o que o layout modifica + status + ferramentas
- `preview` — como o preview visual deve se comportar (dock, taskbar, widgets, style)

Status dos componentes:
- `supported` — pode ser implementado com ferramentas existentes
- `optional` — opcional / polish
- `experimental` — experimental
- `conceptual` — só referência visual

## Próximos passos sugeridos

1. Conectar com API local (127.0.0.1) para métricas reais
2. Implementar ação real de "Aplicar layout" (orquestrar ferramentas)
3. Adicionar mais layouts (Cyberpunk, Hacker/Terminal, Vidro)
4. Pré-visualização mais rica / animações de transição entre layouts
5. Backend de validação e reversão de personalizações

## Filosofia

- Limpo e premium
- Hierarquia clara
- Dark-first
- Progressive disclosure
- Separação Modo Normal / Modo Técnico
- Personalização de desktop como feature central (não só UI do app)

## Licença

Projeto em desenvolvimento.
