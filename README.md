# ZEUS

Aplicativo Windows para diagnóstico, manutenção e otimização com dados reais, interface em português e revisão das alterações antes da execução.

**Estado: versão de desenvolvimento com módulos integrados; aceitação nativa acompanhada pelo GitHub Actions.** Os resultados de cada execução são registrados. Ganhos de desempenho precisam ser medidos no computador e na tarefa do usuário.

O [histórico de versões](CHANGELOG.md) registra as mudanças entregues. A versão exibida no aplicativo vem dos metadados incorporados ao executável; o pacote também informa a versão, o commit de origem e os hashes em `build-info.json`.

## Recursos

| Área | O que o aplicativo faz |
| --- | --- |
| Hardware | Lê CPU, RAM, GPU e drivers, placa-mãe, BIOS, módulos RAM e slots declarados pelo firmware, volumes, discos físicos, bateria e rede. Para discos, mostra estado, temperatura, desgaste e contadores de confiabilidade fornecidos pelo Windows (horas ligado e erros de leitura/gravação); campos ausentes continuam indisponíveis e não são tratados como zero. Esses campos não são uma certificação de saúde nem substituem a ferramenta do fabricante. Canais de memória não são deduzidos; sensores aparecem somente quando o Windows fornece a leitura. O inventário separa fabricante do dispositivo de fornecedor do driver quando o Windows reporta ambos. |
| Inventário do Windows | Consulta configuração de rede (IPs, DNS, gateways, rotas e valores de proxy do usuário no HKCU Internet Settings), drivers, PnP, processos, serviços, tarefas agendadas, programas instalados e eventos recentes; tenta ler Secure Boot e TPM. A tela de drivers exibe versão, data, signatário e estado de assinatura reportado por `Win32_PnPSignedDriver`; isso não é uma verificação independente do arquivo nem da cadeia de confiança. Estado de AutoDetect é informado somente quando existe no Registro; ausência não é interpretada como desativado. WinHTTP e configurações de proxy por aplicativo não são consultados. Endereços e nomes locais podem constar no relatório. Atualizações pendentes e saúde da imagem não são medidos durante a coleta. A aba de atualizações permite uma busca online explícita e somente leitura por atualizações de software pendentes. |
| Carga real | Observa CPU, RAM, commit/paginação e processos, engines de GPU com nome do processo quando o PID é mapeado, alocações de memória GPU dedicada/compartilhada por processo e adaptador, memória GPU agregada e capacidade dedicada, e tráfego/atividade local de disco e rede quando disponíveis. Alocações por processo não incluem o orçamento atribuído a esse processo e não confirmam pressão de VRAM. Quando uso e capacidade correspondem pelo LUID, exibe a ocupação agregada reportada e compara uso dedicado médio antes/depois com cobertura explícita. Compara também alocações GPU dedicadas por processo quando PID, horário de início e adaptador coincidem nos dois períodos; processos com identidade incompleta permanecem sem comparação. O Observador sinaliza ocupação dedicada sustentada somente com pelo menos cinco leituras válidas em dez segundos; o plano de jogos/streaming pode recomendar investigar esse sinal, sem classificá-lo como pressão, gargalo ou ganho. Para RAM, baixa memória disponível combinada com leituras de páginas sustentadas pode gerar um sinal para revisar a tarefa; hard faults também podem ler executáveis, DLLs e arquivos mapeados e não comprovam falta de RAM. Heurísticas de jogo/OBS consultam todos os processos acessíveis, mesmo os fora da lista de 50 exibida; se o PID do OBS corresponder a uma engine GPU `VideoEncode`, a amostra mostra sua utilização reportada, sem concluir que haja transmissão ao vivo. Um teste ICMP opcional e iniciado pelo usuário envia cinco pings ao IP/host informado, limita DNS a cinco segundos e cada tentativa a um segundo, e registra amostras no histórico local. Sem resposta ICMP não comprova falta de Internet; latência não representa aplicativo, velocidade nem FPS. |
| Limpeza | Analisa temporários do usuário com mais de sete dias; permite selecionar, guardar em recuperação, restaurar e excluir definitivamente em operações separadas. |
| Inicialização | Desativa entradas selecionadas de HKCU Run, preserva comando/tipo anteriores e permite desfazer. Heurísticas protegem entradas de segurança, backup e sincronização. |
| Perfil e experiência do Windows | Perguntas orientam recomendações; efeitos visuais, planos de energia e papel de parede têm revisão e histórico de restauração. A organização da Área de Trabalho mostra os arquivos, categorias e destinos antes de mover; registra hashes e bloqueia restauração quando o conteúdo mudou ou o nome original já está ocupado. Pastas, atalhos, executáveis e tipos não reconhecidos ficam no lugar. |
| Primeira execução | Mostra um guia na primeira abertura, leva ao perfil de uso e recursos a preservar, e registra a conclusão no SQLite. O perfil não aplica mudanças ao Windows; usuários com preferências antigas não recebem o guia novamente. |
| Manutenção | DISM e SFC separados entre verificar e reparar em planos distintos. DISM ScanHealth classifica apenas respostas reconhecidas; SFC /verifyonly usa somente novas entradas `[SR]` do CBS.log e mantém estado desconhecido quando faltam evidências reconhecidas. Análise e otimização de volume usam o mecanismo nativo conforme o tipo de mídia. |
| Proteção | Atualização de assinaturas e verificações rápida, completa e offline do Microsoft Defender ativo. O Windows mantém suas políticas de remediação. |
| Drivers | Consulta candidatos do Windows Update; a instalação revalida a identidade e exporta os drivers existentes. Para reversão, libera dispositivos PnP marcados presentes, exporta o pacote atual e solicita ao Windows a versão anterior que ele manteve, se existir; nunca reinicia automaticamente. |
| Atualizações de programas | Consulta `winget` sob demanda. Cada pacote pode ser atualizado individualmente após confirmação, nova checagem da identidade/versões, WinGet interativo e verificação posterior; termos não são aceitos automaticamente. Tentativas são registradas no SQLite, ficam bloqueadas quando o resultado é incerto e exigem revisão manual, pois a reversão depende do fornecedor. Microsoft Store e pacotes não correspondidos não são incluídos. |
| Plano geral | Reúne preferências visuais explicitamente incluídas e as seleções de limpeza, inicialização e manutenção. Exibe o plano antes de executar sequencialmente. |
| Aparência | Temas Completo, Mínimo, Aurora, Claro, Gamer Neon e Cyberpunk para a interface do ZEUS, com seis opções de cor de destaque persistidas localmente. O alto contraste do Windows continua prevalecendo sobre a paleta escolhida. |
| Histórico | Resultados, logs, estados anteriores e recuperação de sessões interrompidas; exportação JSON e pacote ZIP de diagnóstico revisável, cópia verificada do SQLite e restauração confirmada com proteção do estado atual. |
| Persistência local | SQLite versionado para preferências, atividades, manutenção e sessões/amostras de desempenho; importação dos JSON antigos sem removê-los, verificação de integridade, backup consistente e restauração de cópias compatíveis. |

