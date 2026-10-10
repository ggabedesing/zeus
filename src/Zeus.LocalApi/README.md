# Backend local da interface simples

`Zeus.LocalApi` usa o inventário e a observação reais de `Zeus.Windows`. Não fornece executor de comandos ou caminhos recebidos da interface.

## Inicialização

- `ZEUS_PORT`: porta entre 1024 e 65535; escuta somente em `127.0.0.1`.
- `ZEUS_TOKEN`: segredo aleatório de 32 a 256 caracteres criado pelo processo desktop.
- Todas as requisições precisam de `X-Zeus-Token`, Host `127.0.0.1` e ausência de Origin. O frontend usa a ponte do processo principal, sem CORS.
- Pronto para receber chamadas quando a saída contém `ZEUS_READY`.
- O executável `Zeus.Observer.exe` deve acompanhar a publicação para coletar o desempenho.

## Operações disponíveis

| Método | Caminho | Retorno / corpo |
| --- | --- | --- |
| GET | `/api/health` | Estado do backend |
| GET | `/api/diagnostics` | `HardwareSnapshot`, com cache de cinco minutos e prazo de 55 segundos |
| GET | `/api/performance` | `PerformanceObservation`, com cache de quatro segundos e prazo de 25 segundos |
| GET | `/api/history` | Até 200 sessões locais |
| POST | `/api/personalization/preview` | `{layoutId}`; retorna plano, limitações e `wallpaperDataUrl` |
| POST | `/api/personalization/apply` | `{previewId}`; confirmação de uma prévia válida e de uso único |
| POST | `/api/personalization/revert` | `{transactionId}`; restauração verificada e protegida contra mudanças externas |

JSON usa camelCase. Estados de resultado/histórico: `applied`, `restored`, `blocked`, `needs-review`. Dados ausentes no diagnóstico/performance continuam ausentes; não são substituídos por medições fictícias.

## Personalização realmente implementada

Os quatro presets permitem somente papel de parede estático próprio, em todos os monitores, com ajuste Preencher. O BMP mostrado na confirmação é o mesmo aplicado. As prévias expiram em cinco minutos e não podem ser reutilizadas. Dock, relógio, widgets e modificações de shell não são instalados por este backend.

O `UserOptimizationService` existente salva imagens anteriores, hashes, posição e estado durável antes da mutação; verifica a aplicação; tenta compensar falhas; e impede que a restauração sobrescreva alterações externas. Uma interrupção aparece no histórico para revisão/restauração. Apresentações de slides e estados sem imagem de backup válida são bloqueados.

Dados ficam em `%LocalAppData%/Zeus/desktop-ui`, separados do histórico anterior do aplicativo WPF. Não são enviados a serviços externos. A alteração de papel de parede não exige administrador.

## Limites de validação

O projeto foi compilado localmente sem avisos nem erros. Na integração de 10/10/2026, inventário/desempenho reais e endpoints foram verificados; chamadas sem token ou com Origin foram rejeitadas. Uma imagem temporária foi aplicada e verificada no monitor conectado, a repetição da prévia foi bloqueada, e o fundo anterior foi restaurado e verificado. O histórico registra a sessão como `restored`. Evidências locais em `artifacts/desktop-ui-api-results.json`. A interface Electron também foi aberta e conferida, com CPU real visível e prévia BMP exata, sem erros de página.
