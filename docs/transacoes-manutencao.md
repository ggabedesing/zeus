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

O Desktop mostra o estado junto a cada etapa. O histórico SQLite foi migrado para o schema 4; sessões antigas recebem `NotRecorded` e origem WUA desconhecida, sem serem convertidas em confirmações. Instalações de driver novas guardam a seleção lógica da fonte usada, para permitir reconsulta explícita após reinicialização sem trocar silenciosamente de serviço. O recibo protegido impede repetição automática: uma sessão incompleta fica no histórico para inspeção e uma nova execução exige nova revisão e consentimento no app.

As alterações reversíveis do usuário (inicialização, preferências visuais e plano de energia) também gravam transições duráveis: `Prepared` antes da ação, `Applying` antes da escrita, `Applied` somente depois da leitura de verificação, `Restoring` antes da reversão e `Restored` somente após confirmar o estado anterior. Interrupções em `Applying`/`Restoring` ficam identificadas como resultado incerto. Se uma mudança externa impedir a reversão, `RestoreBlocked` preserva a configuração externa e deixa a tentativa explícita no histórico. Documentos antigos sem status permanecem `Unknown`.

O DISM ScanHealth informa uma das classificações do repositório de componentes somente quando a saída contém exatamente um resultado conhecido. Código de saída zero sem resultado reconhecido, saída conflitante ou erro fica como desconhecido e exige revisão do log. “Sem corrupção detectada” vale apenas para o repositório de componentes; não certifica toda a instalação do Windows. A etapa Scan não executa RestoreHealth.

SFC `/verifyonly` classifica somente entradas `[SR]` anexadas ao `CBS.log` desde o início daquela execução. `Verify complete` sem marcador de divergência reconhecido informa que o SFC não indicou violações nos arquivos protegidos verificados; `Repairing corrupted file` e `Cannot repair member file` indicam divergência, com o segundo preservado como estado específico não reparável. Isso não afirma a saúde de todos os componentes do Windows. Log inacessível, truncado/rotacionado, grande demais, código inesperado ou frases não reconhecidas permanecem desconhecidos. A fonte Microsoft documenta que o CBS recebe entradas SFC `[SR]`, que o Windows Modules Installer também escreve no mesmo log, e mostra exemplos para arquivos limpos/divergentes; por isso o ZEUS lê apenas o trecho novo e continua conservador quando a saída não pode ser isolada. `/verifyonly` não altera os arquivos. Reparos SFC continuam exigindo revisão manual e verificação posterior.

O protocolo rejeita um plano que combine qualquer verificação DISM/SFC com qualquer reparo DISM/SFC. Na interface, o botão de execução fica desativado e a mensagem orienta revisar a verificação e montar um novo plano. O auxiliar repete a validação antes de criar a sessão, então payloads externos inválidos também não iniciam comandos.

A busca de atualizações de software usa o Windows Update Agent somente quando solicitada pelo usuário, com o critério `IsInstalled=0 and IsHidden=0 and Type='Software'` e as fontes/políticas configuradas no Windows. Ela retorna títulos, KBs, estado de download e ID da atualização. Não chama download, instalação, aceite de licença ou reinicialização. Resultado parcial, erro, identidade inválida ou duplicada permanece incompleto; uma lista vazia só é descrita como vazia quando a busca foi completa. A validação online em Windows conectado/gerenciado continua separada dos testes de parser e da aceitação da interface.
