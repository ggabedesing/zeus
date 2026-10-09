# Avisos de terceiros do pacote Windows

Este inventário cobre dependências NuGet e o runtime .NET incluídos no pacote autocontido de Windows. Ele não é uma licença do código ou da marca ZEUS. O repositório ainda não contém um arquivo `LICENSE` para o próprio projeto; essa escolha permanece pendente do mantenedor.

O publicador lê `Zeus.Desktop.deps.json` e `Zeus.Maintenance.deps.json`, exige metadados de licença para cada pacote NuGet de runtime, inclui os textos SPDX MIT e Apache-2.0 usados, preserva avisos adicionais presentes nos pacotes e copia `ThirdPartyNotices.txt` da distribuição .NET. O manifesto do pacote registra as hashes desses arquivos junto aos demais arquivos publicados.

O conjunto esperado no build atual é:

| Componente | Licença declarada no pacote | Uso no ZEUS |
| --- | --- | --- |
| Microsoft.Data.Sqlite 10.0.12 e Microsoft.Data.Sqlite.Core 10.0.12 | MIT | Persistência SQLite |
| SQLitePCLRaw.bundle_e_sqlite3 2.1.12, SQLitePCLRaw.core 2.1.12, SQLitePCLRaw.lib.e_sqlite3 2.1.12 e SQLitePCLRaw.provider.e_sqlite3 2.1.12 | Apache-2.0 | Provedor e biblioteca nativa SQLite |
| System.Management 10.0.0 | MIT | Consultas WMI/CIM do Windows |
| System.CodeDom 10.0.0 | MIT | Dependência do auxiliar de manutenção |
| Runtime Microsoft.NETCore.App e Microsoft.WindowsDesktop.App 10.0.12 | Conforme avisos do runtime Microsoft | Aplicação autocontida WPF |

O pacote também inclui dependências transitivas adicionais quando aparecem nos manifests `.deps.json`. A lista real e suas versões ficam em `THIRD-PARTY-NOTICES.md` gerado dentro de cada pacote; avisos detalhados ficam em `THIRD-PARTY-NOTICES.NET.txt` e nos arquivos de aviso NuGet correspondentes.

Os textos padrão SPDX estão em [`licenses/SPDX`](../licenses/SPDX), conforme a lista oficial de [MIT](https://raw.githubusercontent.com/spdx/license-list-data/main/text/MIT.txt) e [Apache-2.0](https://raw.githubusercontent.com/spdx/license-list-data/main/text/Apache-2.0.txt). A licença do ZEUS, os avisos de ativos próprios e a revisão completa do código-fonte ainda precisam ser concluídos antes de uma declaração de release open source.
