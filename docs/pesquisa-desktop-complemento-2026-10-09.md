# Complemento da pesquisa de desktop — ZEUS

Consulta em 09/10/2026. Os relatórios existentes já cobrem 30 achados; este complemento registra lacunas verificadas. Nenhuma ferramenta foi instalada ou executada, nenhuma personalização aplicada. Este documento não declara implementadas as referências no EXE.

## Relógios, widgets e retrô

| Referência | Evidência consultada | Uso e limite para ZEUS |
| --- | --- | --- |
| [Zebar](https://github.com/glzr-io/zebar) | Widgets, barras e popups em webviews nativas; Windows/macOS/Linux; GPL-3.0. Último push em 26/09/2026. Windows pode exigir atualização de WebView2. | Relógio/calendário/painéis externos como integração opcional. Consumo e compatibilidade por build não medidos. |
| [RetroBar](https://github.com/dremin/RetroBar) | Barra alternativa com estilos Windows 95–Vista; declara Windows 10/11 e .NET Desktop Runtime. Apache-2.0; último push em 09/09/2026. | Referência real para Retrô no desktop. Validar tray, monitores, Explorer e recuperação antes de integrar. |
| [ElevenClock](https://github.com/marticliment/ElevenClock) | Relógio de taskbar do Windows 11 com múltiplos monitores; arquivado em 10/09/2025; último push em 05/09/2025. | Referência histórica, sem assumir manutenção atual. README declara Apache-2.0, mas [LICENSE atual](https://github.com/marticliment/ElevenClock/blob/main/LICENSE) contém GPL versão 3. Divergência não resolvida: não concluir direitos de redistribuição. |
| [TaskbarX](https://github.com/ChrisAnd1998/TaskbarX) | Centralização e opções de animação; MIT; arquivado em 12/06/2025; último push em 21/01/2024. | Referência histórica de movimento. Não assumir suporte a builds atuais. |

Datas e estado archived consultados na API dos repositórios: [Zebar](https://api.github.com/repos/glzr-io/zebar), [RetroBar](https://api.github.com/repos/dremin/RetroBar), [ElevenClock](https://api.github.com/repos/marticliment/ElevenClock), [TaskbarX](https://api.github.com/repos/ChrisAnd1998/TaskbarX). pushed_at indica atividade, sem comprovar estabilidade, suporte ou data de release.

## Vidro: mecanismos distintos

[ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) modifica especificamente Explorer. Instalação documentada registra DLL com administrador, exige reabrir janelas e oferece recuperação de crash. Último push em 13/05/2024; não arquivado ([API](https://api.github.com/repos/Maplespe/ExplorerBlurMica)). O repositório apresenta arquivos LGPL-3.0 e GPL-3.0: verificar componente antes de reutilizar. Classificação proposta: grupo 4, referência técnica.

[DWMBlurGlass](https://github.com/Maplespe/DWMBlurGlass) declara suporte desde Windows 10 2004. O método personalizado depende de engenharia reversa de DWM; também oferece SystemBackdrop com interfaces públicas. Não garante coexistência com MicaForEveryone. Último push em 26/03/2026 ([API](https://api.github.com/repos/Maplespe/DWMBlurGlass)). A existência de um modo público não torna todas as modificações oficialmente suportadas.

A API pública [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type) exige Windows 11 build 22621. Trata material de fundo de uma janela; não equivale a uma API de tema global para Explorer, Start e aplicativos arbitrários.

## Arquitetura e próximo passo

Inferência a partir das fontes: manter relógio/widgets próprios como caminho principal; estudar Zebar como integração externa e RetroBar como referência de perfil Retrô avançado. Metadados do catálogo devem conter archived, data da consulta, dependências e divergências de licença.

Separar por componente: janela/widget ZEUS; configuração oficial do Windows; programa externo; alteração de Explorer/DWM dependente de build. Prévia e reversão precisam descrever o mecanismo específico, sem prometer recuperação de programas externos ainda não ensaiados.

CPU/RAM/GPU, jogos/OBS, reversão e compatibilidade com a máquina do usuário permanecem não medidos. Vídeos completos não foram assistidos; não foram inventados timestamps nem efeitos observados. Consultas online e revisão dos relatórios existentes foram somente leitura; o complemento foi salvo pelo agente principal após a pesquisa.
