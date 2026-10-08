# Validação do ZEUS em Windows

Esta matriz distingue implementação, teste automatizado e aceitação das alterações administrativas em PCs de teste. Um item implementado não deve ser anunciado como validado em hardware sem evidência correspondente.

## Testes automatizados

| Área | Evidência exigida pelo workflow |
| --- | --- |
| Compilação | Todos os projetos Release compilados no Windows |
| Regras | Todos os projetos de testes executados; zero falhas e zero testes ignorados no runner Windows |
| Limpeza | Seleção, conteúdo alterado, conflitos, journal, interrupções e proteção de caminhos |
| Inicialização | Entrada HKCU exclusiva de fixture desativada/restaurada, tipos preservados e conflitos protegidos |
| Preferências | Efeitos do usuário aplicados/restaurados; perfil não troca energia sozinho |
| Inventário e carga | CPU, RAM e volumes reais; contadores nativos de CPU/memória/processos |
| Auxiliar | Argumentos inválidos rejeitados antes de operação e armazenamento administrativo protegido |
| Persistência SQLite | Migrações transacionais, integridade, sessões/etapas, preferências, eventos e importação idempotente sem remover JSON de origem |
| Interface | Aplicação WPF real com inventário, oito áreas e três temas capturados em PNG |
| Publicação | Pacote autocontido com interface, auxiliar e dependências |

O relatório de `.validation/status.json` relaciona o commit, execução do Actions, contagens de testes, resultados por etapa e capturas. `accepted=true` refere-se **somente a esta aceitação automatizada**. As limitações de hardware e operações não executadas também constam nesse relatório.

### Execução local recebida

Em 8 de outubro de 2026, o usuário enviou o relatório da suíte executada no seu PC principal para o commit `c9404f0d473e34ef5a7b6a9e62d04fb9ecfc2b2a`: **203 testes aprovados, zero falhas e zero testes ignorados**, nos seis projetos. O relatório registra compilação Release concluída, execução com administrador e download local do SDK oficial com SHA-512 conferido. As somas por projeto e os resultados das etapas foram conferidos.

O [resumo da evidência](evidencias/pc-local-2026-10-08.json) preserva o commit e os resultados, sem caminhos pessoais. O relatório lista 12 capturas; os arquivos dessas capturas locais não foram recebidos para revisão. A versão do Windows e a configuração física não constam no relatório. Esse resultado confirma a suíte automatizada nessa execução local e mantém pendentes os cenários administrativos abaixo.

## Aceitação administrativa em máquinas de teste

Use Windows 11 suportado, snapshots quando disponíveis e backups independentes. Guarde a versão/commit, ação, relatório e resultado após reiniciar. Execute cada cenário separadamente.

| Cenário | Resultado esperado | Estado |
| --- | --- | --- |
| Usuário comum cancela UAC | Nenhum comando iniciado; etapas canceladas no histórico | Pendente de Windows interativo |
| UAC com credenciais de outro administrador | Relatório protegido legível para o usuário original; suas preferências continuam no seu HKCU | Pendente |
| Proteção do Sistema desativada | Reparos/drivers bloqueados, motivo e log preservados | Pendente |
| Limite de criação de ponto atingido | Sem alteração de política; novo reparo bloqueado | Pendente |
| Ponto novo disponível | Identidade confirmada antes de iniciar DISM/SFC ou driver | Pendente |
| Verificações DISM/SFC | Saída real registrada; sucesso do comando não promete ausência de corrupção | Pendente |
| Reparos DISM/SFC | Sem encerramento por timeout; retorno e reinício solicitados registrados | Pendente |
| Defender ativo / outro antivírus | Comandos só quando o Defender está em modo normal; políticas preservadas | Pendente |
| Defender offline e BitLocker | Revisão específica; recuperação de sessão após reinício; resultado conferido no Windows | Pendente |
| SSD e HDD | Mecanismo nativo escolhe a operação; não força desfragmentação de SSD | Pendente em mídia física |
| Oferta de driver desaparece ou muda | Identidade reconsultada; instalação bloqueada sem correspondência | Pendente com oferta real |
| Licença de driver e backup | Aceite por candidato; exportação confirmada antes de instalar; falha bloqueia | Pendente com oferta real |
| Atualização de driver e reversão | Instalação oficial registrada; dispositivo validado após reinício e recuperação ensaiada | Pendente |
| Falha de gravação / interrupção | Estado incompleto indicado; relatórios parciais recuperados sem repetir ações | Fixtures de journal e armazenamento aprovadas em Windows; interrupção real do auxiliar pendente |
| PC com pouca RAM e armazenamento limitado | Medir consumo do ZEUS e comparar tarefa equivalente antes/depois | Pendente em equipamento físico |
| Assinatura e distribuição | Authenticode, hash e entrega do pacote verificados | Hash implementado; assinatura de produção pendente |

## Recuperação disponível ao usuário

Na área Limpeza, selecione a sessão guardada e restaure os arquivos. Se o destino já tiver um arquivo novo, ele é preservado. Arquivos excluídos definitivamente não podem ser recuperados pelo ZEUS.

Em Perfil e plano ou Histórico, use “Restaurar estado anterior” para preferências, energia e entradas de inicialização. Alterações posteriores conflitantes são preservadas.

Para falhas do Windows após reparo/driver, use as opções de Recuperação do Windows e o ponto criado. A exportação de drivers está na pasta `driver-backup` da sessão administrativa. Essas rotas precisam ser ensaiadas em uma máquina de teste antes de distribuir para produção.

## Conferência no computador principal

O pacote `zeus-testes-windows.zip` permite executar a suíte automatizada no próprio computador: extraia a pasta inteira e abra `INICIAR-TESTES.cmd`. Autorize o pedido de administrador e aguarde o relatório em `artifacts/TestResults/<sessão>/relatorio.json`. Se necessário, o iniciador baixa o SDK oficial e verifica SHA-512. Ele testa arquivos e entradas próprios e restaura as preferências visuais ao final. Os resultados ficam locais.

Para conferir também o uso normal da interface:

1. Extraia a pasta inteira do pacote do aplicativo `zeus-win-x64.zip` e abra `Zeus.Desktop.exe` com seu usuário comum. A pasta `tests` do pacote de testes contém os projetos, não a distribuição autocontida do aplicativo.
2. Aguarde “Diagnóstico concluído”. Confira CPU, memória, GPU e volumes; leituras ausentes devem trazer avisos.
3. Em Hardware e carga, use “Medir carga por 5 segundos” durante a tarefa que está lenta.
4. Em Inicialização, use “Atualizar inicialização” para ler as entradas. Em Limpeza, “Analisar temporários” apenas lista candidatos.
5. Teste os três temas e responda às perguntas do perfil; escolher um tema ou responder ao perfil não aplica ajustes ao Windows.
6. Exporte o JSON e confira os dados localmente. Revise nomes de computador, usuários e processos antes de compartilhar.

Esse roteiro da interface só lê dados e guarda preferências da interface. Reparos, desativação de inicialização, exclusão, efeitos do Windows, instalação de drivers e verificação offline exigem ações separadas e revisão. A suíte de fixtures e a conferência no PC principal não confirmam os cenários administrativos ainda pendentes na matriz acima.
