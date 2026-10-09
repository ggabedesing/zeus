# Pesquisa aplicada — personalização visual do ZEUS para Windows

**Coleta:** 9 de outubro de 2026. **Escopo:** pesquisa e arquitetura; nenhum ajuste visual foi aplicado ao computador durante esta coleta.

## Resumo executivo

A comunidade realmente personaliza Windows com três camadas diferentes: (1) recursos suportados pelo próprio Windows e APIs documentadas; (2) janelas/apps próprios, widgets e docks separados; (3) alterações injetadas no Explorer, Start ou outros processos do sistema. Essas camadas não devem aparecer como se fossem equivalentes num catálogo ZEUS.

O melhor caminho para uma central confiável é começar com pacotes visuais próprios, declarativos e reversíveis: wallpaper por monitor, cores, efeitos/acessibilidade suportados, relógio/widget ZEUS e organização de janelas via API pública ou integração opcional com PowerToys. Dock, taskbar e Start substitutos precisam ser rotulados como integração de terceiro e compatibilidade por build. Patch de shell, tema de sistema não assinado e injeção ficam fora do modo comum.

**Estado atual encontrado no repositório:** há catálogo de perfis JSON com versão de esquema, prévia/restauração de wallpaper e efeitos selecionados, temas e cor para a interface ZEUS, layouts personalizados no escopo `zeus-ui`, e relógio de desktop configurável. A documentação do catálogo confirma `scope: zeus-ui`; portanto “macOS inspirado”, “Aurora” etc. não significam que a barra/Start do Windows já foram alterados. A pesquisa não altera esse escopo.

## A. Galeria global e B. 30 achados verificáveis

Cada item é uma referência real ou um recurso documentado, com seu uso potencial para o ZEUS.

