# ZEUS — lacunas de implementação identificadas

Revisão de código em 2026-10-09. Esta lista orienta continuidade e não declara concluídas as fases 2–12. Testes administrativos pendentes são separados de recursos ainda ausentes. O pacote original `ZEUS-Testes` permanece preservado; o desenvolvimento segue na branch `feat/zeus-windows-mvp`.

## Próximas capacidades

| Fase | Lacuna confirmada | Base existente e próximo trabalho |
| --- | --- | --- |
| 3/9 | Atribuição de I/O a disco/volume e comprovação de conflito de armazenamento ainda ausentes | A contabilidade de leitura/escrita/outras transferências por processo foi implementada com PID+início, intervalo próprio, lista independente até 30, histórico, exportação e comparação com cobertura. Inclui rede/dispositivos; médias das taxas por amostra não são vazão agregada do período. Atribuição física e conflito real precisam de evidência adicional. |
| 3 | O Observador não isola cada provedor bloqueante em um processo com prazo total | `WindowsPerformanceProbe.ReadCounterRows` possui timeout WMI, mas consultas sequenciais e chamadas nativas não garantem deadline por coletor. Implementar fronteira interrompível, estado/duração por coletor, resultados parciais e teste de ausência de auxiliares órfãos. |
| 9 | OBS ainda é identificado por processo/engine, sem estado real de stream/gravação, frames ou codec | `ActivityContextDetector`: integração local opcional somente leitura com API oficial OBS, credenciais sem exposição, timeout e deltas por sessão. Sem conexão/dados, permanecer indisponível. Não alterar configuração nem reiniciar OBS. |
| 7 | Instalação de driver é confirmada pelo registro WUA, sem confirmação separada do driver ativo | `DriverInstallVerificationPolicy`, `WindowsDriverRollback.ReadDriverState`: conservar DeviceID/INF antes/depois, associação por dispositivo e evidência posterior ao reinício; pacote registrado não equivale a driver ativo. |
| 8 | Falta ensaio administrativo real da sequência reparo → SCAN e do reinício quando solicitado | O histórico oferece “Verificar após reparo (SCAN)” para reparos concluídos elegíveis; sessão independente, estados estruturados DISM/SFC e vínculo durável no recibo antes do auxiliar iniciar e no SQLite. Recuperação conserva vínculo ou informa conflito. Comparação descreve a leitura posterior; sem baseline confirmado, não afirma melhora nem causalidade. Ensaios reais de reparo e reinício permanecem pendentes. |
| 7 | Verificação criptográfica independente do pacote de driver ainda é delegada ao Windows Update | `PresentationModels`, `DriverSupportCatalog`: inspeção de material selecionado/exportado, hash, catálogo/assinatura e origem separados. Hash ou assinatura do INF isolado não autenticam o pacote completo. |
| 11 | Catálogo de layouts importáveis tem escopo `zeus-ui`; não representa temas completos do desktop Windows | `VisualLayoutCatalog`, wallpaper, preferências visuais, relógio e organizador: compor experiência do desktop com componentes suportados, prévia, compatibilidade e recuperação específicas. Docks/Start/shell de terceiros exigem integração explicitamente opcional e avaliação própria. |
| 12 | Distribuição de produção assinada depende de certificado e validação da release | `release-process.md`: não substituir certificado de produção por certificado de teste; manter pacote de desenvolvimento identificado até o caminho de assinatura/aceitação ser concluído. |

## Defeitos corrigidos nesta revisão

O motor formal passa a resolver candidata e pré-requisitos como conjunto, sem permitir que regra descartada suprima outra. Definições circulares/enumerações inválidas são rejeitadas; coleções são copiadas, e CPU/janelas inválidas ficam sem evidência. O contrato e os testes estão em [motor-regras-otimizacao.md](motor-regras-otimizacao.md).

## Evidência que ainda precisa ser produzida

A matriz administrativa em [validacao-windows.md](validacao-windows.md) continua exigindo cenários de ponto de restauração, reparos, instalação/reversão de driver em equipamento de laboratório e reinícios. Os testes automáticos, leituras reais e abertura do EXE não substituem esses cenários. Cada validação deve identificar o commit, as ações realizadas, estados anteriores/posteriores e limites observados.
