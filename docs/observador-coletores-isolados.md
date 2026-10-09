# Observador — coletores isolados

## Contrato

`WindowsPerformanceProbe.SampleAsync` supervisiona auxiliares `Zeus.Observer.exe` locais, sem elevação. O executável é resolvido ao lado do aplicativo; não é procurado no PATH. O auxiliar aceita somente categoria conhecida e duração inteira de 2 a 30 segundos. Não recebe comandos, caminhos de arquivos pessoais ou ações de otimização.

CPU e processos são medidos primeiro. Depois são consultados seis coletores, com até três auxiliares concorrentes: engines GPU, memória por adaptador GPU, memória GPU por processo, RAM/paginação, discos e rede. RAM física e paginação permanecem no mesmo coletor. As leituras não são simultâneas; início/fim e duração de cada execução aparecem no relatório. O intervalo de CPU continua separado do tempo total da coleta.

O prazo do coletor CPU é a duração pedida mais 10 segundos; os demais têm 10 segundos cada. Cancelamento pelo usuário encerra os auxiliares e propaga cancelamento. Prazo excedido retorna estado TimedOut com as leituras válidas recebidas até então, sem repetir a coleta silenciosamente. A limpeza tem espera limitada de cinco segundos; falha para confirmar encerramento é erro explícito.

## Dados parciais e indisponíveis

O protocolo NDJSON em stdout publica linhas válidas durante a coleta. O supervisor verifica categoria, sequência, estrutura, valores, quantidade e tamanho. O limite total de stdout é 2 MiB e stderr é limitado a 16 KiB. Um pacote final somente é aceito dentro do contrato. EOF abrupto, prazo excedido ou saída inválida preservam apenas os dados anteriormente validados e indicam incompletude.

CPU por processo e I/O continuam usando PID e início do processo, com intervalo próprio. A lista de CPU/RAM apresenta até 50 processos; I/O tem lista independente até 30. Associação GPU e heurísticas consultam o conjunto acessível preservado, limitado a 4.096 processos, com truncamento informado. Memória GPU por processo consulta também o início pelo PID no próprio coletor; nome da janela CPU só é usado quando as épocas positivas correspondem. Epoch ausente ou diferente mantém o nome desconhecido. Essa observação não torna atômicas a consulta WMI e a leitura do processo; nome de engine associado apenas por PID da janela CPU não prova que aquele processo continuou vivo até a consulta GPU.

Estados Complete, Partial, Unavailable, TimedOut e Failed descrevem a leitura, não a saúde do componente. Ausência de CPU não vira utilização de zero. A capacidade de RAM usa o contrato legado de zero para leitura ausente, acompanhada de aviso; a interface apresenta indisponível. GPU/VRAM não confirmam gargalo, I/O não atribui bytes a disco físico, e presença de OBS/engine de codificação não confirma transmissão.

## Histórico e distribuição

`PerformanceObservation.Collectors` é opcional: histórico antigo continua sem esse dado e a interface informa sua ausência. Estados, início/fim e duração são conservados no SQLite e na exportação JSON esquema 13. Avisos longos por coletor podem ser resumidos no histórico, com indicação de omissão; a amostra ao vivo permanece completa dentro dos limites do protocolo.

Desktop, testes e pacote portátil incluem o auxiliar com seus arquivos de execução. Publicação inclui o novo projeto no inventário de dependências/licenças, hashes e verificações de versão. O caminho de release exige também seus binários; isso não declara que o pacote de desenvolvimento tem assinatura de produção.

## Controle dos processos

A implementação cria o auxiliar suspenso, associa-o ao Job Object e somente depois retoma sua execução, evitando a janela entre iniciar e associar. O fechamento do Job Object encerra os processos pertencentes ao coletor, inclusive quando o processo principal já saiu. A lista de handles herdados inclui somente stdin/stdout/stderr do auxiliar. A execução não reduz proteções do Windows nem encerra processos de outros aplicativos.

Referências consultadas: [UpdateProcThreadAttribute — lista de handles na criação](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute) e [Process Creation Flags — CREATE_SUSPENDED](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags). O ZEUS usa criação suspensa e atribuição ao Job antes de retomar; não depende de associar um processo que já esteja executando.

## Limites da aceitação

Fixtures de travamento, saída incompleta/inválida/excessiva e cancelamento verificam a fronteira de processos. Leituras reais e medição pela interface verificam associação, persistência e relatório. Nenhuma dessas verificações comprova precisão universal dos contadores, compatibilidade com todas as GPUs/versões do Windows ou melhoria de desempenho. O custo dos próprios auxiliares faz parte da carga observada; resultados antes/depois exigem condições equivalentes.
