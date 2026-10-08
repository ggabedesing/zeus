# ZEUS

Aplicativo Windows para diagnóstico, manutenção e otimização com dados reais, interface em português e revisão das alterações antes da execução.

**Estado: versão de desenvolvimento com módulos integrados; aceitação nativa acompanhada pelo GitHub Actions.** Os resultados de cada execução são registrados. Ganhos de desempenho precisam ser medidos no computador e na tarefa do usuário.

## Recursos

| Área | O que o aplicativo faz |
| --- | --- |
| Hardware | Lê CPU, RAM, GPU e drivers, placa-mãe, BIOS, módulos RAM, volumes, discos físicos, bateria e rede. Sensores aparecem somente quando o Windows fornece a leitura. |
| Inventário do Windows | Consulta configuração de rede, drivers, PnP, processos, serviços, tarefas agendadas, programas instalados e eventos recentes; tenta ler Secure Boot e TPM. Endereços e nomes locais podem constar no relatório. Atualizações pendentes e saúde da imagem não são medidos durante a coleta. |
| Carga real | Observa CPU, RAM e processos, além de engines de GPU e tráfego/atividade local de disco e rede quando disponíveis. A amostragem adapta o intervalo, guarda sessões e referência no SQLite, conserva um buffer recente, compara médias de CPU/RAM e identifica heurísticas de processos de jogos/OBS. A presença de OBS não comprova transmissão ao vivo. |
| Limpeza | Analisa temporários do usuário com mais de sete dias; permite selecionar, guardar em recuperação, restaurar e excluir definitivamente em operações separadas. |
| Inicialização | Desativa entradas selecionadas de HKCU Run, preserva comando/tipo anteriores e permite desfazer. Heurísticas protegem entradas de segurança, backup e sincronização. |
| Perfil | Perguntas de uso orientam recomendações; efeitos visuais e planos de energia existentes têm revisão e recuperação próprias. |
| Manutenção | DISM e SFC para verificar/reparar o Windows; análise e otimização do volume pelo mecanismo nativo, conforme o tipo de mídia. |
| Proteção | Atualização de assinaturas e verificações rápida, completa e offline do Microsoft Defender ativo. O Windows mantém suas políticas de remediação. |
| Drivers | Consulta candidatos oficiais do Windows Update, mostra licenças e instala as identidades selecionadas após proteção de recuperação e exportação dos drivers existentes. |
| Plano geral | Reúne preferências visuais explicitamente incluídas e as seleções de limpeza, inicialização e manutenção. Exibe o plano antes de executar sequencialmente. |
| Aparência | Temas Completo, Mínimo e Aurora, inspirado no macOS, para a interface do ZEUS. |
| Histórico | Resultados, logs, estados anteriores e recuperação de sessões interrompidas; exportação de relatório JSON. |
| Persistência local | SQLite versionado para preferências, atividades, manutenção e sessões/amostras de desempenho; importação dos JSON antigos sem removê-los e verificação rápida de integridade. |

Nenhuma manutenção é selecionada automaticamente. Drivers e Defender offline possuem fluxos de revisão específicos. O tema altera a interface do ZEUS; as preferências de efeitos do Windows são uma operação separada.

Mover arquivos para recuperação **não libera espaço**. Só a exclusão definitiva remove os bytes guardados; ela não passa pela Lixeira e não pode ser desfeita. Ponto de restauração não recupera documentos apagados. O módulo limita a análise a 10.000 candidatos e arquivos de até 128 MiB.

## Requisitos e execução

O banco local fica em `%LOCALAPPDATA%\Zeus\zeus.db`. A primeira abertura cria o esquema SQLite versionado. `history.json` e `preferences.json` existentes são importados de forma idempotente e mantidos como cópias locais; falhas de leitura não apagam nem substituem esses arquivos. O registro de atividades fica local e mantém os 10.000 eventos mais recentes; revise os dados antes de compartilhar o banco.

- Windows 11 x64 em versão suportada.
- Proteção do Sistema disponível para reparos e instalação de drivers. O auxiliar exige um ponto **novo e confirmado**, sem contornar limites ou políticas do Windows.
- Autorização de administrador via UAC para manutenção; a interface e as alterações do próprio usuário usam permissões comuns.
- Conexão para consultar/baixar drivers e, quando necessário, fontes de reparo do Windows.

Extraia a pasta inteira de `zeus-win-x64.zip` e execute `Zeus.Desktop.exe`. O pacote autocontido inclui o runtime e `Zeus.Maintenance.exe`; não copie apenas um executável. Builds atuais são distribuições de desenvolvimento sem assinatura Authenticode de produção. ARM64 é um alvo de publicação ainda sem aceitação em hardware.

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
