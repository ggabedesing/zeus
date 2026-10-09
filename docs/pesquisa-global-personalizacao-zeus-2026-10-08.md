# Pesquisa global de personalização visual para o ZEUS

**Coleta:** 8 de outubro de 2026  
**Projeto observado:** `zeus-dev`, branch `feat/zeus-windows-mvp`  
**Escopo:** pesquisa e arquitetura; nenhuma personalização foi aplicada ao PC e nenhum código do produto foi alterado nesta pesquisa.

## Resumo executivo

O ZEUS pode oferecer experiências visuais diferentes sem prometer que controla todo o Windows. O caminho confiável é montar um catálogo de **perfis visuais com recursos próprios** — cores do ZEUS, papel de parede escolhido, widgets leves e atalhos para as opções oficiais do Windows — e mostrar, antes de cada mudança, exatamente quais componentes serão afetados.

Há um limite importante: a Microsoft documenta opções de usuário para cores, alinhamento, ícones e comportamento da barra de tarefas, mas não uma API pública geral para substituir ou redesenhar Explorer, Iniciar, barra de tarefas e janelas do sistema. As demonstrações mais radicais encontradas usam mods que entram no processo do Explorer, ou trocam componentes do shell. Devem ser tratadas como referências avançadas e não como base do ZEUS.

O aplicativo atual é WPF em `.NET 10`, já tem três temas internos (`Completo`, `Mínimo`, `Aurora · inspirado no macOS`), papel de parede com histórico, preferências visuais do Windows e relógio opcional. A recomendação é evoluir esses pontos de forma aditiva, sem reescrever a interface aprovada nem transformar o projeto em substituto do Explorer.

O usuário acrescentou que espera uma solução open source que funcione como aplicativo `.exe`. A inspeção do checkout não encontrou uma licença `LICENSE` na raiz; antes da distribuição pública do executável, o projeto deve escolher uma licença própria e inventariar avisos/licenças de dependências e ativos. Não copiar código, temas, ícones, cursores, fontes ou imagens de terceiros sem verificar a licença específica.

## Como a pesquisa foi feita e limites

- Priorizei documentação oficial, repositórios mantidos pelos próprios projetos e páginas de autores.
- Conferi páginas e metadados públicos atuais de licenças, compatibilidade e atividade do repositório quando disponíveis.
- As páginas do YouTube permitiram validar título, autor, descrição e capítulos. O leitor web não conseguiu abrir o player/transcrição; portanto, as notas de vídeo abaixo são explicitamente baseadas nos metadados e capítulos publicados, e não afirmam uma análise quadro a quadro.
- “Baixa carga” só é afirmada quando o próprio projeto fornece essa informação. Ainda assim, custo real depende do equipamento, conteúdo, monitores e regras de pausa; o ZEUS deve medir localmente antes de fazer afirmações de desempenho.
- Fontes não oficiais ou experimentais são ideias para observar, não certificação de segurança.

## A. Galeria global: 30 achados verificáveis