| # | Achado observado | Evidência e leitura para o ZEUS |
|---:|---|---|
| 1 | FancyZones: layouts em zonas e prévia por monitor | [Documentação Microsoft](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones). Organização de janelas é viável como integração opcional; não é tema de sistema. |
| 2 | Workspaces captura e abre conjuntos de apps em posições | [Documentação Microsoft](https://learn.microsoft.com/en-us/windows/powertoys/workspaces/). Bom modelo para espaços de Trabalho/Jogos/Streaming, informando que janelas podem saltar visualmente ao reposicionar. |
| 3 | Workspaces usa APIs públicas e motor FancyZones, mas não garante snapping nativo | [Limitações oficiais](https://learn.microsoft.com/en-us/windows/powertoys/workspaces/#frequently-asked-questions). Evitar prometer que qualquer layout será restaurado perfeitamente. |
| 4 | PowerToys é MIT e tem várias utilidades para Windows | [Repositório oficial](https://github.com/microsoft/PowerToys). É referência de integração e UX; registra telemetria diagnóstica básica, aspecto que o ZEUS deve expor antes de integrar. |
| 5 | TranslucentTB permite opacidade, Acrylic e estados dinâmicos da taskbar | [Repositório](https://github.com/TranslucentTB/TranslucentTB). É GPL-3.0; Windows 10/11, mas pacote portátil declarado só funciona no Windows 11. Candidato a integração externa opt-in, não código para copiar sem cumprir GPL. |
| 6 | Aero Dock combina dock independente, janelas ao vivo, magnificação, temas, widgets e modos por app | [Repositório e screenshots](https://github.com/TheAgencyMGE/aero-dock). MIT, Windows 10 1809+/11 x64, WebView2, sem elevação segundo README. É um projeto novo/pequeno (37 commits, 77 stars na coleta) e build sem assinatura; referência de capacidade, ainda precisa de validação independente antes de recomendação. |
| 7 | Rainmeter desenha skins/widgets independentes no desktop | [Site oficial](https://www.rainmeter.net/) e [documentação de skins](https://docs.rainmeter.net/manual/skins/). GPL-2.0, ativo; apto para inspiração de relógio, visualizador e HUD, mas cada skin de terceiro tem licença e riscos próprios. |
| 8 | Rainmeter não muda o estilo visual global do Windows | [Primeiros passos oficiais](https://docs.rainmeter.net/manual/getting-started/). Excelente fronteira para explicar widget versus shell. |
| 9 | Lively aplica papéis de parede animados e screensavers | [Repositório oficial](https://github.com/lively-community/lively). GPL-3.0, WinUI 3, oferece instalador para Windows 10+; integração deve pausar sob tela cheia/bateria e medir GPU/VRAM, sem prometer impacto zero. |
| 10 | KDE Global Themes agrupa layout, painel, cores, estilo, decoração, ícones, cursor, wallpaper, login e lock screen | [Manual KDE](https://docs.kde.org/stable_kf6/en/plasma-workspace/kcontrol/lookandfeel/). Modelo forte de metadados de tema, mas esses componentes pertencem ao Plasma e não podem ser aplicados diretamente no Windows. |
| 11 | KDE permite visualizar e escolher componentes de um tema antes de aplicar | [Manual KDE, prévia e seleção](https://docs.kde.org/stable_kf6/en/plasma-workspace/kcontrol/lookandfeel/). ZEUS deve permitir aplicar só componentes compatíveis em vez de uma transformação monolítica. |
| 12 | KWin separa efeitos de janelas, acessibilidade, foco, workspaces e abrir/fechar | [Manual de efeitos](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/kwineffects/). Referência para catálogo de movimento; execução do KWin é Linux-only. |
| 13 | Hyprland define animações por classe/escopo, duração e curvas Bézier/spring | [Wiki oficial](https://wiki.hypr.land/configuring/core/animations/). Inspiração para editor de presets de animação dentro do ZEUS, sem mexer nas animações globais da shell. |
| 14 | Animações contínuas em Hyprland podem aumentar uso de CPU/GPU e bateria | [Aviso oficial da wiki](https://wiki.hypr.land/configuring/core/animations/). Não oferecer loop decorativo ligado por padrão; testar energia e movimento reduzido. |
| 15 | GNOME Extensions modifica shell, gerenciamento de janelas e lançador | [Admin Guide GNOME](https://help.gnome.org/system-admin-guide/extensions.html). Conceito exclusivo de GNOME, com risco de compatibilidade entre versões de extensão e shell. |
| 16 | Vídeo de KDE Plasma 6.6 demonstra painéis, widgets, temas, wallpaper de vídeo, efeitos e restauração | [“KDE Plasma 6.6 Customization Made Easy”](https://www.youtube.com/watch?v=XV0jdHwUjpI), SuperUser Tech, 14/03/2026. Timestamps da descrição: painéis 2:03; widgets 12:16; temas 15:29; vídeo wallpaper 21:02; animações/efeitos 23:23; restaurar 26:39. Demonstra Linux; observar fluxo segmentado e reversão. |
| 17 | Vídeo Windows macOS-like combina Rainmeter, Nexus Dock, ícones e widgets | [“macOS Style Desktop Customization Using Rainmeter & Nexus Dock”](https://www.youtube.com/watch?v=0emZidi__k4), The Tech Vegas, 24/02/2023. O próprio vídeo cita ferramentas de terceiros; timestamps/transcrição não foram verificados, então não atribuo cada passo a um tempo específico. |
| 18 | Vídeo de GNOME 50 apresenta extensões para relógio, painel lateral e grade de apps | [“GNOME Desktop Customisation | Best GNOME Extensions”](https://www.youtube.com/watch?v=fjOwZ_3G2j4), NH Soft, 03/06/2026. Descrição fornece timestamps: relógio 0:53; Raven 3:02; grade vertical 5:47. Ideia: biblioteca de componentes opcionais e testados por versão. |
| 19 | DWM compõe as janelas e fornece efeitos de vidro/transições do sistema | [Visão geral Microsoft](https://learn.microsoft.com/en-us/windows/win32/dwm/dwm-overview). DWM é infraestrutura do Windows; não é um motor de skins global que o ZEUS possa controlar livremente. |
| 20 | WinUI Composition oferece animações e efeitos na árvore visual da aplicação | [Animações](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-animation) e [efeitos](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects). Aplicável às transições dentro do ZEUS; não anima Start/Explorer arbitrários. |
| 21 | Visual layer executa gráficos, efeitos e animações aceleradas em camada própria | [Visual layer overview](https://learn.microsoft.com/en-us/windows/apps/develop/composition/visual-layer). Estudar para uma futura camada visual WinUI é uma decisão de arquitetura; o desktop atual é WPF e não deve ser reescrito por impulso. |
| 22 | Apps devem respeitar animações e efeitos avançados desativados pelo usuário | [Microsoft: composition tailoring](https://learn.microsoft.com/en-us/windows/uwp/composition/composition-tailoring). Movimento reduzido e transparência precisam de fallback legível em todos os temas. |
| 23 | A documentação de UX recomenda animações curtas, úteis e interrompíveis | [Microsoft: animations and transitions](https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-animations). Evitar movimento decorativo, flashes e informação transmitida só por animação. |
| 24 | IDesktopWallpaper é API Win32 documentada para wallpaper, slideshow, posição e vários monitores | [Interface Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper). Esta é a base técnica apropriada para crescer o módulo já existente; exige tratar retorno parcial e topology changes. |
| 25 | API do wallpaper existe desde Windows 8 em apps desktop | [Requisitos](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper#requirements). Compatível com o mínimo Windows 11 do produto e oferece caminho em Windows 10 se esse suporte voltar ao escopo. |
| 26 | Windows documenta integração da janela com a taskbar, ícones e notificações | [Taskbar Win32](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar). Nas fontes consultadas, a API documentada cobre integração da aplicação na barra, não sua substituição geral. Inferência prática: o ZEUS pode desenhar dock/janela própria sem se injetar no shell. |
| 27 | Open-Shell fornece menu Iniciar substituto e ajustes de Explorer | [Wiki](https://github.com/Open-Shell/Open-Shell-Menu/wiki/) e [licença MIT](https://github.com/Open-Shell/Open-Shell-Menu/blob/master/LICENSE). Projeto separado; instruções próprias de desinstalação e compatibilidade precisam de validação por build. Integração visual permanece externa/opt-in. |
| 28 | ExplorerPatcher altera taskbar, Start e Alt+Tab, mas fecha/reinicia Explorer e tem diferenças por build | [Repositório](https://github.com/valinet/ExplorerPatcher) e [releases/compatibilidade](https://github.com/valinet/ExplorerPatcher/releases). GPL-2.0. Não recomendado como ação padrão: a própria release registra que builds de Windows podem quebrar Explorer/Start. |
| 29 | Mica For Everyone pede materiais DWM a apps Win32 suportados, mas é ferramenta externa de regras | [Repositório oficial](https://github.com/MicaForEveryone/MicaForEveryone). A própria página alerta contra site impostor e limita o efeito a apps suportados; tratar como referência/integração manual, nunca baixar por mirror. |
| 30 | KDE Windows Modern empacota tema escuro/claro, ícones, decoração, painel, wallpapers e applets | [Repositório do tema](https://github.com/Jeysef/KDE-Windows-Modern). Referência visual verificável para coesão de um pacote inteiro, aplicável ao KDE Plasma 6, não Windows. |

**Referências em outros idiomas:** o vídeo brasileiro [“Como Personalizar Windows 11! INCRÍVEL e Minimalista”](https://www.youtube.com/watch?v=UTVZqBXZNtA), Dani Hardware, 14/10/2022, cita Taskbar, Rainmeter, Droptop Four e JaxCore; é um tutorial histórico, então versões/links citados precisam ser rechecados antes de recomendação. O vídeo em português [“COMO deixar o DESKTOP BONITO e MINIMALISTA no WINDOWS 10/11”](https://www.youtube.com/watch?v=Bwu2GCIZ_lQ) é outra referência para desktop limpo e organização. Em espanhol, [“Tema Gotas de Jade”](https://setup-plus.com/tema-gotas-de-jade/) mostra wallpaper animado, widgets Rainmeter, transparência e dock Nexus; a página contém um pack comunitário e menciona configuração de serviço meteorológico, então serve para observar o resultado, não para importar arquivos ou credenciais. Em francês, [“Personnaliser Windows 11 avec Windhawk et Rainmeter”](https://kyoshiro.fr/posts/personnaliser-windows-11-windhawk-rainmeter) separa a modificação da barra de tarefas (Windhawk) dos widgets (Rainmeter). São relatos/tutoriais, não fontes de suporte oficial.

Outra descoberta relevante, fora dos 30 por ser específica do Windows 11 e beta: [Taskbar Widgets](https://github.com/pfcdev/TaskbarWidgets) é MIT e instala widgets sem substituir o shell, mas declara depender de superfícies XAML privadas; o próprio projeto desliga a integração quando o layout atual não é suportado. Serve como estudo de isolamento do processo, permissões por extensão e fallback, não como integração pronta.

**Observação sobre imagens:** fontes KDE e PowerToys incorporam capturas/miniaturas diretamente nos manuais; os vídeos acima são demonstrações com movimento. Não incluí conceitos de Dribbble/Behance como implementações técnicas. Vídeos foram lidos pelas páginas e descrições acessíveis; não consegui validar visualmente cada frame. Timestamps são somente os que o autor listou na descrição.

## C. Catálogo de animações e decisão de implementação

| Interação | Referência | Viabilidade ZEUS |
|---|---|---|
| Abrir/recolher painel de configurações, expandir cards, trocar página/tema | WinUI Composition e guideline de animações Windows | Direta dentro da janela ZEUS; manter abaixo de ~200 ms quando feedback, respeitar alto contraste e movimento reduzido. O app continua WPF até uma decisão de migração baseada em prova. |
| Prévia de wallpaper/tema | KDE Global Themes e atual módulo ZEUS | Direta: transição só depois de captura de estado, botão cancelar sempre visível, compensação se aplicação parcial falhar. |
| Dock com magnificação, abrir apps e preview de janelas | macOS/KDE como referência; dock de terceiros no Windows | Aplicação avançada: janela própria, consumo/perda de foco/monitores/tela cheia. Prototipar separadamente, nunca substituir taskbar sem recuperação. |
| Abrir/minimizar/maximizar janelas do sistema, Start e Explorer | DWM controla composição global | Apenas integração com shell/terceiro hoje. ZEUS não deve declarar que animações próprias alteram estas transições globais. |
| Transparência e desfoque em superfícies do ZEUS | Composition effects / materiais Windows | Dentro da aplicação com fallback opaco quando efeitos avançados estão desligados. Aplicar efeito global exige terceiros e não é API geral documentada. |
| Wallpaper ao vivo | Lively | Somente integração externa opt-in. Estimar e mostrar consumo; pausa no fullscreen/energia limitada; rollback restaura o wallpaper anterior, não necessariamente estado interno de player externo. |
| Indicador de progresso/atividade | Diretrizes Windows | Animação transmite estado real, com texto/percentual equivalentes; não simular progresso. |

## D. Catálogo de ferramentas e licenças

| Ferramenta | Licença/compatibilidade verificada | Uso e ressalva |
|---|---|---|
| [PowerToys](https://github.com/microsoft/PowerToys) | MIT; Windows 10/11, x64/ARM64 conforme versão | FancyZones e Workspaces são referências de menor risco; integração separada, consentida. Repositório divulga diagnóstico/telemetria básica. |
| [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) | GPL-3.0; Windows 10/11, build portátil só Win11 segundo README | Referência para cor/transparência da taskbar; não redistribuir/modificar código sem revisar obrigações GPL. Usa pontos não documentados (repositório marcado `undocumented`). |
| [Rainmeter](https://github.com/rainmeter/rainmeter) | GPL-2.0; projeto ativo; site declara Windows 7–11 | Melhor referência para skins/widgets e layouts importáveis. Skins têm licenças individuais e podem executar scripts/chamar dados externos; tratar skin como conteúdo não confiável. |
| [Lively Wallpaper](https://github.com/lively-community/lively) | GPL-3.0; Windows 10+ (requisitos variam por versão) | Melhor referência e integração opcional para wallpaper animado. Não incorporar no pacote ZEUS sem plano de licença, dependências e atualização. |
| [Windhawk](https://github.com/ramensoftware/windhawk) / [mods oficiais](https://github.com/ramensoftware/windhawk-mods) | Aplicativo GPL-3.0; licença de mod é individual (MIT quando ausente no catálogo oficial conforme README) | Acesso poderoso a Explorer/Start/taskbar por hooking/injeção. Para catálogo de temas, link externo com risco/build/licença explícitos; não baixar/instalar/aplicar automaticamente. |
| [Mica For Everyone](https://github.com/MicaForEveryone/MicaForEveryone) | MIT; foco em Windows 11 | Aplicação externa de regras DWM em apps Win32; não equivale a API estável universal. O repo adverte sobre site falso; só apontar para fonte oficial (GitHub/Store). |
| [Open-Shell](https://github.com/Open-Shell/Open-Shell-Menu) | MIT; Windows moderno, mas validar versão e comportamento | Menu substituto e extensões Explorer; compatibilidade e desinstalação/reinício merecem alerta. Não recomendado para aplicação silenciosa. |
| [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) | GPL-2.0; suporte por builds listadas | Extensa alteração do shell, com elevação e encerramento do Explorer. Pesquisa técnica apenas; não integrar em modo seguro. |
| [Aero Dock](https://github.com/TheAgencyMGE/aero-dock) | MIT; Windows 10 1809+/11 x64; requer WebView2 | Referência recente de dock e widgets próprios; projeto pequeno, unsigned, sem elevação segundo README. Validar origem, release e comportamento antes de recomendar ou distribuir. |
| [Taskbar Widgets](https://github.com/pfcdev/TaskbarWidgets) | MIT; somente Windows 11 x64; beta | Widgets sem substituir o shell, porém com dependência declarada de XAML privado. O próprio app desativa integração incompatível; usar como laboratório/referência, não como dependência de produção. |
| [KDE Plasma](https://kde.org/plasma-desktop/) | Software livre; Linux | Referência de temas compostos, widgets, layout selecionável e reversão. Não é dependência do Windows ZEUS. |
| [GNOME Extensions](https://extensions.gnome.org/) | Licença varia por extensão; Linux/GNOME | Modelo de extensão modular e atualização por versão; não transferível para shell Windows. |
| [Hyprland](https://wiki.hypr.land/configuring/core/animations/) | Licença do projeto: BSD-3-Clause; Linux/Wayland | Referência para curvas, escopos e consumo mensurável de animações; não é runtime Windows. |

**Estado de manutenção:** verificado pelas páginas oficiais/repositórios durante a coleta. Rainmeter e TranslucentTB mostravam commits recentes; PowerToys, Lively, Windhawk e ExplorerPatcher publicavam atividade/releases. Para licenciar qualquer redistribuição, verificar o arquivo `LICENSE` e dependências na versão exata do artefato; nome do repositório não é parecer jurídico.

## E. Matriz de viabilidade

Escala: risco baixo/médio/alto de compatibilidade/recuperação; desempenho baixo/médio/alto consumo adicional.

| Função | Windows 10/11 | Esforço | Risco | Desempenho | Reversão | Elevação/reinício |
|---|---|---:|---:|---:|---|---|
| Temas e cor somente no ZEUS | Ambos | Baixo | Baixo | Baixo | Imediata via SQLite | Não |
| Wallpaper estático por monitor + posição | API desde Win8; ambos | Médio | Baixo-médio | Baixo | Capturar IDs, arquivos e posições; validar antes de restaurar | Normalmente não; sessão pode refletir alteração |
| Efeitos do Windows (animação/transparência) | APIs/versões variam | Médio | Médio (acessibilidade) | Pode melhorar/alterar carga | Captura tipada, diálogo de confirmação e re-leitura | Não necessariamente; refletir novo logon em certos efeitos |
| Relógio, cards e HUD como janelas do ZEUS | Ambos, conferir DPI/monitores | Médio | Médio (fullscreen/foco) | Médio e mensurável | Fechar/ocultar e restaurar perfil | Não |
| Presets de layout de janelas | Ambos, via PowerToys ou janela própria | Médio-alto | Médio | Baixo | Reaplicar geometria anterior | Não; integração pode exigir aplicativo instalado |
| Dock independente do ZEUS | Ambos | Alto | Médio-alto (taskbar/monitor/input) | Médio | Ocultar/fechar dock; nunca matar Explorer | Não |
| Taskbar/Start alternativos via terceiros | Build específico | Alto | Alto | Baixo-médio | Desinstalador + restauração documentada; pode exigir reboot | Frequentemente sim |
| Patches de tema de sistema / injection | Dependente de build | Muito alto | Alto | Incerto | Não garantir; recuperação pode exigir modo seguro | Sim/risco de sessão |
| Wallpaper animado externo | Win10 1903+ segundo Lively | Médio de integração | Médio | Médio-alto em GPU/memória/energia | Restaurar wallpaper; fechar ferramenta | Não, mas depende da ferramenta |
| Dock independente (Aero Dock é um exemplo atual) | Windows 10 1809+/11 x64 segundo README | Alto | Médio-alto (foco, DPI, taskbar e monitores) | Médio | Ocultar/fechar dock; não substituir Explorer | Sem admin segundo README; instalação requer WebView2 |
| Widgets dentro da taskbar via superfícies privadas | Windows 11 x64, beta | Alto | Alto e suscetível a atualização | Médio | Desabilitar/remover extensão; pode precisar reiniciar Explorer | Depende da implementação e layout |

## F. Biblioteca de layouts recomendados (componentes reais, não rótulos vazios)

Legenda: **ZEUS** = afeta app; **Windows suportado** = pode mudar via API testada; **Opcional externo** = ferramenta terceiro; **conceito** = somente apresentação no primeiro marco.

| Layout | Componentes a oferecer |
|---|---|
| Windows Moderno | ZEUS: tema claro/escuro, acento, movimento; Windows suportado: wallpaper/acento só onde API é confiável. |
| Minimalista | ZEUS: menos animação e HUD compacto; Windows: wallpaper neutro; sem remover componentes shell. |
| Produtividade | ZEUS: relógio discreto; integração PowerToys Workspaces/FancyZones opcional, não alegar gestão nativa do Start. |
| Aurora | ZEUS: gradientes, paleta e transições acessíveis; wallpaper escolhido pelo usuário. |
| Gamer Neon | ZEUS: alto contraste, HUD de hardware lido por fontes reais; sem polling agressivo e sem substituir taskbar. |
| Cyberpunk | ZEUS: paleta, wallpaper, alertas com texto e movimento reduzido; animações opcionais e limitadas. |
| macOS inspirado | ZEUS: dock/painel próprios somente após protótipo; wallpaper, ícones do ZEUS e relógio. Dock de terceiros é link opcional. Marcar barra/menu global como “não aplicado”. |
| KDE inspirado | ZEUS: painel lateral e zonas em app; layout global KDE é só referência Linux. |
| Vidro | ZEUS: material Acrylic/Mica na própria janela com fallback; Mica For Everyone aparece como integração de terceiro. |
| Retro/Amber | ZEUS: fonte/paleta/ícones próprios; não prometer tema clássico no Explorer. |
| Monocromático | ZEUS: cinza e ajustes de contraste; acessibilidade sempre prevalece. |
| Completo personalizado | Compositor do usuário escolhe componentes suportados individualmente; nunca instala `.msstyles`, scripts ou mods sem revisão. |

## G. Plano de implementação sugerido

1. **Ajustar o contrato do catálogo atual:** separar `scope` e compatibilidade em cada componente; renomear/descrição explícita de presets para dizer “aparência do ZEUS”; estados `suportado`, `opcional externo`, `experimental`, `conceito`.
2. **Inventariar operações visuais já existentes:** unificar wallpaper, efeitos, relógio e personalização em uma composição visual; incluir captura tipada por monitor, preview, aplicar/verificar/reverter e histórico de cada subcomponente.
3. **Completar wallpaper suportado:** separar wallpaper por monitor/slideshow e fallback; detectar alteração concorrente e não sobrescrever estado que mudou após o snapshot.
4. **Construir biblioteca de temas internos:** recursos estáticos versionados + tokens de cor/raio/espacamento/motion, prévia, acessibilidade AA, estado desconhecido e importação validada sem execução de script.
5. **Experiência de layout do usuário:** oferecer perfis de janelas via integração PowerToys já instalado, consultar licença/versão e jamais instalar de surpresa; também avaliar protótipo de dock próprio isolado.
6. **Animações próprias:** usar transições discretas na janela existente, observar movimento reduzido/alto contraste/energia e acrescentar teste de consumo e resposta ao foco/fullscreen.
7. **Integrações externas após segurança/licença:** pesquisa técnica e modelo de assinatura/atualização de pacotes; não embutir Windhawk, temas GPL ou skins de usuários sem cadeia de origem e verificação.
8. **Aceitação:** matriz Windows 10/11, single/multi monitor, DPI, sessão remota, fullscreen/jogos/OBS, UAC, rollback parcial e perfil corrompido; CI não substitui hardware real.

## H. Referências visuais para olhar

- KDE Global Theme tem prévia da miniatura e seleção de componentes no manual: [capturas e fluxo](https://docs.kde.org/stable_kf6/en/plasma-workspace/kcontrol/lookandfeel/).
- PowerToys Workspaces mostra captura, edição e status de abertura em imagens oficiais: [guia ilustrado](https://learn.microsoft.com/en-us/windows/powertoys/workspaces/).
- Windows com widgets/visualizer: [site Rainmeter](https://www.rainmeter.net/) e [Enigma suite](https://engard.me/projects/enigma/) (licença CC BY-NC-SA; não redistribuir como pacote comercial sem autorização compatível).
- Barra com transparência dinâmica: [capturas e recursos do TranslucentTB](https://github.com/TranslucentTB/TranslucentTB).
- Superfícies Mica em apps Win32: [capturas e limites no Mica For Everyone](https://github.com/MicaForEveryone/MicaForEveryone).
- Movimento real em desktops KDE/GNOME: vídeos #16 e #18 na galeria acima; a descrição fornece timestamps, mas a coleta não validou cada frame/transcrição.

## I. Arquitetura recomendada

O tema deve ser um manifesto declarativo assinado/versionado, não um script:

- `ThemeManifest`: `id`, versão de esquema/tema, autor, licença/atribuição, hashes, resumo, screenshots, compatibilidade de Windows/build, política de rede, dependências e canais de suporte.
- `Components[]`: `componentId`, escopo (`zeus-ui`, `wallpaper`, `user-preferences`, `external-integration`), estado de suporte, requisitos, valor pretendido, impacto esperado, reinício, acessibilidade, permissões e reversibilidade.
- `Preview`: renderização isolada com arquivos de usuário em leitura, sem gravar Registro/configuração; mostrar componentes ignorados e razão.
- `Apply`: consentimento por operação, bloquear concorrência, salvar snapshot tipado antes, aplicar em ordem de dependências, reler cada mudança e gravar auditoria. Sem PowerShell/scripts arbitrários dentro do tema.
- `Rollback`: desfazer somente o que ainda corresponde ao valor aplicado; se o usuário ou outro app mudou depois, marcar conflito e preservar o estado desconhecido. Compensar falha parcial com a captura imediatamente anterior.
- `Compatibility`: lista explícita `Windows 10/11 + builds`, arquitetura, monitores, DPI e dependência externa; sem inferir suporte por nome.
- `Package`: assinatura/hash da fonte, licença por arquivo, antivírus/SmartScreen tratado sem pedir exclusão, atualização com canal/rollback e pacote de diagnóstico sem dados pessoais por padrão.
- `Accessibility`: movimento reduzido, contraste alto, escala, esquema claro/escuro; animações não são o único canal de feedback.

## J. Primeiros passos recomendados

1. Corrigir agora a apresentação de nomenclatura/escopo: tema “macOS inspirado” deve informar claramente que muda a interface ZEUS e componentes Windows individualizados.
2. Consolidar prévia e reversão do wallpaper, que já têm API/superfície no produto e evidência de recuperação multi-monitor.
3. Criar um tema ZEUS completo realmente coeso (Aurora, minimalista e neon) usando os tokens/recursos existentes e teste visual, sem depender de hack de Explorer.
4. Prototipar integração de Workspaces com PowerToys instalado, só leitura/detecção primeiro; deixar criação/aplicação para após modelo de autorização e compatibilidade.
5. Tratar dock como projeto separado, instrumentando consumo, DPI, fullscreen, OBS, foco e recuperação antes de somar ao `.exe` principal.

## Fontes primárias consultadas

- [Microsoft PowerToys](https://github.com/microsoft/PowerToys), [FancyZones](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones), [Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces/).
- [Microsoft DWM](https://learn.microsoft.com/en-us/windows/win32/dwm/dwm-overview), [WinUI Composition](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-animation), [efeitos](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects), [UISettings](https://learn.microsoft.com/en-us/windows/uwp/composition/composition-tailoring), [IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper).
- [Rainmeter](https://github.com/rainmeter/rainmeter), [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB), [Lively](https://github.com/lively-community/lively), [Mica For Everyone](https://github.com/MicaForEveryone/MicaForEveryone), [Windhawk Mods](https://github.com/ramensoftware/windhawk-mods), [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher), [Open-Shell](https://github.com/Open-Shell/Open-Shell-Menu).
- [KDE Global Themes](https://docs.kde.org/stable_kf6/en/plasma-workspace/kcontrol/lookandfeel/), [KWin Effects](https://docs.kde.org/stable_kf6/en/kwin/kcontrol/kwineffects/), [GNOME Extensions](https://help.gnome.org/system-admin-guide/extensions.html), [Hyprland animations](https://wiki.hypr.land/configuring/core/animations/).

