# Estados da transação de manutenção

Cada etapa registra o resultado do executor e um estado de verificação separado. `Succeeded` significa que o executor aceitou o resultado conforme a ação; não é uma afirmação genérica de que o problema original foi resolvido.

| Estado | Significado |
|---|---|
| `NotRecorded` | Relatório antigo ou fonte que não informou estado estruturado. |
| `NotStarted` | Ação bloqueada/cancelada antes de iniciar; nenhuma verificação necessária foi executada. |
| `Pending` | O auxiliar marcou a etapa como em andamento antes do comando; o término ainda não foi confirmado. |
| `CommandCompleted` | O comando terminou com código aceito. Inspecione logs e, para verificações, os resultados do Windows. |
| `ProviderConfirmed` | O provedor confirmou a operação solicitada (por exemplo, Windows Update confirmou a instalação da identidade); valide o dispositivo e o problema original. |
| `ManualReviewRequired` | Houve falha, interrupção, reparo ou operação cuja saída não confirma o estado final; examine o log e o estado do Windows antes de repetir. |

O Desktop mostra o estado junto a cada etapa. O histórico SQLite foi migrado para o schema 3; sessões antigas recebem `NotRecorded`, sem serem convertidas em confirmações. O recibo protegido impede repetição automática: uma sessão incompleta fica no histórico para inspeção e uma nova execução exige nova revisão e consentimento no app.
