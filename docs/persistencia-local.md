# Persistência local do ZEUS

## Arquivo e esquema

O banco do usuário fica em `%LOCALAPPDATA%\Zeus\zeus.db`. Ele é criado na primeira execução com SQLite e registra a versão em `PRAGMA user_version` e em `schema_migrations`. A inicialização valida tabelas obrigatórias, recusa uma versão de esquema mais nova que a do aplicativo e não substitui um banco incompatível.

O esquema atual (versão 1) contém:

- `app_settings`: preferências da interface serializadas em JSON, com chave estável e data de atualização;
- `maintenance_sessions` e `maintenance_steps`: sessões e etapas normalizadas, com chaves estrangeiras e sequência;
- `activity_entries`: eventos locais com data UTC, categoria, tipo, severidade, resumo, detalhes JSON opcional e correlação;
- `app_metadata`: marcadores de importação idempotente dos arquivos JSON antigos;
- `schema_migrations`: versões aplicadas e respectivas datas.

O banco usa transações para gravações de histórico e eventos. O registro de atividades mantém no máximo 10.000 entradas; leituras recentes retornam até 500 por padrão. Mensagens e logs locais podem conter caminhos ou nomes pessoais. O banco não é enviado pelo ZEUS e não é incluído na exportação JSON padrão.

## Migração sem perda dos dados antigos

Na primeira leitura, `history.json` e `preferences.json` são validados e importados quando existem. O marcador é gravado na mesma transação que os dados importados. A origem JSON permanece intacta como cópia local. Uma entrada já presente no banco não é sobrescrita pelo arquivo legado. Se a validação ou a gravação falhar, o arquivo JSON não é apagado.

Novas preferências e sessões são gravadas no SQLite. Os arquivos JSON antigos não são atualizados depois da migração. Não remova o banco nem os arquivos de origem durante a migração; backup e restauração administrados pelo aplicativo ficam para a fase de liberação.

## Saúde e recuperação

O estado interno consulta `PRAGMA quick_check`, versão do SQLite, versão do esquema, tamanho do arquivo principal e contagens de eventos e sessões. A falha da checagem aparece como estado degradado; o aplicativo não tenta reparar nem apagar o banco automaticamente. Migração de esquema futura deve adicionar uma etapa transacional, preservar versão desconhecida e testar interrupções antes de ser habilitada.

## Testes

`Zeus.Storage.Tests` exercita criação/versionamento, integridade, gravação de preferências, atividade, sessão/etapas normalizadas e idempotência da importação. `DesktopStorageTests` verifica a migração dos JSON legados e confirma que os arquivos de origem permanecem iguais.
