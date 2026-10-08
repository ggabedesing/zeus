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
| Papel de parede | prévia local, limites de formato/tamanho/caminho, backup SHA-256, aplicação verificada, restauração pelo histórico, bloqueio de mudança externa e preservação de slideshow/papéis distintos por monitor |
| Inventário estendido | drivers, PnP, rede, processos, serviços, tarefas, software, eventos recentes, Secure Boot, TPM e slots de memória declarados pelo firmware; canais não são inferidos e fontes opcionais podem permanecer indisponíveis |
| WinGet | consulta de leitura e testes de prévia/argumentos seguros; atualização individual só após confirmação, nova checagem de ID/versões, execução interativa e verificação posterior. Nenhum aceite automático de termos. Tentativas sem confirmação são registradas e bloqueadas para repetição; não há garantia genérica de reversão. A aceitação automatizada nunca instala pacote. |
| Atualizações do Windows | busca WUA sob demanda para software pendente, com origem/políticas configuradas; estados parciais e falhas permanecem explícitos; a busca não baixa nem instala. Em 08/10/2026 às 16:52 UTC, uma consulta real deste PC terminou completa e retornou zero atualizações de software pendentes. Isso cobre somente a fonte configurada e não avalia drivers nem atualizações ocultas. |
| DISM ScanHealth | estados explícitos do repositório de componentes são classificados; código de saída sem uma mensagem conhecida permanece desconhecido e exige revisão do log |
| SFC /verifyonly | parser de novas linhas `[SR]` de CBS.log reconhece ausência/divergência/não reparável; código, log ou idioma sem evidência conhecida permanece desconhecido; nunca executa reparo nessa etapa. A tentativa de execução real neste PC foi cancelada no UAC antes de iniciar `sfc.exe`; falta validar a captura com um CBS.log produzido por uma execução real. |
| Separação de reparos | planos combinando SFC/DISM Scan e Repair são bloqueados na política, interface e fronteira do auxiliar |
| Observador | buffer circular limitado, política adaptativa pela maior carga percentual válida observada em CPU, processos, GPU, disco e rede (rede normalizada pelo enlace reportado; contadores indisponíveis não significam ociosidade), comparação descritiva antes/depois de CPU/RAM com cobertura de valores válidos sobre o total, picos médios por amostra da engine GPU mais ativa/atividade de disco e uso dedicado médio/ocupação por adaptador (com número de amostras disponíveis), tráfego médio antes/depois em bytes por segundo e cobertura por adaptador de rede, transferência e latência média de leitura antes/depois por unidade de disco com coberturas separadas, cobertura de presença de jogo/OBS e encoder associado ao OBS em cada período (heurística, sem confirmar partida ou transmissão), sinal de ocupação dedicada GPU sustentada (mínimo de 5 leituras válidas em 10 s; é indicador de ocupação, não diagnóstico de pressão), contadores locais de GPU/disco/rede e memória dedicada/compartilhada/comprometida/capacidade dedicada quando disponíveis, PID de engine associado ao nome apenas quando corresponde ao processo amostrado, heurísticas de jogos/OBS sobre processos acessíveis e utilização de `VideoEncode` quando ligada ao PID do OBS, sessões/referência SQLite e exportação; pressão de VRAM permanece desconhecida porque os contadores agregados disponíveis não incluem orçamento por processo ou evidência de paginação; telemetria de codec/quadros e detecção confiável de partida/transmissão continuam pendentes |
| Latência ICMP | teste sob demanda para IP/host informado, resolução DNS limitada a 5 s, cinco tentativas com timeout de 1 s, respostas/status/latência registrados no SQLite; ausência de resposta não é classificada como falta de Internet nem como perda geral de pacotes |
| Auxiliar | Argumentos inválidos rejeitados antes de operação e armazenamento administrativo protegido |
| Persistência SQLite | Migrações transacionais v1→v2→v3, integridade, sessões/etapas de manutenção e desempenho, amostras/referência, preferências, eventos, importação idempotente sem remover JSON de origem, backup verificado e restauração confirmada com cópia de segurança do banco ativo e recuperação automática em falha |
| Interface | Aplicação WPF real com inventário, oito áreas e três temas capturados em PNG |
| Publicação | Pacote autocontido com interface, auxiliar e dependências |

