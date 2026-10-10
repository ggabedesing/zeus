# Zeus PC — interface simples e funções reais

## Entrega

O frontend do ZIP `zeus-pc (1).zip` foi integrado ao motor Windows existente, preservando os arquivos fornecidos e o aplicativo WPF anterior. A navegação agora tem quatro páginas: Início, Meu PC, Aparência e Histórico. O Modo Técnico é opcional. Os números fictícios e os botões sem ação foram retirados do fluxo principal.

O aplicativo Electron inclui serviço .NET e coletor Observer próprios. Não requer instalação de Node.js/.NET para usar o pacote portátil. A API é local, autenticada e tem operações específicas; não oferece executor de shell. A política de conteúdo, o isolamento da página e a validação do IPC seguem as [orientações oficiais de segurança do Electron](https://www.electronjs.org/docs/latest/tutorial/security).

## Verificação local realizada

- Compilação do frontend: TypeScript e Vite passaram.
- Publicação do serviço e coletor: concluída para Windows x64, com runtime incluído.
- Inventário real: i9-9900, GTX 960, aproximadamente 32 GB de RAM e volume C: retornados pelo Windows.
- Desempenho: CPU e memória reais; dados indisponíveis e avisos continuam explícitos.
- Autenticação: chamadas sem token e com Origin de navegador rejeitadas com HTTP 401.
- Prévia: BMP 960×540 exibido; aplicação usa os mesmos bytes.
- Aplicação real: papel de parede temporário aplicado e verificado no monitor conectado.
- Repetição: prévia já consumida foi bloqueada.
- Reversão real: papel de parede/ajuste anteriores restaurados e verificados; journal marcado `restored`.
- Electron: navegação pelas quatro páginas, dados reais e imagem de prévia conferidos; nenhum erro de página na verificação.
- Revisão independente encontrou risco de fechamento durante mutação; foi corrigido. O app aguarda aplicação/restauração antes de encerrar o serviço.

Evidências locais: `artifacts/desktop-ui-api-results.json`, `artifacts/desktop-ui-electron-results.json`, `artifacts/desktop-ui-home.png`, `artifacts/desktop-ui-appearance.png`. O teste foi de uma tela/monitor conectado; não equivale a validar todas as configurações de monitores ou todas as fases do roadmap.

## Limites

Aparência aplica somente papel de parede estático. Dock, relógio sobre o desktop, widgets, animações globais e ferramentas externas ainda não estão integrados. Não há previsão de ganho de FPS ou declaração de saúde sem evidência. Temperaturas não são inventadas quando a fonte não existe.

A interface nova ainda não reúne todos os recursos administrativos, drivers, reparo e streaming do aplicativo anterior. O EXE é portátil e não possui assinatura digital. O diagnóstico inicial pode levar até 55 segundos e usa cache de inventário por cinco minutos; a amostragem de desempenho é atualizada separadamente.

Auditoria de dependências de produção retornou zero vulnerabilidades conhecidas. As ferramentas de desenvolvimento/empacotamento ainda têm avisos de auditoria, incluindo dependências do Tailwind 3 e electron-builder; uma migração dessas ferramentas precisa ser tratada separadamente, preservando o design e o build.

## Código e construção

- Interface: `src/Zeus.DesktopUI`.
- Serviço: `src/Zeus.LocalApi`.
- Construção: `scripts/build-desktop-ui.ps1`.
- Resultado: `src/Zeus.DesktopUI/release/Zeus-PC-0.2.0-Windows.exe`.
