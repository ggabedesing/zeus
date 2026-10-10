# Catálogo de Layouts — Zeus PC

**Aplicação atual:** os quatro presets geram papéis de parede estáticos locais. A imagem exata aparece antes da confirmação. Componentes de dock, widgets e shell descritos abaixo são referências e não são instalados. O estado anterior é salvo e a reversão é verificada pelo motor Windows.

## 1. Windows Moderno
- **ID:** `windows-moderno`
- **Nível:** safe
- **Ideia:** Windows 11 limpo, premium, sem poluição
- **Componentes principais:** Taskbar baixa com labels, Start compacto, accent personalizado, widgets discretos
- **Ferramentas:** Windhawk, TranslucentTB, WinPaletter, Rainmeter leve

## 2. macOS Inspired Clean
- **ID:** `macos-inspired`
- **Nível:** medium
- **Ideia:** Dock central, barra superior, widgets de vidro
- **Componentes principais:** Dock com magnificação, toolbar superior, taskbar escondida, widgets glass
- **Ferramentas:** Seelen UI (Weg + Toolbar), MyDockFinder, Rainmeter (Big Sur/Monterey)

## 3. Minimalista
- **ID:** `minimalista`
- **Nível:** safe
- **Ideia:** Quase nada na tela. Foco máximo.
- **Componentes principais:** Taskbar auto-hide, ícones escondidos, 1 widget discreto, launcher rápido
- **Ferramentas:** Nativo + PowerToys Run + Rainmeter (Mond/Paper Thin)

## 4. Gamer Neon
- **ID:** `gamer-neon`
- **Nível:** safe
- **Ideia:** Monitoramento em destaque com estética neon
- **Componentes principais:** Widgets CPU/GPU/RAM neon, taskbar transparente, wallpaper animado, cores ciano/roxo
- **Ferramentas:** Rainmeter (Cyberpunk/Neon), TranslucentTB, Lively Wallpaper

---

## Como adicionar um novo layout

1. Crie `src/layouts/meu-layout.ts` seguindo o tipo `DesktopLayout`
2. Exporte em `src/layouts/index.ts`
3. O preview e a página de Personalização detectam automaticamente

## Status dos componentes

| Status | Significado |
|---|---|
| supported | Implementável com ferramentas existentes |
| optional | Opcional / polish |
| experimental | Experimental, pode quebrar |
| conceptual | Só referência visual por enquanto |