O relatório de `.validation/status.json` relaciona o commit, execução do Actions, contagens de testes, resultados por etapa e capturas. `accepted=true` refere-se **somente a esta aceitação automatizada**. As limitações de hardware e operações não executadas também constam nesse relatório.

O inventário estendido inclui informações locais de rede (endereços, DNS, gateways e rotas) e os valores de proxy observados no Registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`: proxy manual, URL PAC, valor `AutoDetect` quando presente e exceções. O diagnóstico diferencia proxy manual desativado de configuração indisponível; ausência de um valor permanece desconhecida, não “desativada”. A URL PAC é sanitizada para remover credenciais, consulta e fragmento. Essa fonte não representa necessariamente o proxy efetivo de todo aplicativo: WinHTTP e configurações por aplicativo não são consultados. Nomes de drivers/dispositivos/programas/processos/serviços/tarefas, slots de memória declarados pelo firmware e metadados de eventos recentes também podem aparecer. Canais de RAM não são deduzidos da quantidade de módulos ou da frequência. O texto bruto dos eventos e linhas de comando dos processos são omitidos. A exportação completa pode revelar detalhes do computador; confira antes de compartilhar. Atualizações pendentes e integridade da imagem do Windows não são inferidas pelo primeiro diagnóstico, que não consulta atualizações online nem executa DISM/SFC. A consulta WinGet é uma ação separada e explícita na aba Atualizações e drivers; ela não instala pacotes. O Centro de Reparos classifica somente as frases conhecidas do `/ScanHealth /English`; isso descreve corrupção do repositório de componentes, não a saúde física do PC nem todo o estado do Windows. Saída ausente, conflitante ou não reconhecida fica como desconhecida.

### Execução local recebida

Em 8 de outubro de 2026, o usuário enviou o relatório da suíte executada no seu PC principal para o commit `c9404f0d473e34ef5a7b6a9e62d04fb9ecfc2b2a`: **203 testes aprovados, zero falhas e zero testes ignorados**, nos seis projetos. O relatório registra compilação Release concluída, execução com administrador e download local do SDK oficial com SHA-512 conferido. As somas por projeto e os resultados das etapas foram conferidos.

O [resumo da evidência](evidencias/pc-local-2026-10-08.json) preserva o commit e os resultados, sem caminhos pessoais. O relatório lista 12 capturas; os arquivos dessas capturas locais não foram recebidos para revisão. A versão do Windows e a configuração física não constam no relatório. Esse resultado confirma a suíte automatizada nessa execução local e mantém pendentes os cenários administrativos abaixo.

Na validação local do commit `2a4c718`, `Zeus.SmokeCheck` concluiu a coleta nativa e encontrou contadores de memória GPU para dois adaptadores, com leitura dedicada disponível nos dois. Isso confirma disponibilidade dos contadores neste computador, não pressão de VRAM; nenhum reparo ou restauração foi executado.

No commit `e68f904`, a nova leitura DXGI foi conferida no mesmo PC: dois adaptadores com contador de uso dedicado, um pareamento por LUID com capacidade dedicada e ocupação calculável; o adaptador virtual permaneceu sem porcentagem. Compilação Release sem avisos/erros, 62 testes de hardware aprovados e 39 testes de aceitação aprovados (1 teste administrativo ignorado). Ocupação é uma razão observada, não comprova gargalo ou pressão por si só.

Na validação local atual, `Zeus.SmokeCheck` coletou CPU, RAM, volumes, dados de desempenho nativos e inventário opcional de placa-mãe/BIOS/disco. Foram vistos dois adaptadores GPU, uso dedicado em dois e capacidade/ocupação correspondente em um. Nenhum jogo/OBS conhecido apareceu entre os processos acessíveis dessa amostra; isso não comprova que estejam fechados, e a atividade de encoder ficou indisponível. A detecção sintética cobre associação `VideoEncode` ao PID do OBS, sem afirmar transmissão ao vivo. Compilação Release sem avisos/erros, 64 testes de hardware aprovados e 39 testes de aceitação aprovados (1 teste administrativo ignorado). Nenhum reparo ou alteração de configuração foi executado.

Os coletores WMI de desempenho agora mantêm instâncias válidas já lidas se a enumeração for interrompida no meio, marcando a lista como parcial; uma falha antes da primeira instância mantém a métrica indisponível. Cancelamento continua sendo propagado e encerra a coleta.

Esta revisão compilou a solução Release sem avisos/erros; 66 testes de hardware passaram, incluindo os dois casos de preservação parcial/indisponibilidade. A aceitação Windows passou em 39 testes, com 1 teste administrativo ignorado. O smoke test nativo foi concluído sem executar reparos; nesta amostra a telemetria de encoder continuou indisponível.

A comparação de CPU e RAM agora informa cobertura válida por período e descarta valores percentuais fora de 0–100; campos opcionais preservam a leitura de comparações antigas. A revisão passou na compilação Release sem avisos/erros, em 67 testes de hardware e em 39 testes de aceitação Windows (1 teste administrativo ignorado). Um caso sintético valida coberturas 1/3 e 3/3 para CPU e amostras indisponíveis para RAM.

O backup e a restauração do SQLite passaram em teste de armazenamento: a restauração preservou preferências e atividades da cópia escolhida e criou uma cópia anterior verificável; uma versão de esquema futura foi recusada sem alterar o banco ativo. Uma falha simulada após a cópia de segurança e antes da aplicação confirmou a recuperação do estado anterior. A aceitação WPF confirmou os botões de backup e restauração. Compilação Release sem avisos/erros, 17 testes de armazenamento aprovados e 39 testes de aceitação aprovados (1 teste administrativo ignorado). O pacote `win-x64` autocontido e seu SHA-256 foram criados com sucesso em diretório temporário deste PC. A restauração é confirmada e reinicia a aplicação; a cópia de segurança fica em `%LOCALAPPDATA%\Zeus\recovery`. Ainda falta executar o fluxo completo pelos diálogos e verificar o reinício após uma restauração real.

O Observador agora resume ocupação dedicada sustentada da GPU apenas com pelo menos cinco leituras válidas distribuídas por dez segundos, e requer ocupação de 90% ou mais em ao menos 80% dessas leituras. O resultado é rotulado como sinal para investigar; não diagnostica pressão, gargalo ou impacto em jogos. Nesta revisão, 71 testes de hardware passaram, a compilação Release terminou sem avisos/erros e três testes de aceitação Windows (inventário nativo e interface WPF) passaram. O classificador foi verificado com séries sintéticas; uma tarefa longa real ainda precisa ser observada para validar o sinal em uso.

A comparação descritiva antes/depois inclui tráfego médio de rede por adaptador, transferência média e latência de leitura por unidade de disco, com coberturas válidas independentes por período; contadores ausentes continuam indisponíveis. Nesta revisão, 76 testes de hardware passaram, a compilação Release passou sem avisos/erros e a aceitação WPF passou em 39 testes, com um teste administrativo ignorado. As agregações foram verificadas com dados sintéticos; a comparação medida durante uma carga real continua dependendo de coleta no uso do PC.

A política de amostragem adaptativa agora usa a maior carga percentual válida entre CPU total, processos acessíveis, engines GPU, atividade de disco e tráfego de rede normalizado pela velocidade de enlace reportada. Fontes ausentes ou inválidas não são interpretadas como ociosidade e, sem qualquer sinal válido, mantém-se o intervalo de 10 s. Nesta revisão, 78 testes de hardware passaram e a compilação Release passou sem avisos ou erros; os novos casos cobrem GPU e disco com CPU indisponível, normalização de rede e contadores inválidos. Isso valida a política com fixtures, mas não substitui medição prolongada em cargas reais.

A comparação também resume a presença observada de processo de jogo conhecido, OBS e atividade `VideoEncode` associada ao PID do OBS em cada período, com coberturas separadas para contexto disponível e encoder conhecido. A leitura continua heurística: não prova partida ou transmissão ao vivo, e diferenças de contexto devem ser consideradas na interpretação. Nesta revisão, 79 testes de hardware passaram, a compilação Release terminou sem avisos ou erros e a aceitação WPF passou em 39 testes, com um teste administrativo ignorado. Os casos de contexto foram sintéticos; falta validar a apresentação durante uma tarefa real.

Os contadores atuais não permitem classificar pressão de VRAM: são agregados por adaptador e não incluem o orçamento de memória atribuído ao processo nem evidência de paginação. A interface agora conserva esse resultado como desconhecido tanto com ocupação alta quanto baixa. A documentação DXGI da Microsoft descreve o orçamento e o uso do processo como parâmetros relevantes e alerta para possível stutter quando o uso do processo ultrapassa o orçamento; isso não autoriza inferir pressão a partir da ocupação total do adaptador. Para uma classificação futura, será necessária uma fonte compatível de orçamento por processo e evidência temporal correlata. Fonte: [DXGI_QUERY_VIDEO_MEMORY_INFO](https://learn.microsoft.com/pt-br/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info).

A experiência de papel de parede agora tem prévia antes da confirmação, cópia do arquivo anterior com verificação SHA-256, aplicação/validação, registro no histórico e restauração bloqueada se outro app alterar a imagem depois. O fluxo aceita somente arquivo local fixo BMP/JPEG/PNG de até 32 MiB, imagem estática única compartilhada por monitores e sem slideshow configurado; os modos não suportados permanecem intactos. Os testes com plataforma simulada verificaram aplicar/restaurar, alteração externa, extensão enganosa, slideshow e configuração por monitor. O teste Windows de status COM foi somente leitura; não trocou o papel de parede deste PC. Nesta revisão, 27 testes de otimização do usuário passaram e 1 teste de alteração visual real permaneceu ignorado por exigir ativação explícita; o teste WPF passou e a compilação Release terminou sem avisos/erros.

## Diagnóstico de dependências de serviços

O inventário mantém separados o estado/partida consultados por `Win32_Service` e as dependências declaradas pela consulta local `Get-Service`. Se a segunda fonte falhar, dependências ficam desconhecidas sem invalidar o inventário dos serviços. A tela de Manutenção descreve relações, grupos, referências que não foram resolvidas no inventário e estados observados; não inicia, para ou altera serviços. Um serviço parado isoladamente não é classificado como defeito. Nesta revisão, passaram 74 testes de hardware, 3 testes focados de inventário/WPF e a compilação Release sem avisos ou erros; o teste WPF clica no botão e confere a lista produzida. A suíte focal também cobre dados desconhecidos, referências ausentes e grupos.

## Diagnóstico de dispositivos PnP

A tela de Manutenção agora interpreta um catálogo selecionado de códigos PnP documentados pela Microsoft, com orientação de consulta segura para o dispositivo. Códigos que não estão no catálogo, valores inválidos e inventário indisponível permanecem explícitos, sem inferir causa física. O código, o estado retornado pelo Windows e o nome do dispositivo continuam visíveis. Nada habilita dispositivos, altera Registro/firmware ou instala drivers. A compilação Release passou sem avisos/erros; 68 testes do núcleo e 3 testes focados de inventário/WPF passaram. Os testes cobrem códigos 10, 28, 43 e 52, além de códigos desconhecidos e indisponíveis.

## Padrões de eventos para investigação

O diagnóstico consulta até 20 eventos recentes de nível crítico, erro ou aviso em cada log System e Application. A tela agrupa repetições pelo log, provedor e ID, mostrando contagem, nível e horário mais recente; não interpreta uma repetição como causa. Mensagens brutas continuam fora da interface/exportação. “Nenhum evento retornado” fica separado de fonte indisponível, e nenhuma ocorrência não é tratada como prova de Windows saudável. Nesta revisão, a compilação Release passou sem avisos/erros, 72 testes do núcleo passaram e os 3 testes focados de inventário/WPF passaram com consulta nativa aos logs.

## Preparação para redefinição de rede

A aba Manutenção mostra os endereços IP, DNS, gateway e proxy que o inventário retornou, ou avisa quando os dados estão incompletos. Um JSON pode ser salvo em local escolhido pelo usuário como referência dos campos coletados; ele não é backup integral nem restauração automática, e pode conter dados internos. A navegação para Configurações do Windows exige confirmação de que o usuário revisou os dados e consegue recuperar configurações manuais, além de estar preparado para reinício e reconfiguração de VPN, switches virtuais e adaptadores. Uma confirmação final explica os efeitos. O ZEUS não executa, nem registra como executada, a redefinição; o Windows apresenta a decisão final. A Microsoft recomenda essa etapa como último recurso, pois ela remove/reinstala adaptadores, redefine configurações e pode alterar perfis para públicos. Nesta revisão, compilação Release sem avisos/erros e teste WPF aprovado; o teste confirma que a navegação permanece bloqueada até as duas confirmações, sem abrir Configurações nem alterar a rede. Fontes: [orientação de redefinição de rede da Microsoft](https://support.microsoft.com/pt-br/windows/experience/connectivity-networking/fix-ethernet-connection-problems-in-windows) e [URI oficial de Configurações](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).

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
