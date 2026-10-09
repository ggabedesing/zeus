# Verificar após reparo

O histórico do ZEUS oferece uma nova varredura separada para sessões completas, sem erro global, com RepairWindowsImage ou RepairSystemFiles concluído. Passos cancelados/falhos não geram esse plano. A revisão mostra a origem, ações, pedido UAC e orientação de reinício manual. Nenhum reparo é incluído no plano posterior.

| Reparo registrado | Nova leitura |
| --- | --- |
| RepairWindowsImage | DISM ScanWindowsImage, /ScanHealth |
| RepairSystemFiles | SFC VerifySystemFiles, /verifyonly |

SFC /verifyonly verifica sem reparar; DISM /ScanHealth procura corrupção. Referências: [SFC oficial](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc) e [verificação de imagem DISM](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image?view=windows-11).

## Evidência e limites

O auxiliar conserva ImageHealthState e SystemFilesState tipados apenas para suas respectivas varreduras. Relatórios antigos ficam Unknown. Exit code zero, texto de conclusão de reparo ou ausência de log não se tornam estado saudável. Falha, sessão incompleta, resultado ausente ou provedor não confirmado permanecem desconhecidos.

A comparação liga comando de reparo anterior e varredura posterior. Sem uma baseline de integridade comprovada, não descreve melhora nem usa “ainda detectou”. Mesmo uma varredura explícita sem corrupção descreve aquele mecanismo e horário; não prova causalidade, saúde completa ou ganho de desempenho. O ZEUS não reinicia automaticamente nem verifica que o reinício solicitado foi realizado.

## Recuperação e armazenamento

A API tipada cria plano somente SCAN. O recibo local guarda VerificationOfSessionId antes de iniciar o auxiliar; é metadado do usuário, não autorização nem assinatura de origem. Reiniciar o aplicativo não repete operações. Recibo sem resultado cria passos desconhecidos ligados à origem; relatório vazio de inicialização continua erro sem vínculo inválido. Ao reconciliar, vínculo recuperado tem prioridade, vínculo já salvo é fallback, e conflito mantém sessão incompleta e recibo para revisão.

SQLite esquema 6 acrescenta estados e vínculo por migração transacional. Backups de esquema 5 são migrados em staging antes da restauração; arquivos originais e cópia de segurança são preservados. Exportação JSON esquema 11 conserva os campos opcionais. Histórico parcial pode omitir a sessão de origem: nesse caso a interface informa comparação indisponível. A sessão original não é alterada para declarar reparação confirmada.

## Aceitação necessária

Testes controlados cobrem mapeamento, cronologia, planos exclusivos, estados desconhecidos, associação, recibos, reconciliação e banco/backup. Interface real pode ser renderizada com cartão de histórico controlado sem executar manutenção. Esses testes não substituem ensaio de reparo real em laboratório, com proteção de recuperação, evidência anterior/posterior e reinício solicitado. A validação de cada pacote está em validacao-windows.md.
