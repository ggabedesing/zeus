# Histórico de versões

## Não lançado

- A aba de drivers identifica separadamente o fabricante do dispositivo e o fabricante do driver, e oferece consulta de atualizações em fontes oficiais.
- A interface acompanha a ativação e a desativação do alto contraste do Windows enquanto está aberta.
- A versão do aplicativo é exibida na janela e compartilhada pelo pacote portátil, pelo manifesto de proveniência e pelo MSI.
- A geração do pacote interrompe a publicação se os executáveis e assemblies do aplicativo ou do auxiliar administrativo divergirem da versão solicitada.
- A instalação de driver do Windows Update é individual: selecionar mais de um candidato bloqueia a transação, e a confirmação detalha apenas o dispositivo escolhido.
- Candidatos sem fabricante/modelo identificáveis ou com data ausente, inválida, sentinela ou futura continuam visíveis como indisponíveis, mas não podem ser selecionados para instalação pelo ZEUS; o auxiliar repete a validação antes do download.
- A interface mostra a seleção de servidor WUA e, para serviços adicionais, o `ServiceID`; a instalação permite o ID oficial conhecido do Microsoft Update e bloqueia os demais. O auxiliar confirma modo e ID de serviço novamente antes da consulta exata.
- A política de origem WUA é centralizada para busca, seleção e transação; o resultado persistido da instalação do driver registra a fonte lógica selecionada/confirmada e seus limites conhecidos.
- O pacote portátil inclui este histórico de versões.
