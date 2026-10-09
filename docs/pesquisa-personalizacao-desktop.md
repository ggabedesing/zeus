# Pesquisa de personalização visual do Windows para o ZEUS

**Atualizada em:** 9 de outubro de 2026  
**Escopo:** referências públicas verificáveis, arquitetura e prioridades para a central de personalização. Esta pesquisa não instala programas nem altera as configurações do Windows.

## Resumo executivo

Há três faixas de implementação que devem continuar separadas:

1. **Personalização do próprio ZEUS:** temas, cores, animações curtas, acessibilidade, relógio e prévias. É a faixa mais previsível e já existe no aplicativo.
2. **Preferências documentadas do Windows:** papel de parede, efeitos de animação/transparência, tema e atalhos para páginas oficiais. Cada configuração precisa de leitura anterior, confirmação, verificação e restauração.
3. **Substituição ou interceptação do shell:** docks, barras alternativas, Start alternativo, hooks e mods em processos do Explorer. São integrações opcionais de risco elevado, com compatibilidade dependente da build; não devem ser aplicadas silenciosamente pelo ZEUS.

Os projetos encontrados confirmam que desktops vistosos combinam wallpaper, widgets e alterações de shell. A conclusão de produto, porém, é priorizar wallpaper estático, relógio/widgets próprios do ZEUS, organização reversível e atalhos oficiais. Para temas de taskbar, Start, ícones e cursores, o ZEUS deve informar limites e abrir as configurações oficiais ou deixar o usuário escolher uma ferramenta externa conscientemente.

Não há base suficiente para preencher 30 achados independentes sem repetição ou extrapolação. Este documento lista **18 referências verificáveis** e registra quando uma licença, compatibilidade ou comportamento não foi confirmado.

## A. Galeria e achados verificados

