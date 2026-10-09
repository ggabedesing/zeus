# Histórico de versões

## Não lançado

- A área Perfil e plano agora abre páginas oficiais do Windows para temas, cores, menu Iniciar, barra de tarefas, som e tela de bloqueio, deixando explícito que essas alterações são controladas pelo Windows e não são revertidas pelo ZEUS.
- O ciclo automatizado do MSI abre a janela principal da versão instalada após a atualização e valida o encerramento antes da desinstalação.
- A aba de drivers identifica separadamente o fabricante do dispositivo e o fabricante do driver, e oferece consulta de atualizações em fontes oficiais.
- A interface acompanha a ativação e a desativação do alto contraste do Windows enquanto está aberta.
- A versão do aplicativo é exibida na janela e compartilhada pelo pacote portátil, pelo manifesto de proveniência e pelo MSI.
- A geração do pacote interrompe a publicação se os executáveis e assemblies do aplicativo ou do auxiliar administrativo divergirem da versão solicitada.
- A instalação de driver do Windows Update é individual: selecionar mais de um candidato bloqueia a transação, e a confirmação detalha apenas o dispositivo escolhido.
- Candidatos sem fabricante/modelo identificáveis ou com data ausente, inválida, sentinela ou futura continuam visíveis como indisponíveis, mas não podem ser selecionados para instalação pelo ZEUS; o auxiliar repete a validação antes do download.
- A interface mostra a seleção de servidor WUA e, para serviços adicionais, o `ServiceID`; a instalação permite o ID oficial conhecido do Microsoft Update e bloqueia os demais. O auxiliar confirma modo e ID de serviço novamente antes da consulta exata.
- A política de origem WUA é centralizada para busca, seleção e transação; o resultado persistido da instalação do driver registra a fonte lógica selecionada/confirmada e seus limites conhecidos.
- A ficha e a confirmação de instalação explicam que o Windows Update valida hashes e assinaturas antes de instalar; o ZEUS não apresenta essa validação do Windows como verificação criptográfica independente do aplicativo.
- Após a instalação de um driver, o auxiliar consulta novamente a identidade exata no Windows Update e diferencia pacote confirmado, reinicialização pendente e resultado que precisa de revisão; a confirmação do pacote não declara que o dispositivo já está usando o driver.
- A interface de instalação de driver explica os três resultados e o histórico apresenta estado pendente como verificação pendente, sem sugerir que o comando ainda esteja rodando.
- O resumo da manutenção não apresenta uma sessão com verificação pendente como concluída; orienta conferir o histórico após a reinicialização solicitada.
- Instalações pendentes guardam a seleção lógica do Windows Update no SQLite versionado. Uma ação explícita pode reconsultar o pacote exato após reiniciar; confirmação atualiza o histórico, enquanto ausência/incompletude mantém a pendência sem reinstalação automática.
- A reconsulta de driver só começa quando o tempo de inicialização do Windows indica uma reinicialização posterior à sessão de instalação.
- O pacote portátil inclui este histórico de versões.
