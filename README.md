# ZEUS

Aplicativo nativo para diagnóstico e manutenção de computadores Windows, com interface em português, plano de recomendações e execução explícita de ações oficiais.

**Estado: MVP em desenvolvimento.** O aplicativo utiliza dados reais do computador. Não promete ganhos fixos de desempenho e não inclui limpeza de registro, desativação de proteção, instalação automática de drivers ou mudanças de firmware.

## Recursos desta versão

- Inventário de CPU, RAM, GPU, unidades e programas de inicialização.
- Estado do Microsoft Defender quando acessível; informações ausentes são sinalizadas.
- Recomendações conservadoras baseadas no diagnóstico.
- Análise de volume, verificação/reparo de imagem Windows e arquivos protegidos, verificação rápida do Defender.
- Confirmação das ações, elevação via UAC e ponto de restauração confirmado antes dos reparos.
- Histórico local, exportação de diagnóstico e layouts Minimal/Completo.

As verificações não são reparos automáticos. Um comando concluído não garante que todos os problemas foram corrigidos; resultados e logs devem ser conferidos. Ponto de restauração não substitui backup dos seus documentos.

## Requisitos

- Windows 11 x64 em versão suportada para o aplicativo.
- SDK .NET definido em [global.json](global.json) para desenvolvimento.
- Administração somente para manutenção que a exige; diagnóstico básico e interface usam permissões comuns.
- Proteção do Sistema disponível e criação de um ponto novo para os reparos protegidos. O Windows pode limitar a criação de pontos, incluindo o intervalo de 24 horas do cmdlet utilizado.

Pacotes portáteis são builds de desenvolvimento sem assinatura de produção. ARM64 é um alvo de publicação possível, ainda pendente de validação em hardware. Esta versão não constitui um novo motor antivírus.

## Desenvolver no Windows

```powershell
dotnet restore Zeus.slnx
dotnet build Zeus.slnx -c Release
dotnet test tests/Zeus.Core.Tests/Zeus.Core.Tests.csproj -c Release
dotnet run --project src/Zeus.Desktop/Zeus.Desktop.csproj
```

O auxiliar `Zeus.Maintenance` deve permanecer junto do executável Desktop. O build copia seus arquivos para permitir execução local; a publicação abaixo inclui ambos.

## Gerar o pacote portátil

```powershell
pwsh -File scripts/publish-windows.ps1
```

O pacote autocontido fica em `artifacts/zeus-win-x64.zip`. Extraia a pasta inteira e execute `Zeus.Desktop.exe`. Não copie apenas o executável. O runtime acompanha o pacote, sem exigir SDK na máquina do usuário.

O workflow em [.github/workflows/ci.yml](.github/workflows/ci.yml) compila e testa o projeto, executa diagnóstico somente de leitura em Windows e gera o artefato portátil. Os artefatos ficam na execução correspondente em [GitHub Actions](https://github.com/ggabedesing/zeus/actions).

## Trabalhar no ambiente Linux da nuvem

```bash
bash scripts/setup-cloud.sh
export PATH="/workspace/.dotnet:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
dotnet build Zeus.slnx -c Release -m:2
dotnet test tests/Zeus.Core.Tests/Zeus.Core.Tests.csproj -c Release
```

O setup baixa o SDK oficial e confere SHA-512 com os metadados da Microsoft. Use o checkout existente: tarefas na nuvem já são isoladas, sem necessidade de criar worktrees.

Linux permite compilar o alvo Windows e testar as regras compartilhadas. A interface WPF, UAC, sensores, Defender e reparos exigem Windows para validação funcional. Não execute reparos em CI; use máquinas de teste com backups e snapshots para esses cenários.

## Estrutura

| Projeto | Responsabilidade |
| --- | --- |
| `Zeus.Core` | Modelos, catálogo, política de manutenção e recomendações |
| `Zeus.Windows` | Coleta real e coordenação do auxiliar elevado |
| `Zeus.Maintenance` | Execução de operações previamente implementadas |
| `Zeus.Desktop` | Interface WPF, diagnóstico, seleção de ações e histórico |
| `Zeus.Core.Tests` | Testes de regras, permissões lógicas e recomendações |
| `Zeus.SmokeCheck` | Diagnóstico somente de leitura para CI Windows |

O projeto não aceita scripts livres para execução administrativa. Leia [SECURITY.md](SECURITY.md) para o modelo de recuperação e distribuição.

## Próximas etapas

1. Validar interface, UAC e recuperação em máquinas Windows de teste.
2. Medir o consumo do próprio aplicativo e os resultados em PCs fracos.
3. Desenvolver limpeza seletiva com recuperação específica e revisão de inicialização.
4. Adicionar automação de drivers com fontes oficiais e matriz de compatibilidade.
5. Expandir sensores, perfis de uso e personalização.

O [relatório de viabilidade](docs/relatorio-viabilidade-zeus.md) detalha o produto, as fontes pesquisadas e os limites de cada módulo.
