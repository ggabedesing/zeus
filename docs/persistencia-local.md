# Persistência local do ZEUS

## Arquivo e esquema

O banco do usuário fica em `%LOCALAPPDATA%\Zeus\zeus.db`. Ele é criado na primeira execução com SQLite e registra a versão em `PRAGMA user_version` e em `schema_migrations`. A inicialização valida tabelas obrigatórias, recusa uma versão de esquema mais nova que a do aplicativo e não substitui um banco incompatível.

O esquema atual (versão 3) contém:

- `app_settings`: preferências da interface serializadas em JSON, com chave estável e data de atualização;
- `maintenance_sessions` e `maintenance_steps`: sessões e etapas normalizadas, com chaves estrangeiras e sequência;
- `performance_sessions` e `performance_samples`: sessões de observação, referência antes/depois e amostras com CPU/RAM em colunas tipadas e demais contadores em JSON validado;
- `activity_entries`: eventos locais com data UTC, categoria, tipo, severidade, resumo, detalhes JSON opcional e correlação;
- `app_metadata`: marcadores de importação idempotente dos arquivos JSON antigos;
- `schema_migrations`: versões aplicadas e respectivas datas.

O banco usa transações para gravações de histórico e eventos. O registro de atividades mantém no máximo 10.000 entradas; leituras recentes retornam até 500 por padrão. Mensagens e logs locais podem conter caminhos ou nomes pessoais. O banco não é enviado pelo ZEUS e não é incluído na exportação JSON padrão.

## Migração sem perda dos dados antigos

Na primeira leitura, `history.json` e `preferences.json` são validados e importados quando existem. O marcador é gravado na mesma transação que os dados importados. A origem JSON permanece intacta como cópia local. Uma entrada já presente no banco não é sobrescrita pelo arquivo legado. Se a validação ou a gravação falhar, o arquivo JSON não é apagado.

Novas preferências e sessões são gravadas no SQLite. Os arquivos JSON antigos não são atualizados depois da migração. Não remova o banco nem os arquivos de origem durante a migração. Na aba Histórico, o usuário pode criar uma cópia de segurança do banco em uso: o SQLite produz uma cópia consistente, o ZEUS confere `integrity_check` e versão do esquema, e só então grava o arquivo no destino escolhido. Para restaurar, o ZEUS copia o arquivo escolhido para uma área temporária, valida e migra essa cópia sem tocar na origem, cria uma segunda cópia verificada do banco atual em `%LOCALAPPDATA%\Zeus\recovery` e só então restaura os dados. Se a aplicação da cópia falhar, tenta recuperar o estado anterior automaticamente; a cópia de segurança permanece para recuperação manual. O aplicativo pede confirmação e reinicia para carregar os dados restaurados. Backups incluem configurações e dados locais, podendo conter nomes de processos, caminhos e eventos; guarde-os em local privado.

## Saúde e recuperação

O observador conserva até 600 amostras em memória por processo aberto e também grava sessões no SQLite. A referência marcada e as sessões mais recentes são recuperadas na próxima abertura. O banco limita o histórico a 200 sessões comuns e 4.000 amostras; cada payload JSON tem limite de 64 KiB. O JSON detalhado inclui nomes de processos e contadores locais; trate o banco e o relatório exportado como dados pessoais.

O estado interno consulta `PRAGMA quick_check`, versão do SQLite, versão do esquema, tamanho do arquivo principal e contagens de eventos e sessões de manutenção. A falha da checagem aparece como estado degradado; o aplicativo não tenta reparar nem apagar o banco automaticamente. As migrações da versão 1 para 2 e da versão 2 para 3 são transacionais e preservam as configurações, eventos e histórico existentes; versões futuras devem seguir o mesmo padrão e preservar esquemas desconhecidos.

## Testes

`Zeus.Storage.Tests` exercita criação/versionamento, migração 1→2, integridade, gravação de preferências e atividade, sessões/etapas, amostras de desempenho e idempotência da importação. `DesktopStorageTests` verifica a migração dos JSON legados e confirma que os arquivos de origem permanecem iguais.
