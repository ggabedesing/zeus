# Segurança e recuperação

O ZEUS está em desenvolvimento. Builds atuais não possuem certificado Authenticode de produção. Distribuições de produção precisam assinar os executáveis e verificar a integridade de suas atualizações.

## Fronteira administrativa

A interface usa permissões comuns e delega manutenção ao auxiliar local após revisão e UAC. O auxiliar aceita somente ações conhecidas, GUID de sessão e, para drivers, GUID/revisão do Windows Update e aceite explícito de licença. Payloads têm limite de tamanho e recusam propriedades desconhecidas, repetidas, enums numéricos, comandos e caminhos arbitrários.

Ferramentas e módulos PowerShell são selecionados por caminhos fixos do Windows. Não há execução de scripts fornecidos pelo usuário. A manutenção é sequencial; um lock de arquivo exclusivo também impede dois auxiliares simultâneos. Reparos já iniciados não são encerrados por um cronômetro ou pelo fechamento da interface.

Sessões administrativas ficam em `ProgramData/Zeus/Sessions/<GUID>`, com propriedade Administrators/SYSTEM e ACL protegida. Usuários comuns podem ler os relatórios, mas não escrever nos resultados administrativos. O armazenamento recusa redirecionamentos e diretórios preexistentes inadequados. Logs podem ser lidos por outros usuários locais.

## Recuperação por tipo de alteração

- Reparos DISM/SFC e instalação de drivers exigem um ponto de restauração novo, identificado e confirmado antes da ação. Falha na preparação bloqueia essas ações; limites de criação e políticas não são modificados.
- Antes de instalar drivers, PnPUtil exporta os drivers existentes para a sessão protegida. Falha de exportação bloqueia a instalação. O Windows Update consulta novamente a identidade exata; BIOS e firmware são excluídos. Backup de drivers e ponto de restauração não garantem recuperação de toda incompatibilidade.
- Preferências visuais, plano de energia e entradas HKCU Run guardam o estado anterior antes da alteração. Restaurar verifica conflitos e preserva mudanças posteriores. A proteção de entradas de segurança/backup/sincronização é heurística e não substitui a revisão do usuário.
- Temporários selecionados são guardados em recuperação local, com validação de caminhos e conteúdo, journal durável e restauração sem sobrescrever arquivos novos. Guardar não libera espaço. Exclusão definitiva exige revisão separada e é irreversível.
- O auxiliar salva resultados parciais antes de ações que podem reiniciar. Recibos locais permitem recuperar relatórios após interrupção; eles nunca autorizam execução nem repetição automática de comandos.

Pontos de restauração não são backups de documentos e podem expirar. A verificação offline do Defender pode reiniciar imediatamente; sua conclusão deve ser conferida na Segurança do Windows após o reinício. A interface exige revisão específica e confirmação de acesso à recuperação do BitLocker, quando aplicável.

O programa não desativa antivírus, serviços de segurança, Windows Update ou políticas do dispositivo. Não altera firmware, overclock nem limpa o registro. Preferências de uso orientam recomendações; não escolhem automaticamente um plano de energia ou uma lista de serviços para desligar.

## Dados

Diagnóstico e histórico permanecem locais. O aplicativo não envia inventário a IA ou telemetria. A consulta de drivers e downloads usa os serviços configurados no Windows Update. Relatórios exportados podem conter nome do computador, dispositivos, nomes de usuários e processos; revise antes de compartilhar. Comandos de inicialização e variáveis de ambiente não integram a exportação.

Logs de ferramentas Windows podem conter caminhos e dados pessoais. Não inclua senhas, tokens ou valores de variáveis de ambiente em relatórios ou issues.

## Validação

O CI Windows usa uma máquina virtual descartável. Além de leituras reais e renderização da interface, testes criam/restauram entradas HKCU próprias, aplicam/restauram efeitos visuais e verificam uma sessão administrativa isolada. Não executa reparos, instala drivers, cria pontos de restauração, agenda verificação offline ou reinicia a máquina.

Antes de produção, execute a [matriz de validação Windows](docs/validacao-windows.md) em máquinas de teste com backups. Ausência de falhas em CI não comprova compatibilidade de todos os equipamentos ou ganho de desempenho.

Para relatar um problema, descreva versão, ação e resultado observado. Revise logs anexados para remover dados pessoais.
