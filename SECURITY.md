# Segurança e recuperação

O ZEUS está em desenvolvimento. Distribuições de produção devem assinar interface, auxiliar e instalador e verificar a integridade de suas atualizações. Os builds atuais não possuem certificado Authenticode de produção.

## Execução

A interface usa permissões comuns. A manutenção é delegada ao auxiliar local após confirmação e UAC. O auxiliar permite somente IDs de ações conhecidas e um identificador de sessão; não recebe comandos livres, URLs ou caminhos de saída fornecidos pelo usuário.

Sessões administrativas são gravadas em diretório protegido de ProgramData. A implementação recusa caminhos com redirecionamento e diretórios preexistentes com propriedade inadequada. Usuários comuns podem ler os resultados, mas não devem poder substituir os comandos ou resultados administrativos.

Somente operações selecionadas são executadas. Atualizações de drivers, BIOS, overclock, limpeza de registro e desativação de segurança não fazem parte desta versão.

## Recuperação

Reparos de imagem/arquivos Windows exigem um ponto de restauração novo e confirmado. Falha ao criá-lo bloqueia esses reparos. Não reduzir o intervalo de criação nem alterar políticas para contornar um bloqueio.

Pontos de restauração não são backups dos documentos e podem expirar. Faça backups independentes antes de manutenção importante. Nem toda operação pode ser cancelada ou desfeita. Após iniciar o auxiliar, a interface deve aguardar sua conclusão em vez de encerrar ferramentas de reparo à força.

## Dados

Diagnósticos e histórico ficam localmente. O programa não envia dados para serviços de IA ou telemetria. Relatórios exportados podem conter nome do computador, nomes de dispositivos e usuário de entradas de inicialização; revise antes de compartilhar.

Logs de ferramentas Windows podem conter caminhos e dados do sistema. Resultados administrativos em ProgramData podem ser lidos por outros usuários locais. Não inclua senhas, tokens ou valores de variáveis de ambiente em relatórios ou issues.

## Validação

CI executa regras e diagnóstico de leitura. Não executa reparos, cria restore points ou modifica políticas de segurança. Antes de uso em produção, validar cenários de UAC, credenciais diferentes, Proteção do Sistema indisponível, falha de disco, interrupção e reinício em Windows de teste.

Para relatar problemas, descreva a versão, ação escolhida e comportamento observado. Revise qualquer log anexado para remover dados pessoais.
