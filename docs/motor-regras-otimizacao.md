# Motor de regras de otimização

O plano formal é uma avaliação explicável de leituras locais. Ele não executa ações e não representa uma pontuação geral de saúde.

## Saídas

- `RecommendationsAvailable`: há sugestões de revisão sustentadas pelas leituras disponíveis.
- `NoOptimizationRequired`: as regras aplicáveis foram avaliadas, nenhuma foi acionada e não há pendência de dados ou pré-requisito.
- `NeedsMoreData`: uma ou mais regras não puderam ser avaliadas com evidência suficiente.
- `PrerequisitesNotMet`: uma sugestão depende de outra regra que não foi satisfeita; ela é suprimida e a dependência é registrada.

O plano inclui, por regra, perfil compatível, categoria de benefício, confiança, evidência exigida, plano de validação, resultado da avaliação e ação associada (atualmente nenhuma regra do planejador seleciona uma ação). Regras com evidência ausente não são convertidas em resultados negativos.

## Compatibilidade, dependências e conflitos

As regras atuais são diagnósticos de memória, espaço, inicialização, proteção e qualidade das leituras. São compatíveis com os perfis definidos porque os critérios medem o computador, independentemente do perfil de uso; o perfil continua disponível no plano para evoluir regras específicas sem mudar os identificadores existentes.

Uma dependência precisa estar presente no perfil e acionada com evidência disponível. Caso contrário, a sugestão dependente é suprimida e o plano informa `PrerequisitesNotMet`. Dependências em cascata são resolvidas até estabilizarem. Referências inexistentes, IDs duplicados e autorreferências são rejeitados ao carregar definições.

Em conflito, vence a regra com maior confiança (`High`, `Medium`, `Low`). Empates são resolvidos por ID ordinal para manter o resultado determinístico. O relatório registra as duas regras, a vencedora e a regra suprimida. Essa resolução organiza sugestões; não autoriza nem executa mudanças no Windows.

## Limites e validação

Os critérios de RAM, espaço livre e contagem de inicialização são heurísticas de triagem. O plano de teste de cada regra pede uma observação representativa antes de atribuir causa ou benefício. Uma leitura vazia não equivale a inventário confirmado quando o coletor registrou falha da fonte.

Os testes `OptimizationPlannerTests` cobrem plano sem alterações necessárias, evidência ausente, dependência pendente, resolução de conflito e a ausência de ação automática. A aceitação WPF verifica a presença do plano na interface; o relatório exportado inclui o plano formal no schema 4.
