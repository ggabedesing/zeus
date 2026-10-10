# OBS — conexão local opcional de diagnóstico

## Escopo

O ZEUS consulta a API WebSocket 5 do OBS já habilitada pelo usuário. A opção começa desativada, funciona sem elevação e aceita somente uma porta inteira de 1 a 65535 no endereço fixo `127.0.0.1`; proxy, DNS e destinos remotos não são usados. O ZEUS não habilita o servidor, não reinicia o OBS e não altera suas cenas, saídas ou configurações.

Contrato externo consultado em 2026-10-09: [protocolo oficial obs-websocket](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md). Handshake, autenticação e tipos de resposta seguem essa especificação. A allowlist do ZEUS contém exclusivamente `GetVersion`, `GetStats`, `GetStreamStatus` e `GetRecordStatus`; a assinatura de eventos é zero. Requests não disponíveis ficam desconhecidos.

## Uso na interface

Em **Hardware e carga**, informe a porta do servidor e, se necessário, sua senha. Marque **Consultar OBS nas medições** e escolha **Salvar conexão**. Salvar não estabelece conexão. A leitura ocorre ao usar **Consultar OBS agora**, uma medição manual ou o Observador. A consulta avulsa apresenta dados na tela; as medições de desempenho também guardam a evidência no histórico e relatório.

Senha vazia mantém a senha já salva. **Esquecer senha** remove somente a cópia do ZEUS e encerra sua conexão. Para desativar as consultas, desmarque a opção e salve. O servidor e a senha configurada no OBS permanecem sob controle do usuário.

## Segurança e armazenamento

Referências de plataforma: [CryptProtectData/DPAPI](https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata) e [CreateFileW e compartilhamento de handles](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew). Os limites abaixo descrevem a implementação do ZEUS e as verificações realizadas, sem certificar isolamento contra o próprio usuário.

- A senha fica em `%LocalAppData%\Zeus\obs-connection\password.dpapi`, protegida com DPAPI do usuário atual, separada do SQLite, preferências e relatórios. Fixtures usam um diretório temporário próprio.
- O backup do aplicativo inclui o banco SQLite, não a pasta de credenciais. Não copiar a pasta de senha para pacotes de diagnóstico ou futura rotina de backup integral. Ao restaurar o banco em outro usuário, informar a senha novamente.
- Escrita de ciphertext atômica, tamanho limitado, UTF-8 estrito e handles de diretório com acesso de listagem, sem compartilhar exclusão, rejeitam junctions existentes e impedem rename/substituição de ancestrais validados durante a operação. Compartilhamento de escrita permanece necessário para a troca atômica do arquivo; esses handles não bloqueiam alterações de metadados por outro processo do mesmo usuário. Se faltam diretórios, Load/Clear retornam sem acessar o arquivo; Save cria e valida um componente por vez com o pai travado. DPAPI protege contra outros usuários, não contra um processo malicioso com o próprio token do usuário; a implementação não promete cobrir todas as suas técnicas. A senha pode existir transitoriamente como string no processo; a implementação não promete apagar strings gerenciadas. Buffers próprios de plaintext, autenticação e fingerprint são zerados após uso.
- Mensagens de erro são fixas; comentários arbitrários do servidor e dados de autenticação não entram em logs/modelos. Não são solicitadas configurações do OBS, chaves de transmissão ou fontes/cenas. Campos extras eventualmente retornados pelo servidor são ignorados; uma fixture inclui um caminho de gravação fictício e comprova que ele não entra na observação ou exportação.
- A conexão possui limite total de seis segundos por captura, mensagens de texto até 64 KiB, profundidade JSON até 16 e rejeição de propriedades duplicadas, correlação inválida e campos incompatíveis. Capturas são serializadas. Timeout ou erro encerra a conexão e invalida sua referência de deltas; cancelamento solicitado pelo usuário propaga após encerrá-la.

## Evidência e interpretação

`PerformanceObservation.Obs` registra estados `Disabled`, `Complete`, `Partial`, `Unavailable`, `TimedOut` ou `Failed`. Campos ausentes ficam `null`. Uma resposta inválida não adiciona campos daquela requisição, mas leituras anteriores já validadas podem sobreviver como evidência parcial. Uma conexão recusada não prova que o OBS esteja fechado.

Há estados separados de transmissão, gravação, pausa da gravação e reconexão; FPS, CPU, memória, tempo médio de renderização e contadores de frames reportados pelo OBS. Gravação ativa com pausa é apresentada como pausada; ausência desse campo não vira não pausada. Codec permanece indisponível: presença de uma engine GPU não identifica o encoder configurado. A detecção por processo continua separada do estado informado pela API.

Os percentuais de frames pulados são diferenças entre contadores, e não médias de percentuais cumulativos. Cada delta exige conexão idêntica, counters válidos e monotônicos, denominador positivo e horários locais crescentes. A transmissão exige saída ativa nas duas consultas e duração crescente; a thread de saída exige estados conhecidos e estáveis de gravação/transmissão e ao menos uma saída ativa. Renderização, thread de saída e saída de transmissão são fontes distintas.

O relatório guarda os horários locais de recebimento validado por fonte (`StatsReadAt`, `StreamReadAt`, `RecordReadAt`) e os horários de referência dos deltas (`PreviousStatsReadAt`, `PreviousStreamReadAt`). Não são relógios internos do OBS nem uma captura atômica. A leitura OBS acontece após a amostragem Windows e tem sua própria janela. Esses percentuais não medem perda de pacotes da rede inteira, nem provam gargalo, qualidade visual ou benefício de otimização.

Parada e reinício ocorridos inteiramente entre duas consultas podem não ser observados. A implementação não afirma continuidade conclusiva da saída. Ausência de delta não significa ausência de frames pulados. Nenhuma regra de mudança de configuração do encoder foi acrescentada.

## Persistência e compatibilidade

SQLite permanece no esquema 7: a evidência OBS é aditiva no JSON de detalhes da amostra. Relatório passa ao esquema 14. Histórico antigo sem `Obs` continua desconhecido, e preferências antigas assumem conexão desativada/porta 4455. A senha nunca faz parte desses modelos. Resumo persistido é limitado a 1024 caracteres com aviso de omissão; medidas e estados são preservados.

## Validação e limites deste marco

Os testes usam servidores WebSocket locais controlados para verificar autenticação, fragmentação, limites, erros, correlação, sessão e deltas; não substituem uma transmissão real. Fixtures Windows verificam DPAPI, roundtrip, corrupção, junction, exclusão, SQLite, exportação e backup sem credenciais. Aceitação WPF exercita salvar/limpar/desativar, senha fora de campos visíveis, porta inválida e integração das medições sem alterar um OBS real.

O ensaio contra uma instalação real do OBS, durante transmissão/gravação autorizadas, permanece pendente. Ainda faltam comparação agregada de métricas OBS entre sessões e comprovação de encoder/gargalos com fontes adequadas. Abertura do EXE e testes do protocolo não encerram a fase 9 inteira.