| # | Referência | O que demonstra | Uso no ZEUS / limite |
|---|---|---|---|
| 1 | [Motion in Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/design/motion/) | Movimento responsivo, coerente e breve; exemplos de entrada, saída e transição de página. | Base para transições internas discretas. A documentação é de design de apps Windows, não uma API para animar a shell inteira. |
| 2 | [Otimização de animações e mídia — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-animations-and-media) | Diferença entre animações independentes e dependentes; animações infinitas mantêm CPU ativa. | Limitar duração, evitar animação contínua no painel e não animar propriedades de layout sem necessidade. A página trata principalmente de WinUI; o ZEUS usa WPF. |
| 3 | [Parâmetros de acessibilidade — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winauto/accessibility-parameters) | O Windows expõe preferências como alto contraste e animação da área cliente; apps devem reagir à mudança. | Preservar alto contraste e movimento reduzido. O aplicativo já observa alto contraste e usa movimento reduzido nas transições. |
| 4 | [Configurações do Windows 11 para desenvolvedores — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-windows-11) | Documenta algumas configurações e valores de taskbar/tema. | Não tratar a existência de um valor de Registro como contrato estável para escrever nele. Preferir APIs documentadas ou abrir Configurações. |
| 5 | [Wallpaper e temas no Windows 11 — Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/wallpaper-and-themes-windows-11) | Regras de design e provisionamento de papel de parede/tema em implantação. | Útil para conceitos e requisitos; o foco é customização/provisionamento OEM, não um contrato universal de API para um app de usuário. |
| 6 | [SystemParametersInfoW — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow) | API Win32 para parâmetros do sistema; documenta `SPI_SETDESKWALLPAPER` e seu retorno. | A chamada bem-sucedida não substitui leitura posterior do estado. Para monitores independentes, validar a implementação específica e a topologia, não presumir uma única imagem por monitor. |
| 7 | [Lively Wallpaper — código-fonte](https://github.com/rocksdanister/lively) | Wallpapers de vídeo, GIF, páginas e apps; múltiplos monitores e pausa em jogo/fullscreen segundo o projeto. Licença GPL-3.0. | Forte referência para catálogo e pausa adaptativa; integração/redistribuição exige cumprir GPL e revisar dependências. Não embutir como componente sem análise jurídica e técnica. |
| 8 | [Guia inicial do Lively](https://github.com/rocksdanister/lively/wiki/Getting-Started) | Requisitos publicados, armazenamento por usuário e ressalvas sobre alto contraste. | Inspira avisos de desempenho e compatibilidade; requisitos do projeto precisam ser revalidados antes de recomendar instalação. |
| 9 | [Rainmeter — repositório](https://github.com/rainmeter/rainmeter) | Widgets/sk skins de desktop; GPL-2.0. O projeto informa que não transmite dados por padrão, mas skins podem fazê-lo. | Referência de widgets/HUD e metadados de skin. Nunca importar skins sem mostrar origem, permissões/conteúdo e comportamento de rede. |
| 10 | [Manual de skins do Rainmeter](https://github.com/rainmeter/rainmeter-docs/blob/master/source/manual/skins/index.html) | Skin é janela móvel, dinâmica e às vezes interativa; pode carregar recursos/fontes. | Orienta o catálogo visual. Um pacote de skin é conteúdo executável em sentido amplo e precisa de revisão própria. |
| 11 | [Microsoft PowerToys](https://github.com/microsoft/PowerToys) | Conjunto ativo de utilitários, inclui FancyZones; licença MIT. O repositório declara coleta de telemetria básica. | Referência para zonas e produtividade, com transparência de telemetria. É integração recomendada como ferramenta externa, não dependência do núcleo. |
| 12 | [Instalar PowerToys — Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/install) | Canais oficiais de distribuição incluem GitHub, Store e gerenciadores de pacotes. | Padrão para apontar ao usuário a fonte oficial. Não instalar automaticamente sem consentimento. |
| 13 | [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) | Aparência transparente/translúcida da taskbar; GPL-3.0; o próprio projeto informa que alguns builds portáteis se limitam ao Windows 11 e inclui instruções de Registro para certos casos. | Apenas opção externa avançada. Dependência da shell e instruções de política/Registro são motivos para não aplicar ou redistribuir dentro do fluxo padrão. |
| 14 | [Windhawk](https://github.com/ramensoftware/windhawk) e [arquitetura dos mods](https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod) | Mods compilados para DLL e carregados nos processos-alvo; a arquitetura usa injeção/hooking global. | Demonstra customização profunda, mas aumenta risco de crash, incompatibilidade e superfície de ataque. Não executar mods pelo ZEUS nem tratar tema de terceiros como confiável. |
| 15 | [Catálogo oficial de mods Windhawk](https://github.com/ramensoftware/windhawk-mods) | Mods são submetidos e mantidos como arquivos independentes; política de licença depende do conteúdo e metadados. | Estudar estilos e limites; não presumir licença uniforme para cada mod. |
| 16 | [ExplorerPatcher: implementação da taskbar](https://github.com/valinet/ExplorerPatcher/wiki/ExplorerPatcher%27s-taskbar-implementation) e [releases](https://github.com/valinet/ExplorerPatcher/releases) | Reimplementa partes da taskbar e documenta dependência da versão do Windows; releases registram problemas de crashloop do Explorer em combinações específicas. | Alto risco operacional. Não integrar como opção de um clique; qualquer recomendação deve exigir versão do Windows, backup e procedimento de recuperação fora do shell. |
| 17 | [GlazeWM](https://github.com/glzr-io/glazewm) | Gerenciador de janelas tiling inspirado no i3, configuração YAML e suporte a múltiplos monitores; GPL-3.0. | Referência para layouts de produtividade e atalhos. Uma aplicação do tipo window manager altera interação global e precisa ser claramente opcional. |
| 18 | [Seelen UI](https://github.com/pt051111/seelen-ui) | Ambiente de desktop customizável para Windows, com barra, widgets, temas e gerenciador de janelas; oferece diferentes formatos de instalação. | Referência visual de experiência integrada; não substituir Explorer nem empacotar no ZEUS. Revisar licença, release, permissões e recovery antes de qualquer recomendação concreta. |

### Vídeos e demonstrações

1. [“We’ll be working with programs like Rainmeter, WindHawk, and Lively Wallpaper”](https://www.youtube.com/watch?v=cy5nSyrDcq4), canal Techno Man. A descrição identifica as ferramentas usadas em uma montagem de desktop. A página indexada não fornece timestamps confiáveis nem URLs diretas dos assets; por isso o vídeo serve como referência de composição, não como tutorial validado passo a passo.
2. [Demonstração de personalização Windows 11, Rainmeter e wallpapers](https://www.youtube.com/watch?v=EGmSWzAdfhE), canal Techno Man. A descrição indica etapas em 00:30, 01:18, 02:44 e 03:27, mas os links de downloads aparecem como placeholders na captura indexada. Não recomendamos baixar arquivos a partir dessa descrição sem verificar o canal e as fontes.
3. [Vídeo de taskbar alternativa com ExplorerPatcher](https://www.youtube.com/watch?v=IiAnpSBtlOs), canal Tech Enthusiast, publicado em 2024-08-02 segundo o resultado indexado. Demonstra mudança da taskbar; serve para observar o resultado visual. A própria natureza de substituição do shell e os avisos de compatibilidade tornam a abordagem inadequada ao caminho seguro padrão do ZEUS.

### Inspiração de outros ambientes

- [Efeitos de desktop do KDE Plasma](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/kwineffects/index.html): separa efeitos por acessibilidade, aparência, gerenciamento de janelas e animações de abrir/fechar.
- [Desktops virtuais do KDE Plasma](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/desktop/index.html): transições Slide, Cube e Fade são opções de compositor/ambiente Linux, não recursos transferíveis diretamente para Explorer.
- [Extensões GNOME Shell](https://wiki.gnome.org/Projects/GnomeShell/Extensions): extensão pode carregar JavaScript/CSS com poder sobre áreas do shell. É analogia de arquitetura extensível, mas também demonstra por que extensões do shell requerem confiança e compatibilidade rigorosas.

## B. Catálogo de animações e aplicação no produto

| Efeito observado/documentado | Tecnologia conhecida | Proposta ZEUS | Limite e acessibilidade |
|---|---|---|---|
| Transição curta entre páginas | WinUI fornece animações de transição; o ZEUS é WPF | Manter a transição existente breve e local ao conteúdo | Desligar se preferência do usuário ou Windows pedir redução de animação; não alegar animação nativa WinUI no WPF. |
| Entrada/saída de painel | WPF `DoubleAnimation`/transform, se usado sem relayout | Opacidade e deslocamento pequenos para painéis próprios | Evitar animação infinita ou atualizar cada frame. Confirmar `ClientAreaAnimation` e alto contraste. |
| Efeitos de abrir/fechar janelas do sistema | Compositor/shell Windows | Não tentar substituir; manter controles do Windows | Sem API pública geral que permita ao ZEUS trocar toda a animação do Explorer. |
| Cube/slide de desktop virtual | KWin/KDE compositor | Referência de linguagem visual; abrir Task View/configuração oficial | Não reproduzir alegando controlar o shell. Depende do KWin no Linux. |
| Ampliação de dock/ícones | Aplicativos de dock ou shell customizada | Estudo visual para um futuro launcher próprio, sem esconder taskbar | Não criar hotkey global nem captura de entrada sem design de segurança, acessibilidade e conflito de atalhos. |
| Fundo animado | Engine externa (Lively/Wallpaper Engine) | Mostrar integração opcional, botão para fonte oficial e pausa/impacto conforme ferramenta | CPU/GPU/RAM variam muito com vídeo, WebGL, monitor e conteúdo; medir no PC do usuário. |
| Indicador de progresso | Controles de progresso do próprio app | Aplicar a operações com duração real e estado explícito | Animação não pode substituir status textual acessível nem comunicar conclusão antes da confirmação. |

Microsoft recomenda movimento rápido, consistente e ligado à ação; a documentação de desempenho alerta contra propriedades que provocam layout e animações infinitas. Para WPF, a escolha concreta deve ser testada no runtime do produto em vez de presumir que a composição WinUI se aplica diretamente.

## C. Ferramentas: classificação e risco

| Ferramenta | Classe | Licença/estado observado | Compatibilidade / dependências | Desempenho / privilégio / reversão | Decisão para o ZEUS |
|---|---|---|---|---|---|
| APIs e Configurações oficiais do Windows | Grupo 1 | Documentação Microsoft | Varia por recurso e versão Windows 10/11 | Preferir por usuário; registrar snapshot; reverter só o que for escrito e verificável | Prioridade máxima; APIs documentadas quando existirem, senão link para Configurações. |
| Lively Wallpaper | Grupo 2 | GPL-3.0, repo ativo | Windows 10 1809+ (Store) ou 1903+ (installer) conforme wiki consultada; exige runtime/dependências no instalador | Renderização de vídeo/web pode usar RAM/GPU; pode pausar em fullscreen; preferência por usuário | Indicação externa com confirmação. Redistribuir/integrar exige análise da GPL e dependências. |
| Rainmeter | Grupo 2 | GPL-2.0; skin de terceiros tem risco próprio | Compatibilidade exata por release/skin não estabelecida nesta pesquisa | Widgets podem consultar rede e medir sensores; skin pode executar scripts/conteúdo | Recomendar fonte oficial; não instalar skins automaticamente. |
| PowerToys/FancyZones | Grupo 2 | MIT; repositório menciona telemetria básica | Windows 10/11; instalação por canais oficiais Microsoft/GitHub | Organização de janelas, não substituição do shell; rollback pelo próprio app | Integração documental opcional; evidenciar telemetria e canal oficial. |
| TranslucentTB | Grupo 2/3 | GPL-3.0; projeto documenta risco/configuração por versão | Alguns pacotes portáteis são somente Windows 11 | Depende da taskbar; algumas instruções mexem em política/Registro | Não aplicar configuração de Registro pelo ZEUS; link externo e aviso de compatibilidade. |
| Windhawk/mods | Grupo 3 | Código aberto; licença varia por mod | Hooks dependem de processos e builds Windows | Injeção em processos, risco de crash/segurança e regressões; rollback depende da ferramenta/mod | Não integrar ao núcleo; apenas referência técnica para modo pesquisador. |
| ExplorerPatcher | Grupo 3 | Código publicado; release/issues variam | Dependente da build do Windows, com regressões documentadas | Pode reiniciar/quebrar Explorer; recuperação potencialmente exige modo seguro/remoção | Não oferecer aplicação automatizada. |
| GlazeWM | Grupo 3 | GPL-3.0 | Windows/macOS conforme repo atual; requer processo de gerenciamento de janelas | Intercepta eventos globais/atalhos e altera layout; precisa de configuração e desligamento confiável | Referência opcional; validar conflitos de atalhos e acessibilidade antes de qualquer integração. |
| Seelen UI | Grupo 3 | Licença deve ser confirmada no arquivo do release escolhido | Windows 10/11 declarados pelo projeto; WebView runtime | Substitui/estende várias partes da experiência; custo e recuperação dependem de módulos habilitados | Referência visual, não dependência do ZEUS. |

**Atenção de licença:** licenças de repositório não autorizam copiar assets, skins, fontes, ícones, imagens de vídeo ou código sem revisar a licença específica de cada arquivo e os avisos de terceiros. O ZEUS deve usar criações próprias ou conteúdo com licença registrada.

## D. Matriz de viabilidade para o catálogo do ZEUS

Escala: esforço e risco Baixo/Médio/Alto. “Reversão” significa snapshot validado, não promessa genérica.

| Recurso | Grupo | Esforço | Risco/desempenho | Win 10/11 | Reversão e política |
|---|---:|---:|---|---|---|
| Temas/cor do próprio ZEUS | 1 | Baixo | Baixo; custo de renderização normal | Ambos | SQLite local; alto contraste prevalece. |
| Papel de parede estático | 1 | Médio | Baixo | Ambos; validar cada versão e monitor | Snapshot por monitor, conferir caminho/hash/topologia, consentir e só então aplicar. |
| Relógio/overlay do ZEUS | 1 | Médio | Baixo, mas janela sempre no topo pode cobrir app | Ambos | Preferências próprias persistidas; desligar/fechar e reset de posição. |
| Organização de ícones/arquivos de desktop | 1 | Médio | Risco de mover arquivo errado/OneDrive | Ambos | Prévia, excluir itens especiais, registrar hash, restaurar com detecção de conflito. |
| Atalho para tema, taskbar, Start, som e lock screen oficiais | 1 | Baixo | Baixo | Varia por URI e versão | Não altera sistema; explicar que ajuste permanece sob controle do Windows. |
| Reduzir animação/transparência | 1 | Médio | Baixo, mas pode exigir nova sessão | Ambos | Ler estado atual, revisão, guardar estado original, verificar leitura posterior e oferecer restauração. |
| Wallpaper animado | 2 | Médio | Médio/alto, conforme conteúdo e hardware | Mínimo depende da ferramenta | Aplicativo externo, confirmar fonte, medir por sessão e pausar ao jogar; não esconder custo. |
| Widget skin de terceiro | 2 | Médio | Pode ter rede/scripts e coletar conteúdo | Depende do skin/runtime | Não aplicar sem prévia de arquivos/licenças e explicação de comportamento. |
| Transparência da taskbar por utilitário externo | 2/3 | Médio | Dependência da shell e versão | Parcial/variável | Instrução externa, sem writes proprietários pelo ZEUS; caminho para desativar no app terceiro. |
| Dock/Start alternativo, hook de Explorer, mod de taskbar | 3 | Alto | Alto; compatibilidade pós-update imprevisível | Depende da build | Não recomendar como ação padrão; exigir cópia/recovery independente, teste de atualização e consentimento específico. |
| Efeitos Cube/macOS/Hyprland em todo Windows | 4 | Alto | Requer outro compositor/shell ou processo substituto | Não nativo | Apenas referência conceitual; não prometer reprodução pelo ZEUS. |

## E. Biblioteca de estilos proposta

Os perfis abaixo descrevem **o que o ZEUS pode aplicar**, não tudo o que uma imagem de inspiração mostra.

| Estilo | Incluído no ZEUS | Opcional com revisão | Apenas referência/fora do escopo seguro padrão |
|---|---|---|---|
| Windows moderno | Tema/cor do app, wallpaper estático pré-visualizado, relógio, organização da área de trabalho | FancyZones via PowerToys | Modificar Explorer/Start por injeção. |
| Minimalista | Tema compacto do app, menos elementos/overlay desligável, wallpaper estático e organização | Widgets Rainmeter escolhidos pelo usuário | Desativar serviço ou recurso de segurança para “limpar” o visual. |
| Aurora/macOS inspirado | Tema, paleta, relógio flutuante e ícones/organização sem mover executáveis | Dock externo escolhido manualmente | Barra superior global substituindo taskbar. |
| KDE/produtividade | Tema do app, zonas de janela como recomendação e atalhos oficiais | FancyZones/GlazeWM com consentimento e instalação independente | Efeitos compositor KWin aplicados ao Explorer. |
| Gamer Neon | Tema e cor do app; perfil de medição para jogo; pausa de animações do próprio ZEUS quando necessário | Wallpaper animado pausado em fullscreen, widgets com carga medida | HUD global sobre jogo, anti-cheat hooks ou mod de processo. |
| Cyberpunk/criador | Tema/cor, wallpaper estático, relógio e abas do ZEUS; plano de streaming | Rainmeter skin auditada pelo próprio usuário | Instalar pack sem licença/origem, ou iniciar engine em background sem opção. |
| Vidro e transparência | Efeito dentro do app, respeitando contraste | TranslucentTB sob responsabilidade do usuário | Alterar taskbar por Registry não documentado ou shell patch. |
| Retro/monocromático | Paletas e fontes do próprio ZEUS com pré-visualização | Packs externos cujas licenças sejam verificadas | Substituir arquivos de sistema, fontes globais ou recursos protegidos. |
| Multi-monitor | Relógio limitado ao retângulo virtual e wallpaper/preview com cada monitor identificado | Conteúdo independente por tela em app externo | Presumir que coordenadas, DPI ou topologia são iguais entre telas. |

## F. Arquitetura recomendada de temas

Cada perfil precisa de um manifesto versionado e declarativo; não de comandos arbitrários.

```json
{
  "schemaVersion": 1,
  "id": "zeus.minimal",
  "name": "Minimalista",
  "scope": "zeus-ui",
  "components": ["theme", "accent", "clock", "wallpaper-static"],
  "requirements": { "windows": ["10", "11"] },
  "assets": [],
  "preview": "preview.png",
  "license": { "spdx": "CC0-1.0", "attribution": [] }
}
```

Fluxo recomendado:

1. **Catalogar:** manifesto assinado/validado, versão de esquema, autor, licença por asset, hash, requisitos e escopo (`zeus-ui` ou `windows-user-setting`). Pacotes de script, DLL ou executável são rejeitados no catálogo de temas.
2. **Pré-visualizar:** renderizar imagem local e simular paleta no ZEUS. Não executar markup remoto, XAML, JavaScript, shell ou conteúdo dinâmico para produzir preview.
3. **Descobrir capacidades:** ler build do Windows, monitores/DPI, alto contraste, política gerenciada e estado atual. Dados desconhecidos desabilitam a ação correspondente e explicam por quê.
4. **Planejar:** mostrar campo, valor atual, valor proposto, fonte da operação, impacto/reinício e confiança; destacar componentes externos separados.
5. **Autorizar/aplicar:** ações numa lista allowlist, autorização por ação, sem elevação genérica. Fazer snapshot imediatamente antes de cada alteração, registrar etapa e chave de idempotência.
6. **Verificar:** consultar novamente estado pela mesma API ou fonte independente adequada; distinguir aplicado, divergente, pendente de sessão e não verificável.
7. **Reverter:** desfazer somente se o estado atual ainda corresponder ao valor aplicado pelo ZEUS e a identidade/topologia for igual à capturada. Conflito externo gera `REQUIRES_REVIEW`, não sobrescrita.
8. **Auditar/exportar:** histórico local, conteúdo do relatório visível, hashes e informações pessoais minimizados. Nada de telemetria por padrão.

### Modelo de dados sugerido

`ThemeManifest`, `ThemeAsset`, `CompatibilityRequirement`, `PreviewSnapshot`, `ApplyPlan`, `BeforeState`, `ApplyReceipt`, `VerificationResult` e `RollbackResult`, cada um versionado e registrado no SQLite. A camada `ThemeAdapter` deve ter implementação independente por componente: `ZeusAppearanceAdapter`, `WindowsWallpaperAdapter`, `WindowsVisualEffectsAdapter` e `ExternalToolLinkAdapter`. Uma falha de um componente não deve deixar os demais com estado declarado como concluído.

## G. Plano de implementação priorizado

1. **Fechar o catálogo interno:** consolidar os temas já existentes, preview no controle real, contraste, foco por teclado, movimento reduzido e teste de resolução/DPI.
2. **Completar o ciclo seguro de wallpaper:** verificar cada monitor, modos de ajuste, prévia, hashes e concorrência externa; preservar slideshow e detectar alterações durante a transação.
3. **Temas locais declarativos:** manifesto sem código, assets licenciados, versão de esquema, assinatura/hash e validação de contraste; instalar somente no escopo ZEUS.
4. **Personalização Windows oficial:** inventariar quais opções têm API documentada por build. Abrir a Configuração oficial para o restante; nunca escrever Registro apenas porque foi encontrado em blog/repo.
5. **Widgets próprios opcionais:** mostrar relógio/estado do sistema com frequência limitada, limites de monitor, pausa em fullscreen/bateria e leitura de desempenho observável.
6. **Integrações externas transparentes:** catálogo de links oficiais para PowerToys, Lively, Rainmeter, GlazeWM. Informar licença, versão, origem, telemetria declarada e passos do usuário; downloads e instalação só por fluxo explicitamente consentido.
7. **Deixar shell patching fora do caminho comum:** qualquer experimento Windhawk/ExplorerPatcher precisa de laboratório e mecanismo de recuperação comprovado em máquina virtual antes de se tornar uma recomendação do produto.

## H. Estado do ZEUS conferido nesta pesquisa

No checkout `zeus-dev`, branch `feat/zeus-windows-mvp`, já existem temas/cor da interface, movimento reduzido, alto contraste, relógio flutuante, prévia/aplicação de wallpaper e atalhos para várias páginas oficiais do Windows. O histórico do projeto também descreve organização reversível de arquivos comuns da Área de Trabalho.

O relógio recebeu nesta sequência uma correção para não ser puxado à área do monitor principal ao mudar de preferência. O teste unitário com retângulo multi-monitor simulado passou, e `scripts/publish-windows.ps1` abriu o `Zeus.Desktop.exe` local, verificou uma janela responsiva e o encerramento normal. Isso não comprova comportamento físico em monitores com DPI misto.

Na continuação da implementação, o catálogo passou a aceitar perfis JSON declarativos do usuário, limitados a temas/acento existentes no escopo `zeus-ui`; o arquivo não executa código nem carrega imagens. A validação rejeita propriedades desconhecidas, limita tamanho e quantidade, armazena no SQLite local e reutiliza prévia/confirmar/cancelar. Isso implementa a primeira versão de temas locais declarativos sem prometer cores arbitrárias, licenciamento de assets ou personalização do shell.

Na hora desta pesquisa, o workflow Windows do commit `fa4661cae1640a600dc56dfab50020c4462bf08b` estava `in_progress`; a execução anterior para `ce73aa89e3853182262142b9947829322104b406` estava `success`. Consultar novamente o workflow antes de usar a validação remota como evidência final.

## I. Lacunas e limites da pesquisa

- Nenhum benchmark independente, consistente e repetível foi coletado para consumo de CPU/GPU/RAM de cada ferramenta. Em wallpapers e skins, o custo depende do conteúdo; o ZEUS deve medir no PC e não propagar números de marketing como garantia.
- Os vídeos foram confirmados por páginas de resultado indexadas; não foi possível verificar a licença de cada asset, os links de download ou todos os timestamps assistindo a cada minuto. O relatório não endossa os pacotes dos autores.
- A pesquisa não confirma uma API pública e estável para reposicionar taskbar, substituir o Start ou fornecer efeitos de compositor global do Windows.
- Compatibilidade “Windows 10/11” declarada por um projeto não equivale a compatibilidade com cada build, edição, DPI, anti-cheat, política corporativa ou atualização cumulativa.
- A matriz é triagem de produto, não auditoria de segurança ou parecer jurídico de redistribuição.

## J. Fontes diretas adicionais

- [Windhawk: Taskbar Styler](https://windhawk.net/mods/windows-11-taskbar-styler)
- [Windhawk: Taskbar Clock Customization](https://windhawk.net/mods/taskbar-clock-customization)
- [PowerToys: página oficial](https://learn.microsoft.com/en-us/windows/powertoys/)
- [KDE Plasma: manual de efeitos](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/kwineffects/index.html)
- [GNOME Shell: extensões](https://wiki.gnome.org/Projects/GnomeShell/Extensions)