Nenhuma manutenção é selecionada automaticamente. Drivers e Defender offline possuem fluxos de revisão específicos. O tema altera a interface do ZEUS; as preferências de efeitos do Windows são uma operação separada.

Mover arquivos para recuperação **não libera espaço**. Só a exclusão definitiva remove os bytes guardados; ela não passa pela Lixeira e não pode ser desfeita. Ponto de restauração não recupera documentos apagados. O módulo limita a análise a 10.000 candidatos e arquivos de até 128 MiB.

## Requisitos e execução

O banco local fica em `%LOCALAPPDATA%\Zeus\zeus.db`. A primeira abertura cria o esquema SQLite versionado. `history.json` e `preferences.json` existentes são importados de forma idempotente e mantidos como cópias locais; falhas de leitura não apagam nem substituem esses arquivos. O registro de atividades fica local e mantém os 10.000 eventos mais recentes; revise os dados antes de compartilhar o banco.

- Windows 11 x64 em versão suportada.
- Proteção do Sistema disponível para reparos e instalação de drivers. O auxiliar exige um ponto **novo e confirmado**, sem contornar limites ou políticas do Windows.
- Autorização de administrador via UAC para manutenção; a interface e as alterações do próprio usuário usam permissões comuns.
- Conexão para consultar/baixar drivers e, quando necessário, fontes de reparo do Windows.

No ZIP portátil, extraia a pasta inteira e execute `Zeus.Desktop.exe`. O pacote autocontido inclui o runtime e `Zeus.Maintenance.exe`; não copie apenas um executável. O MSI de desenvolvimento instala em `Program Files`, registra a desinstalação do Windows e cria um atalho no menu Iniciar. A instalação solicita elevação do Windows; isso permite instalar os binários, não autoriza as ações administrativas do ZEUS. Atualizações preservam `%LOCALAPPDATA%\Zeus`; desinstalar remove os binários/atalho, não o banco, backups ou preferências locais. Builds atuais são distribuições de desenvolvimento sem assinatura Authenticode de produção; o Windows pode exibir avisos de editor desconhecido. O ciclo de instalação, atualização, abertura da janela principal e desinstalação passou no runner Windows descartável do GitHub Actions; UAC/SmartScreen e interrupções ainda precisam de aceitação em PC físico antes de recomendar o MSI a usuários. ARM64 é um alvo de publicação ainda sem aceitação em hardware.

O ZEUS usa as ferramentas do Windows; não constitui um novo motor antivírus. Atualizações de BIOS/firmware, overclock, limpeza de registro e desativação de segurança ficam fora deste produto. Um comando concluído não comprova correção de todos os erros ou aumento de desempenho.

