# HANDOFF — Zeus PC (para outra IA ou desenvolvedor)

## Atualização real — 10/10/2026

Esta pasta foi integrada ao repositório .NET Zeus. A interface agora tem Início, Meu PC, Aparência e Histórico, sem dados mock. Electron inicia o serviço autenticado `Zeus.LocalApi` somente em 127.0.0.1. Inventário e desempenho usam o motor Windows existente. Papel de parede tem imagem real, confirmação, aplicação e reversão; dock/widgets permanecem referências. Consulte `REAL-APP.md` para execução, limites e arquitetura atual. As seções abaixo preservam o briefing original e podem descrever itens anteriores ou planejados.

## Contexto do projeto

Zeus PC é uma central local para Windows focada em:
1. Diagnóstico de hardware/software
2. Monitoramento (CPU, GPU, RAM, temp)
3. Recomendações de otimização
4. **Personalização visual do desktop Windows** (layouts, temas, animações)

O app tem interface própria (React) e no futuro deve orquestrar personalizações no Windows real (via ferramentas como Seelen UI, Rainmeter, Windhawk, etc.).

## Estado atual do código

- Frontend React + Vite + Tailwind funcionando
- Dashboard limpo com métricas mock
- Sistema de layouts de desktop implementado
- 4 layouts definidos (Windows Moderno, macOS Inspired, Minimalista, Gamer Neon)
- Página de Personalização com preview visual e seleção
- Animações CSS (fade-in, slide-up, scale-in)
- Design system dark premium

## Arquivos mais importantes

| Arquivo | Função |
|---|---|
| `src/layouts/types.ts` | Tipos do sistema de layouts |
| `src/layouts/*.ts` | Definição de cada layout |
| `src/layouts/index.ts` | Registro de todos os layouts |
| `src/components/layouts/LayoutPreview.tsx` | Preview visual do desktop |
| `src/pages/Personalization.tsx` | UI de escolha de layout |
| `src/pages/Dashboard.tsx` | Dashboard principal |
| `src/App.tsx` | Rotas/navegação interna |
| `ARCHITECTURE.md` | Arquitetura de personalização |
| `LAYOUTS.md` | Catálogo dos layouts |
| `README.md` | Visão geral e como rodar |

## O que NÃO está implementado ainda

- Backend / API local (127.0.0.1)
- Métricas reais de hardware
- Ação real de "Aplicar layout" no Windows
- Integração com Seelen UI / Rainmeter / Windhawk
- Sistema de backup/restauração de personalizações
- Mais layouts (Cyberpunk, Hacker, Vidro puro, etc.)

## Como continuar

1. Ler `ARCHITECTURE.md` e `LAYOUTS.md`
2. Rodar o app (`npm install && npm run dev`)
3. Entender o modelo `DesktopLayout` em `src/layouts/types.ts`
4. Para novos layouts: copiar um existente em `src/layouts/` e registrar no `index.ts`
5. Para aplicar de verdade no Windows: orquestrar as ferramentas listadas em cada componente do layout

## Regras importantes do produto

- Servidor só em 127.0.0.1
- Única ação mutável atual no backend original: seleção de plano de energia validado
- Não expor executor genérico de PowerShell/shell pela API
- Personalizações devem ser reversíveis
- Separar experiência normal de Modo Técnico

## Objetivo

Transformar o Zeus PC em uma central confiável onde o usuário escolhe um estilo visual, vê preview, aplica, e pode reverter — com diagnóstico e otimização no mesmo lugar.
