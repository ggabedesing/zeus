# Motor de regras de otimização

O plano formal é uma avaliação explicável de leituras locais. Ele não executa ações e não representa uma pontuação geral de saúde.

## Saídas

- `RecommendationsAvailable`: há sugestões de revisão sustentadas pelas leituras disponíveis.
- `NoOptimizationRequired`: as regras aplicáveis foram avaliadas, nenhuma foi acionada e não há pendência de dados ou pré-requisito.
- `NeedsMoreData`: uma ou mais regras não puderam ser avaliadas com evidência suficiente.
- `PrerequisitesNotMet`: uma sugestão depende de outra regra que não foi satisfeita; ela é suprimida e a dependência é registrada.

O plano inclui, por regra, perfil compatível, categoria de benefício, confiança, evidência exigida, plano de validação, resultado da avaliação e ação associada (atualmente nenhuma regra do planejador seleciona uma ação). Regras com evidência ausente não são convertidas em resultados negativos.

## Compatibilidade, dependências e conflitos

Os perfis do aplicativo são Geral, Jogos, Jogos e transmissão, Trabalho e estudo, Edição e criação, Programação e Autonomia. Regras gerais de memória, espaço, inicialização, proteção e qualidade das leituras são compatíveis com todos os perfis. Regras de observação de jogo se aplicam a Jogos e Jogos e transmissão; a regra de contexto conjunto jogo/OBS se aplica somente a Jogos e transmissão.

As regras de jogo recebem a última amostra do Observador. O sinal de CPU usa a carga total da máquina enquanto um processo de jogo conhecido aparece na lista observada; o sinal de memória disponível segue o mesmo requisito de contexto. Esses sinais não atribuem causalidade ao jogo nem comprovam gargalo. A regra de transmissão conjunta só confirma que processos conhecidos de jogo e OBS aparecem simultaneamente entre os observados; ela não afirma que há partida ou transmissão ao vivo. Trabalho, edição e programação usam uma regra própria de CPU, condicionada a uma amostra durante a tarefa selecionada.

Uma dependência precisa estar presente no perfil e acionada com evidência disponível. Caso contrário, a sugestão dependente é suprimida e o plano informa `PrerequisitesNotMet`. O fechamento transitivo inclui todos os pré-requisitos da candidata. Referências inexistentes, IDs duplicados, autorreferências e ciclos de qualquer tamanho são rejeitados ao carregar definições. Metadados, enumerações e perfis também são validados; as coleções recebidas são copiadas para impedir alteração posterior do grafo.

As candidatas são processadas por confiança decrescente (`High`, `Medium`, `Low`) e, em empate, por ID ordinal. Cada candidata e seus pré-requisitos formam um conjunto indivisível. O conjunto só é aceito quando todas as dependências estão elegíveis, não há conflito interno e nenhum membro conflita com um conjunto já aceito. Conflitos são simétricos mesmo quando declarados em apenas uma das regras. Uma candidata rejeitada não reserva pré-requisitos nem elimina outras sugestões. Conjuntos aceitos permanecem íntegros.

A prioridade pertence à candidata que reservou o conjunto: uma candidata ALTA pode manter um pré-requisito BAIXA diante de uma candidata MÉDIA incompatível com esse pré-requisito. A explicação identifica as regras em conflito e a candidata responsável pela reserva. Trata-se de seleção gulosa determinística, não de maximização global da quantidade de sugestões ou de um ganho estimado. Conflito interno mantém a candidata pendente, e suas dependências ainda podem ser avaliadas como candidatas independentes. Essa resolução organiza sugestões; não autoriza nem executa mudanças no Windows.

## Limites e validação

Os critérios de RAM, espaço livre e contagem de inicialização são heurísticas de triagem. O plano de teste de cada regra pede uma observação representativa antes de atribuir causa ou benefício. Uma leitura vazia não equivale a inventário confirmado quando o coletor registrou falha da fonte.

CPU só constitui evidência com valor finito entre 0 e 100. Janelas de RAM/paginação e ocupação da GPU precisam de duração finita de pelo menos dez segundos e cinco amostras válidas. NaN, infinito, duração insuficiente e CPU fora da faixa permanecem dados insuficientes; não acionam a sugestão nem justificam “nenhuma otimização necessária”.

`OptimizationRuleResolutionTests` cobre cadeias de conflitos em todas as ordens das definições, conflitos unilaterais, dependências atômicas, conjuntos impossíveis, pré-requisitos incompatíveis/fora do perfil, ciclos, cópias imutáveis e valores inválidos de CPU/janelas. O exemplo A ALTA conflita B MÉDIA, B conflita C MÉDIA, A compatível com C mantém A e C; B descartada não pode eliminar C.

Os testes `OptimizationPlannerTests` cobrem plano sem alterações necessárias, evidência ausente, dependência pendente, resolução de conflito, observação específica de jogos e a ausência de ação automática. A aceitação WPF verifica a presença do plano e dos perfis na interface; o relatório exportado inclui o plano formal e, no schema 7, fabricante do dispositivo separado do fornecedor do driver, presença PnP e assinatura reportada quando disponíveis.