## Desenvolvimento e publicação

O SDK está definido em [global.json](global.json).

```powershell
dotnet restore Zeus.slnx
dotnet build Zeus.slnx -c Release
# Testes nativos: execute em uma máquina Windows de teste.
Get-ChildItem tests -Recurse -Filter *.csproj | ForEach-Object {
    dotnet test $_.FullName -c Release --no-build
}
dotnet run --project src/Zeus.Desktop/Zeus.Desktop.csproj
pwsh -File scripts/publish-windows.ps1
$payload = Get-ChildItem artifacts -Directory -Filter 'zeus-win-x64-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
pwsh -File scripts/build-installer.ps1 -PayloadDirectory $payload.FullName
```

O teste que aplica/restaura efeitos visuais exige `ZEUS_WINDOWS_ACCEPTANCE=1`. Essa opção é definida no workflow e no iniciador de testes local quando o usuário escolhe testar o aplicativo. Os arquivos de publicação ficam em `artifacts/`, incluindo ZIP, SHA-256 e identificação do código-fonte.

Para executar a suíte completa no próprio PC, extraia `zeus-testes-windows.zip` e abra `INICIAR-TESTES.cmd`. O Windows solicitará administrador para verificar as ACLs. O iniciador usa ou baixa o SDK oficial, confere SHA-512 e executa os sete projetos de testes, com relatório JSON, TRX, logs e capturas em `artifacts/TestResults/<sessão>`. Os testes criam arquivos e entradas próprios; o teste de efeitos visuais aplica e restaura o estado anterior. Reparos, drivers e reinícios têm revisão própria e não integram essa suíte automática.

No checkout, use `scripts/iniciar-testes.cmd` ou `powershell -NoProfile -File scripts/test-on-windows.ps1`. O pacote de fonte/testes é gerado com `pwsh -File scripts/package-windows-tests.ps1` após commitar as alterações.

```bash
bash scripts/setup-cloud.sh
export PATH="/workspace/.dotnet:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
dotnet build Zeus.slnx -c Release
for project in Zeus.Core.Tests Zeus.Cleanup.Tests Zeus.Maintenance.Protocol.Tests Zeus.UserOptimization.Tests Zeus.Hardware.Tests Zeus.Storage.Tests; do
  dotnet test "tests/$project/$project.csproj" -c Release --no-build
done
```

O setup da nuvem baixa o SDK oficial e verifica SHA-512 pelos metadados da Microsoft. Linux permite compilar todos os projetos e testar as regras portáveis; WPF, UAC, registro e comandos do sistema exigem Windows.

O [workflow de aceitação](.github/workflows/ci.yml) executa testes reais em Windows, abre a interface WPF, coleta inventário/carga e captura as oito áreas e três temas. Também testa aplicar/restaurar preferências e entradas de inicialização isoladas. Os pacotes e a evidência ficam no [GitHub Actions](https://github.com/ggabedesing/zeus/actions). Uma branch `validation/zeus-<commit>/run-<id>-<tentativa>` registra resultados do commit exato; ela contém evidência, não código de produto.

O relatório local enviado em 8 de outubro de 2026 registra **203/203 testes aprovados no PC do usuário**, sem falhas ou testes ignorados, para o commit `c9404f0`. O [registro de validação](docs/validacao-windows.md#execução-local-recebida) identifica a evidência e seus limites; essa aceitação não confirma reparos, instalação de drivers ou reinícios reais.

Reparos, instalação de drivers, criação de pontos de restauração e reinícios não são executados automaticamente pelo CI. Os cenários necessários antes de uma distribuição de produção estão em [docs/validacao-windows.md](docs/validacao-windows.md).

## Estrutura

| Projeto | Responsabilidade |
| --- | --- |
| `Zeus.Core` | Modelos, recomendações e protocolo validado de manutenção |
| `Zeus.Cleanup` | Limpeza seletiva, journal e recuperação de arquivos |
| `Zeus.Storage` | Persistência SQLite local, esquema versionado, atividade e histórico estruturado |
| `Zeus.Windows` | Inventário, carga, preferências, Windows Update e coordenação do auxiliar |
| `Zeus.Maintenance` | Operações administrativas previamente implementadas |
| `Zeus.Desktop` | Interface WPF, revisão do plano, histórico e exportação |
| `tests/` | Regras portáveis, fronteiras de arquivos, registro e aceitação nativa |
| `Zeus.SmokeCheck` | Diagnóstico nativo somente de leitura |

O [esquema e a migração do armazenamento local](docs/persistencia-local.md) descrevem localização, preservação dos JSON antigos, verificação de saúde e limites do registro de atividades.

Leia [SECURITY.md](SECURITY.md) e o [relatório de viabilidade com fontes](docs/relatorio-viabilidade-zeus.md).