| # | Referência | O que a evidência mostra | O que o ZEUS pode aproveitar |
|---:|---|---|---|
| 1 | [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) | Plataforma recomendada pela Microsoft para novas aplicações; pode ser acrescentada a WPF, WinForms e Win32. A documentação cita Windows 10 1809+ para APIs do SDK. | Possível adoção seletiva para recursos modernos, sem migrar toda a aplicação WPF. O mínimo técnico do SDK não significa que aquela versão do Windows ainda tenha suporte de segurança. |
| 2 | [Visual Layer](https://learn.microsoft.com/windows/apps/develop/composition/visual-layer) | Composição fornece visuais, efeitos e animações aceleradas; a documentação descreve seu uso também em aplicativos WPF. | Investigar apenas se Storyboards WPF não atenderem a um efeito próprio do ZEUS. Prototipar isoladamente, medir CPU/GPU e manter fallback sem efeito. |
| 3 | [Animações WPF](https://learn.microsoft.com/dotnet/desktop/wpf/graphics-multimedia/animation-overview) | `Storyboard` e animações de propriedades são mecanismos nativos do framework já usado no ZEUS. | Primeira opção para transições dentro do próprio app (painéis, estados de carregamento, cartões), com duração curta e cancelável. |
| 4 | [Storyboards WPF](https://learn.microsoft.com/dotnet/desktop/wpf/graphics-multimedia/storyboards-overview) | Storyboards agrupam sequências e transições controláveis por XAML/código. | Animação local reutilizável em templates; não controla animações globais do Explorer ou de aplicativos de terceiros. |
| 5 | [Parâmetros de acessibilidade do Windows](https://learn.microsoft.com/windows/win32/winauto/accessibility-parameters) | O Windows comunica preferências de acessibilidade; animação de área de cliente está entre os parâmetros que apps devem respeitar. | Ler e respeitar redução de movimento no ZEUS; não depender apenas de um toggle próprio. Sempre oferecer estado final sem animação. |
| 6 | [Materiais Windows: Mica, Acrylic e Smoke](https://learn.microsoft.com/windows/apps/design/signature-experiences/materials) | Microsoft separa materiais por finalidade; Acrylic é translúcido para superfícies temporárias, Mica é base opaca/tintada por wallpaper. | Usar como referência de hierarquia e contraste. “Glass em tudo” não segue a orientação do sistema e pode reduzir legibilidade. |
| 7 | [Materiais em WinUI e fallbacks](https://learn.microsoft.com/windows/apps/develop/ui/materials) | Materiais dependem da versão, GPU/compositor e preferências; RDP, economia de bateria e hardware podem trocar o efeito por cor sólida. | Tema precisa declarar `fallback`; nunca considerar transparência como requisito para compreender a tela. |
| 8 | [Cores oficiais do Windows](https://support.microsoft.com/windows/experience/personalization/personalize-your-colors-in-windows) | Modos claro/escuro, cor de destaque, efeitos de transparência e contraste são opções do próprio sistema no Windows 10/11. | Começar com prévia e atalho para Configurações. Distinguir “abre Configurações” de “ZEUS aplicou”. |
| 9 | [Personalização da barra de tarefas](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows) | Alinhamento, itens, bandeja, esconder e opções de comportamento variam por versão; no Windows 11 a barra fica na parte inferior e ícones podem ficar centralizados ou à esquerda. | Catálogo de capacidades detectadas por versão; no fallback, abrir `ms-settings:taskbar`. Não oferecer opções laterais/topo como se fossem nativas do Windows 11. |
| 10 | [IDesktopWallpaper](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper) | Interface Win32 pública para wallpaper, monitor, posição, apresentação de slides e cores de fundo; mínimo Windows 8 para desktop apps. | É uma base documentada para wallpaper multi-monitor. Ler antes, registrar estado por monitor e slideshow, aplicar por consentimento, conferir e reverter. |
| 11 | [IVirtualDesktopManager](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager) | Interface pública para consultar/mover janelas entre desktops virtuais. A página orienta não trocar automaticamente o desktop do usuário. | Só investigar gestão de janelas próprias. Não confundir a interface com API pública completa para criar, renomear e animar toda a experiência de desktops virtuais. |
| 12 | [PowerToys FancyZones](https://learn.microsoft.com/windows/powertoys/fancyzones) | Ferramenta da Microsoft com layouts grid/canvas, atalhos, zonas multi-monitor e preview. A documentação alerta que edição manual dos JSON internos pode causar problemas. | Referência prática para perfil “Produtividade”; integrar como link/atalho para o PowerToys instalado. Não editar arquivos internos do usuário. |
| 13 | [PowerToys no GitHub](https://github.com/microsoft/PowerToys) | Projeto oficial MIT, ativo e com utilitários de produtividade; seu repositório declara coleta de telemetria diagnóstica básica. | Referência de UX e licença permissiva, mas revisar dependências, CLA, notice e telemetria antes de incorporar qualquer código. Preferir não redistribuir o produto inteiro dentro do ZEUS. |
| 14 | [Configurações Desktop & Dock da Apple](https://support.apple.com/en-gb/guide/mac-help/mchlp1119/mac) | O macOS oferece controles explícitos para posição, ampliação, esconder, animação de minimizar e animação ao abrir apps no Dock. | Referência de comportamento para um dock opcional construído pelo ZEUS; não é evidência de que Windows exponha os mesmos controles globais. |
| 15 | [Apple HIG: Motion](https://developer.apple.com/design/human-interface-guidelines/motion) | Recomenda movimento intencional, breve, opcional, que não carregue sozinho o significado e que possa ser cancelado. | Princípios independentes de plataforma para animações ZEUS e acessibilidade. |
| 16 | [Tema global KDE Plasma](https://docs.kde.org/stable_kf6/en/plasma-workspace/kcontrol/lookandfeel/) | Um pacote pode reunir layout de painéis/widgets, cores, ícones, cursores, decoração, wallpaper, task switcher e tela de bloqueio, permitindo selecionar componentes. | Excelente modelo de catálogo por componentes e prévia. No ZEUS, cada item precisa ser marcado como suportado, manual, externo ou indisponível. |
| 17 | [Área de trabalho Plasma e widgets](https://docs.kde.org/stable_kf6/en/plasma-desktop/plasma-desktop/desktop.html) | Widgets do Plasma podem ser movidos, redimensionados e organizados em painéis/área de trabalho; a configuração de layout faz parte do ambiente gráfico. | Inspiração para mover/redimensionar widgets próprios do ZEUS e para guardar coordenadas por monitor/DPI. |
| 18 | [Animações Hyprland](https://wiki.hypr.land/Configuring/Advanced-and-Cool/Animations/) | A configuração declara escopo, duração, curva e estilo para janelas/workspaces; documentação alerta que animações contínuas renovam quadros e afetam CPU/GPU/bateria. | Design de “motion profiles” para uma versão futura; animar sob demanda, nunca manter efeitos de loop decorativos ativos sem opção e medição. |
| 19 | [GNOME Shell Extensions: funcionamento e limites](https://extensions.gnome.org/about/) | Extensões podem modificar partes centrais do Shell; seu código vira parte do ambiente e suporte é do autor, fora do processo normal de design GNOME. | A comparação mostra por que extensões de shell ampliam risco e compatibilidade. Não há equivalência segura automática para extensões de Explorer. |
| 20 | [GNOME Open Bar](https://extensions.gnome.org/extension/6580/open-bar/) | Extensão real configura top bar, menus, dock e cores, com opção de gerar tema a partir do wallpaper e importar/exportar escolhas. | Inspiração para editor de tema por componente e paleta derivada da imagem; reproduzir no app sem copiar código/arte. |
| 21 | [Windhawk: coleção oficial de mods](https://github.com/ramensoftware/windhawk-mods) | Marketplace de modificações específicas, cada uma com arquivo, versão, autor e licença; algumas alteram o processo `explorer.exe`. | Referência útil de granularidade, atualização e apresentação de compatibilidade; risco alto demais para instalação automática/embutida pelo ZEUS. Licença tem de ser analisada por mod. |
| 22 | [Guia de estilos da taskbar Windows 11](https://github.com/ramensoftware/windows-11-taskbar-styling-guide) | Catálogo comunitário de temas (DockLike, Plasma, WinXP, Glass etc.) que dependem do mod Windhawk Taskbar Styler e têm seletores internos do Shell. | Galeria visual e vocabulário de estilos. Os seletores internos não são API estável; usar como inspiração, não como dependência. |
| 23 | [Guia de estilos do menu Iniciar](https://github.com/ramensoftware/windows-11-start-menu-styling-guide) | Coleção de configurações comunitárias para Start Menu Styling via Windhawk. | Referência visual avançada; não prometer instalação compatível com qualquer build do Windows. |
| 24 | [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) | Aplicação GPL-3 para Windows 10/11, com estados transparente, blur, acrylic e variantes por estado da taskbar; projeto informa poucos MB e CPU quase nula em seu cenário. | Estudar transições de estado e UI. Não integrar código GPL sem decisão de licença; não aplicar a alteração de Registro sugerida pelo projeto para alguns modos de inicialização. |
| 25 | [Rainmeter](https://github.com/rainmeter/rainmeter) e [manual de skins](https://github.com/rainmeter/rainmeter-docs/blob/master/source/manual/skins/index.html) | GPL-2; skins são janelas independentes, movíveis e interativas; o app declara que não envia dados por padrão, mas skins podem acessar rede conforme configuração. | Referência de widgets (relógio, CPU, visualizador). No ZEUS, widget próprio ou catálogo auditado; não instalar skins arbitrárias como se fossem dados inertes. |
| 26 | [Lively Wallpaper](https://github.com/rocksdanister/lively) | GPL-3, Windows 10 1809/1903+ conforme distribuição. O projeto declara pausar wallpapers em fullscreen, bateria e Remote Desktop para reduzir carga. | Integração opcional por abrir o produto oficial/fornecer recomendação. Não embutir media engine/plugin ou prometer impacto zero; medir e validar regras no equipamento real. |
| 27 | [Seelen UI](https://github.com/eythaann/seelen-ui) | Ambiente de desktop substituto para Windows 10/11, com dock, toolbar, widgets, launcher e temas; repo declara AGPL-3 e pacotes assinados. | Excelente referência de alcance visual e organização de configurações. É substituição de shell/ambiente; usar apenas como benchmark, não como arquitetura do ZEUS. |
| 28 | [ExplorerPatcher: releases e avisos](https://github.com/valinet/ExplorerPatcher/releases) | GPL-2 e alterações de Explorer/taskbar/Start. Releases recentes exibem avisos de risco de Explorer não iniciar sob Smart App Control e de incompatibilidades com certos produtos de segurança/builds. | Não recomendar instalação automática. O próprio aviso sobre desativar uma proteção de segurança exclui esse fluxo da política segura do ZEUS. |
| 29 | [GlazeWM](https://github.com/glzr-io/glazewm) | Gerenciador de janelas tiling com configuração YAML, GPL-3; releases e commits continuam em 2026. Alguns efeitos, como bordas, são limitados a Windows 11. | Referência para workspace de programação/produtividade e regras por app; integração futura só via interface documentada, com consentimento e opção de parar/restaurar. |
| 30 | [Komorebi](https://github.com/LGUG2Z/komorebi) | Gerenciador de tiling para Windows 10+, baseado no DWM, com demonstrações de workspaces, barra de estado, transparência e animações. A licença Komorebi 2.0 restringe redistribuição e uso comercial. | Referência para interações e eventos de janela; não redistribuir/integrar sem autorização/licença apropriada. O projeto documenta comando de recuperação para restaurar janelas ocultas. |

### Vídeos e demonstrações em movimento

1. **“Make your KDE Sweet | Customizing KDE Plasma”**, canal **NH Soft**, publicado em 23/04/2025. [Vídeo direto](https://www.youtube.com/watch?v=PyyxQYkloLo). Capítulos publicados: wallpaper em `0:51`, tema/ícones/título em `3:34`, blur/transparência em `6:55`, animação em `9:38`, Alt+Tab em `10:58`. O próprio autor diz Plasma 6.3.4 e KDE Neon. Li descrição e capítulos; o player não ficou disponível para inspeção visual neste canal.
2. **“How to Use FancyZones in PowerToys (Windows 11) - Custom Window Layouts Tutorial”**, canal Delft Stack, publicado em 30/12/2025. [Vídeo direto](https://www.youtube.com/watch?v=FrcE1Fx41X8). A descrição lista criar layout e arrastar janelas; capítulos publicados mostram introdução em `0:00`, início do tutorial em `0:12`, encerramento em `3:10`. O valor principal é confirmar o fluxo de preview e zonas, que também está descrito na documentação oficial Microsoft.
3. **“REPLACE Windows 11 Taskbar for ExplorerPatcher!”**, [vídeo direto](https://www.youtube.com/watch?v=IiAnpSBtlOs). A página de resultados do YouTube identifica uma demonstração de substituição de taskbar/Start por ExplorerPatcher. Não foram expostos autor confiável, capítulos ou fonte do vídeo com detalhes suficientes; mantenho apenas como pista visual e não como tutorial validado. Compare com os avisos de compatibilidade e segurança nas releases oficiais de ExplorerPatcher.

## B. Catálogo de animações

| Interação | Efeito que as referências mostram | Tecnologia confirmada / limite | Direção para o ZEUS |
|---|---|---|---|
| Abrir/fechar painel do próprio ZEUS | Opacidade e deslocamento curtos, vinculados ao painel que aparece. | Storyboards WPF suportados dentro do aplicativo. | Implementação direta. Reagir à preferência de movimento reduzido; no modo reduzido, trocar por mudança instantânea de estado. |
| Carregamento/progresso | Indicador e descrição de etapa, sem bloquear a tela inteira. | WPF / estados de controles; composição é opção avançada. | Priorizar informação e cancelamento antes de animação decorativa. |
| Hover/pressed em botões | Cor/borda/escala muito sutil; feedback acompanha o input. | Templates WPF ou XAML/Visual States; WinUI Composition se houver migração específica. | Implementação direta nos controles próprios, sem bounce repetitivo. |
| Trocar layout/paleta | Prévia instantânea ou transição curta para cores e superfícies do app. | WPF pode animar brushes/propriedades; Acrylic/Mica são materiais de app e têm fallback. | Implementar localmente; botão “desfazer” e contraste verificado. |
| Minimizar/maximizar janelas do Windows | Efeito pertence ao compositor/shell, varia por versão e configuração. | Nenhuma API geral documentada para um app redesenhar o efeito global. | Manter configuração oficial do Windows; evitar hacks de DWM/Explorer. |
| Dock com ícones ampliados | Ampliação segue a distância do ponteiro, com entrada/saída curta. | Demonstrado em Dock/macOS; no Windows, requer dock próprio sempre ativo, janela overlay, input e acessibilidade. | Projeto avançado, opcional e desligado por padrão; medir consumo e tratar múltiplos monitores. |
| Troca de workspaces | Deslizamento/popin e indicador do workspace ativo. | Hyprland implementa configurável; Windows IVirtualDesktopManager público só cobre operações de janelas selecionadas, não a experiência completa. | Fazer apenas no workspace próprio do ZEUS ou abrir solução externa; não simular controle nativo. |
| Wallpapers animados | Vídeo/shader/HTML no plano de fundo, pausado em fullscreen por ferramenta existente. | Lively declara renderer baseado em WinUI 3 e regras de pausa. | Não implementar no primeiro catálogo; link opcional e medir no hardware antes/depois. |
| Notificações/toasts | Painel aparece brevemente e se fecha com interação ou timeout. | APIs oficiais de notificações do Windows e estilos de app; o shell mantém seus próprios flyouts. | Usar notificações próprias/documentadas; não substituir Central de Notificações. |
| Troca de papel de parede | Troca de imagem por monitor ou apresentação de slides. | `IDesktopWallpaper` documentado; nenhuma promessa de crossfade global. | Preview próprio; aplicar uma operação; ler estado anterior e validar retorno antes/depois. |
| Efeitos contínuos (pulsar, blur animado, parallax) | Hynprland alerta que loop renderiza continuamente e usa CPU/GPU/bateria. | Há custo variável; efeito do material também pode ser desligado pelo Windows. | Excluir de perfis padrão. Se um dia entrar, permitir opt-in, duração limitada e pausa em jogo/bateria. |

## C. Catálogo de ferramentas, licença e papel

| Projeto | Licença declarada | Windows / atividade observada | Papel e risco para o ZEUS |
|---|---|---|---|
| Microsoft Windows App SDK | Componentes SDK conforme licença do pacote | Windows 10 1809+ para parte do SDK; documentação atual. | APIs para o app ZEUS; adoção incremental possível. Compatibilidade mínima não equivale a sistema operacional ainda mantido. |
| Microsoft PowerToys / FancyZones | MIT no repo; repositório tem CLA e informa telemetria diagnóstica básica | Releases ativos em 2026; Windows. | Ferramenta oficial opcional para layouts de janelas. Usar atalho/documentação, não manipular JSON interno. |
| Windhawk e mods | Licença varia por mod; a coleção diz que mod sem licença declarada cai sob MIT naquele repositório, mas autores podem incluir código de terceiros | Ativo em 2026; mexe em processos/comportamento de programas | Inspiração e extensão avançada. Revisão por versão/build, origem e licença por mod; risco de atualização do Windows e de código dentro do Explorer. |
| TranslucentTB | GPL-3 | Windows 10/11; estados de taskbar por condição | Referência e opção externa. Não copiar/redistribuir sem resolver obrigação GPL e revisão de código. Não seguir instrução de Registro que altere política para inicialização. |
| Rainmeter | GPL-2 | Windows 7–11 declarado no site, repo ativo; usuários carregam skins | Boa referência de widgets; pacote/skin é executável e pode ter rede. Não assumir que skin de terceiros é segura. |
| Lively Wallpaper | GPL-3 | Store requer Windows 10 1809+; instalador 1903+; repo/release ativo | Apenas integração opcional por link/recomendação ou estudar arquitetura; componentes de vídeo/plugins elevam superfície e dependências. |
| Seelen UI | AGPL-3 | Windows 10/11 declarado; release 1.10 encontrado em 2026, artefatos declarados assinados | Benchmark de substituição total, fora do caminho seguro normal do ZEUS. |
| ExplorerPatcher | GPL-2 | Releases em 2026 e testes declarados em várias builds Windows | Alto risco: modifica Explorer/Start; próprias notas alertam Smart App Control e problemas de segurança. Não recomendar nem automatizar. |
| GlazeWM | GPL-3 | Windows e macOS; v3.10.1 em 2026 | Referência de tiling; integração exige pacote/licença e controles de janela. |
| Komorebi | Komorebi 2.0, restritiva: não comercial e restringe redistribuição | Windows 10+; repo com mudanças em 2026 | Estudar CLI/eventos e recuperação; não embutir no `.exe` sem licença comercial/redistribuição. |

**Licença do ZEUS:** o checkout pesquisado não possui licença `LICENSE` na raiz. Para chamar o produto de open source e distribuir o `.exe`, publicar uma licença escolhida pelo mantenedor, arquivo de notices/SBOM, licenças de NuGet/runtime/recursos, política de atualizações e origem dos ativos visuais. Isso é um requisito de lançamento, não uma autorização para copiar código alheio.

## D. Matriz de viabilidade

Legenda: risco baixo = código isolado no processo do ZEUS; médio = altera uma configuração do usuário reversível; alto = shell/process hooks, acessibilidade global, serviço ou compatibilidade sensível.

| Recurso | Grupo | Windows 10/11 | Complexidade | Carga esperada | Privilégio | Risco/atualização | Reversão recomendada |
|---|---|---|---|---|---|---|---|
| Temas do app ZEUS (paleta, densidade, formas) | 1 Direto | Ambos; conferir contraste alto | Baixa | Baixa se estático | Nenhum | Baixo | Guardar escolha e restaurar um preset |
| Wallpaper por monitor/ajuste de enquadramento | 1 Direto via API pública | Ambos | Média | Imagem estática baixa | Usuário | Médio: monitor/slideshow/diretivas podem variar | Capturar por monitor e validar/reverter |
| Clock e widgets ZEUS | 1 Direto | Ambos | Baixa/média | Baixa se atualização espaçada | Nenhum | Baixo; multi-DPI/monitor | Desativar widget e restaurar coordenadas/visibilidade |
| Cores globais e transparência via Configurações | 1 Direto como acesso guiado | Ambos, UI difere | Baixa | Nenhuma | Nenhum | Baixo | Abrir Configurações; deixa claro que usuário aplica |
| Aplicar cores/accent global por alterações internas | 4 Não recomendado | Build e políticas variam | Média | Nenhuma | Pode exigir contexto | Alto/sem contrato estável | Evitar. Usar Configurações oficiais |
| Reposicionar barra/Iniciar, trocar shell | 3 Avançado | Comportamento varia por build; Win10 suporte encerrado para Home/Pro em 14/10/2025 | Muito alta | Moderada | Pode envolver admin/injeção | Muito alto | Não incluir no catalog inicial; preservar ponto de recuperação externo não é garantia suficiente |
| FancyZones via PowerToys | 2 Integração externa | Windows 10/11 conforme PowerToys | Baixa para atalho; alta para integração | Baixa/média | Admin somente para alvos elevados | Baixo/médio | Não tocar nos arquivos internos; desativar no PowerToys |
| Taskbar via TranslucentTB | 2 Externo | Windows 10/11, limites por versão | Baixa como tutorial | Baixa declarada pelo autor | Instalação normal; startup pode induzir mudanças de política | Médio/alto após updates/licença GPL | Desinstalar pelo produto; ZEUS só informa e nunca promete rollback próprio |
| Windhawk mods / ExplorerPatcher | 4 Apenas referência avançada | A dependência é build do Explorer | Muito alta | Varia | Pode implicar injeção/admin | Muito alto, especialmente atualização/segurança | Não disponibilizar “um clique”; desinstalação e recuperação não devem exigir desligar proteções |
| Rainmeter | 2 Externo | Windows conforme Rainmeter | Baixa para link | Varia por skin; rede possível | Sem admin em uso comum | Médio devido scripts de skin | Usar Rainmeter manager; auditar pacote e tratar como código de terceiros |
| Wallpaper animado | 2 Externo, futuro | Windows compatível com renderer | Média | GPU/CPU variáveis; fullscreen pause | Instalação de terceiros | Médio/alto para jogos, bateria e plugins | Pausar e desinstalar ferramenta; medir antes/depois |
| Animação WPF dentro do ZEUS | 1 Direto | Aplicativo .NET para Windows | Baixa | Baixa se breve | Nenhum | Baixo | Setting de movimento reduzido / instantâneo |
| Composição WinUI dentro do WPF | 1 Direto avançado | Versionar SDK/runtime e fallback | Média/alta | Pode ser eficiente, mas precisa medir | Nenhum | Médio (deployment, GPU, backdrop) | Desligar efeito e voltar ao WPF sólido |
| Dock próprio com magnificação | 3 Avançado | Ambos, monitores e DPI | Alta | Pode ser constante se mal feito | Nenhum se janela normal | Médio (z-order, acessibilidade, foco, gravação de tela) | Processo separado opcional, fechar e apagar preferências do widget |

**Windows 10:** desde 14 de outubro de 2025, Windows 10 Home/Pro não recebe o suporte normal de segurança; consumidores elegíveis podem usar ESU temporário até 12/10/2027. O ZEUS deve mostrar versão/edição e suporte separadamente. Não use “compatível com Windows 10” como sinônimo de “seguro/atualizado”.[Microsoft: fim do suporte ao Windows 10](https://support.microsoft.com/en-us/windows/deployment/updates-lifecycle/windows-10-support-has-ended-on-october-14-2025)

## E. Biblioteca inicial de layouts ZEUS

Os nomes são identidades visuais feitas com recursos licenciados/autorais. As entradas abaixo não significam que o ZEUS alterará taskbar, shell, cursor ou janela de outros programas.

| Layout | Elementos incluídos no app | Elementos globais | Referência e limite |
|---|---|---|---|
| Windows Moderno | Paleta clara/escura, hierarquia Fluent, superfícies sólidas | Cores oficiais somente pelo Settings | Mica é referência para aplicativo Windows 11, não um skin do desktop todo. |
| Mac Inspirado / Aurora | Dock/painel próprio futuro, ícones próprios licenciados, gradiente/paleta | Wallpaper; atalhos para Dock/tarefa Windows | Não copiar ícones, sons ou marca Apple. Animação de dock seria janela ZEUS própria. |
| Plasma Inspirado | Painéis/cards, widgets móveis, densidade configurável | Wallpaper e atalhos | Global Themes KDE mudam componentes do ambiente KDE; não importam no Windows. |
| Minimalista | Baixo ruído, tipografia legível, menos cards/efeitos | Wallpaper/cor opcional | Reduz movimento e evita widgets sempre animados. |
| Gamer Neon | Acento neon, contraste moderado, indicador de sessão/foco | Wallpaper estático, talvez overlay ZEUS desligável | Não fazer overlay em fullscreen/anti-cheat; sem alegar boost de FPS. |
| Cyberpunk | Paleta teal/magenta, elementos de HUD dentro do ZEUS | Wallpaper escolhido pelo usuário | Arte original/licenciada; painel não desenha por cima de jogos por padrão. |
| Vidro/Acrylic | Cartões internos semitransparentes com fallback | Transparência global fica em Settings | Não usar blur de alta área como base universal; contraste/energia/RDP podem forçar sólido. |
| Produtividade | Painel de tarefas do app, atalhos para PowerToys/FancyZones | Layout real de janelas apenas por ferramenta externa escolhida | Preview do layout ajuda; aplicação em app externo é integração separada e explicitamente consentida. |
| Terminal/Hacker | Monoespaçada e painel técnico no ZEUS | Abrir Terminal com link do Windows | Não executar comandos ou scripts vindos do tema. |
| Retro | Paleta, bordas quadradas e fonte licenciada | Sons/wallpaper somente em operação separada | Sem pacotes de tema do sistema ou substituição de shell nesta fase. |
| Monocromático | Escala de cinza própria e foco | Windows contrast themes via Settings | Atender WCAG/contraste e não comunicar estado somente por cor. |
| Personalização completa | Editor de paleta, densidade e componentes internos | Uma lista, componente por componente, do que será aberto/modificado | Toda opção precisa de origem, suporte, risco, preview, aplicação e restauração próprios. |

## F. Arquitetura recomendada para catálogo de temas

1. **Manifesto versionado:** ID estável, nome, autoria, descrição, versão do esquema, alvo Windows e imagens/recursos licenciados.
2. **Capabilities por componente:** app theme, wallpaper, widget, cor global via Settings, atalho oficial. Registrar `supported`, `external`, `manual`, `experimental`, `unsupported` e motivo; não um booleano genérico “compatível”.
3. **Recursos isolados:** diretório do app em `%LocalAppData%` para tema ativo/cache; DB guarda manifesto efetivo, IDs, paths e estado; assets baixados ficam em staging e têm hash/assinatura/origem antes de importação.
4. **Prévia sem efeitos colaterais:** renderizar miniaturas no ZEUS sem chamar APIs do Windows. Preview inclui estados de alto contraste, escala 100–200%, 16:9/ultrawide, claro/escuro e redução de movimento.
5. **Plano antes de aplicar:** apresentar a lista concreta (“muda paleta do ZEUS”; “define wallpaper no monitor X”; “abre Configurações”) com estado anterior, reversibilidade e requisitos. Atalhos de Configurações nunca contam como aplicação confirmada.
6. **Transação por componente:** `CAPTURAR → PREVIEW → CONSENTIR → APLICAR → VERIFICAR → REGISTRAR → REVERTER`. Uma falha em wallpaper não deve deixar o tema do ZEUS parcialmente marcado como aplicado.
7. **Limites do pacote `.exe`:** manter caminho primário instalável/portável do ZEUS sem privilégios para temas próprios. Bibliotecas da Microsoft podem vir via runtime/NuGet conforme licença. Ferramentas GPL/AGPL externas são abertas por escolha do usuário, não embutidas sem análise jurídica/licença. Gerar SBOM e hash do instalador.
8. **Não instalar código de tema:** manifesto aceita dados e imagens verificados; proibir scripts, DLL, XAML arbitrário ou comandos em tema comunitário. Para conteúdo externo, armazenar licença, autor, URL, hash e capacidades declaradas.
9. **Compatibilidade do sistema:** identificar edição/build, high contrast, Battery Saver, Remote Desktop, DPI, quantidade de monitores e políticas. Se desconhecido, marcar não verificado; usar sólido como fallback.
10. **Histórico/reversão:** persistir componente alterado, before/after por monitor, consent receipt, resultado verificado e método de restore; “rollback não disponível” deve ser explícito nos componentes manuais.

## G. Plano de implementação em ordem

1. **Base de segurança e distribuição:** decidir a licença open source do ZEUS, avisos/SBOM e padrão do `.exe`; política de temas segura (sem script, sem downloads implícitos).
2. **Expandir somente a aparência do ZEUS:** paletas autorais com preview, densidade, preferências de animação, respeitar parâmetros de acessibilidade e modo de alto contraste. Baixo risco e usa WPF já instalado.
3. **Organizador de perfis:** separar os temas atuais dos perfis globais; cada componente exibe claramente o que o ZEUS controla, abre ou apenas recomenda.
4. **Wallpaper robusto:** completar captura por monitor, posição/slideshow, verificação de caminho e rollback. A API `IDesktopWallpaper` é fonte candidata; comparar com serviço já presente antes de trocar implementação.
5. **Widgets locais:** evoluir clock/monitor próprios com posição multi-monitor, DPI e pausa; permitir opt-in de inicialização só após comportamento de fechar/reabrir bem testado. Evitar overlay em fullscreen/jogos e não coletar métricas além da sessão pedida.
6. **Atalhos de produtividade externos:** PowerToys/FancyZones, Lively e Rainmeter como cards informativos com site oficial, licença, status e abrir/fechar. ZEUS não instala nem escreve configurações alheias automaticamente.
7. **Integrações de shell somente por estudo separado:** qualquer mod de Explorer exige ameaça/compatibilidade/licença/recovery review, matriz de versões e recuperação sem desativar Defender/Smart App Control. A evidência atual recomenda deixar fora da release de usuário comum.
8. **Medir e liberar:** snapshot antes/depois de CPU/GPU/RAM/VRAM/fps quando disponível, detectar fullscreen/jogos/OBS e pausar wallpapers/overlays; testar cada recurso em Windows 11, monitor único/múltiplos e high contrast; CI de build/installer e smoke de desinstalação.

## H. Próximos passos recomendados

1. Decidir e registrar qual licença será usada para o ZEUS e qual nome/identidade dos primeiros quatro layouts.
2. Montar o manifesto/catálogo como recurso somente de dados para: `Windows Moderno`, `Minimalista`, `Produtividade` e `Aurora`.
3. Definir a matriz de capabilities e a tela “O que este perfil muda” antes de tocar em qualquer outra parte do sistema.
4. Ampliar a reversibilidade do wallpaper multi-monitor e accessibility/reduced-motion em WPF.
5. Colocar Windhawk, ExplorerPatcher, Seelen UI, Rainmeter e Lively numa seção “referências/ferramentas externas” com risco/licença, nunca como botões de “otimizar agora”.

### Fatos confirmados versus inferência

- **Confirmado:** ZEUS no checkout usa WPF e `net10.0-windows`; tem três paletas internas, atalhos para Settings de temas/cores/taskbar, preferências de animação/transparência, wallpaper e clock desktop opcional.
- **Confirmado:** Microsoft documenta APIs de wallpaper, certos aspectos de janelas virtuais, composição e capacidades suportadas de taskbar; materiais podem ter fallback por plataforma/energia.
- **Confirmado:** Windhawk styling guides dependem de Windhawk; ExplorerPatcher descreve riscos de compatibilidade de shell; ferramentas e mods têm licenças distintas.
- **Confirmado:** Windows 10 Home/Pro normal atingiu fim de suporte em 14/10/2025; ESU para consumidores é um programa separado e temporário.
- **Inferência de projeto:** para o objetivo de “central de personalização confiável”, layouts ZEUS compostos por app theme + wallpaper + widget próprio dão o melhor equilíbrio entre valor, reversão e segurança. Essa é uma recomendação, não um resultado medido em todos os PCs.
- **Não verificado:** playback completo dos vídeos no navegador de pesquisa, FPS/carga de cada tema externo em hardware real, suporte a cada build/edição do Windows, e licença do projeto ZEUS porque o checkout observado não tem `LICENSE` na raiz.
