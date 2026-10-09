# Registro WUA e estado do driver por dispositivo

O ZEUS conserva evidência separada do pacote Windows Update e da associação do driver aos dispositivos. ProviderConfirmed continua confirmando o registro exato WUA; não autentica o pacote nem prova driver em uso, funcionamento ou ganho.

## Associação

DriverHardwareID é um ID de hardware ou compatibilidade, não InstanceId. A oferta é reconsultada pela identidade exata e origem antes da instalação; esse ID é comparado por igualdade ordinal sem distinção de caixa com HardwareID/CompatibleID de Win32_PnPEntity. Todas as correspondências são conservadas, sem escolher por título/modelo. Fontes: [WUA](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iwindowsdriverupdate-get_driverhardwareid) e [PnP](https://learn.microsoft.com/pt-br/windows/win32/cimwin32prov/win32-pnpentity).

Cada instância conserva DeviceID, Present, InfName, DriverVersion e DriverProviderName; INF/versão/fornecedor são lidos de [Win32_PnPSignedDriver](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/aa394354(v=vs.85)). A documentação desse provedor é histórica; os testes locais também verificam a associação nesta instalação Windows. IsPresent não equivale a funcionamento correto ou driver carregado em todos os níveis da pilha.

Antes do download, uma captura completa com presença/INF/versão é obrigatória. Ausência ou leitura parcial gera bloqueio NotStarted. Instâncias anteriores ficam congeladas para After e Latest: dispositivo novo não substitui o antigo. Resultado desconhecido, dispositivo ausente, versão ausente e multiplicidade continuam explícitos. Mesmo mudança de INF/versão não identifica conclusivamente o pacote WUA, que não fornece esses metadados nesta API.

## Checkpoints e histórico

O auxiliar cria arquivo JSON com nome fixo derivado de GUID/revisão da oferta, dentro da sessão protegida. Antes da operação grava Before com Flush(true) e substituição atômica; depois grava After. Não envia esse documento pelo stdout, cuja prévia é limitada. Falha posterior de captura/gravação não elimina os marcadores de resultado/reinício WUA e preserva Before. O arquivo secundário ainda exige revisão manual se o auxiliar cair antes de anexá-lo ao relatório.

Reconsultar pacotes e drivers ativos é somente leitura. Preserva Before/After, acrescenta Latest e salva inclusive observação inconclusiva. Outcome original da instalação permanece; uma leitura atual não transforma tentativa falha em instalação bem-sucedida. A mensagem original não acumula descrições; eventos de consulta ficam no registro de atividades. Reinício é manual e sua ocorrência não é comprovada por esta leitura.

SQLite esquema 7 acrescenta active_driver_json, com migração transacional e registros antigos nulos. JSON da exportação usa esquema 12. O limite conjunto é 256 KiB em UTF-8 compacto, com o mesmo escaping do armazenamento. Se necessário, Latest é resumido como incompleto, preservando Before/After; em casos extremos After e depois Before também ficam explicitamente omitidos no resumo. Identidades não são truncadas para fingir comparação. Capturas iniciais integrais ficam no checkpoint protegido quando sua gravação foi confirmada.

## Limites de coleta e aceitação

A reconsulta independente usa processo isolado: 30 segundos, 128 KiB por fluxo, árvore encerrada e espera limitada em cancelamento/timeout. Usuário cancelar propaga cancelamento; timeout interno fica desconhecido. Consultas WMI usam oito segundos, até 5.000 registros e 64 correspondências. Dentro do script de instalação, as consultas compartilham o auxiliar e os timeouts WMI; não há garantia adicional de prazo total interrompível por coletor nesse caminho, e o ZEUS não mata uma instalação já iniciada.

Testes verificam parser, associação real somente leitura, instância congelada mesmo com ID de hardware diferente, dispositivo ausente, cancelamento, tamanho do histórico, migração/backup e sintaxe do script real pelo parser PowerShell sem executar instalação. Não substituem ensaio administrativo real de instalação/reversão/reinício em laboratório. Hash/assinatura independente do pacote e identidade conclusiva da oferta com o driver em uso permanecem pendentes.
