# Histórico de versões

## Não lançado

- A aba de drivers identifica separadamente o fabricante do dispositivo e o fabricante do driver, e oferece consulta de atualizações em fontes oficiais.
- A interface acompanha a ativação e a desativação do alto contraste do Windows enquanto está aberta.
- A versão do aplicativo é exibida na janela e compartilhada pelo pacote portátil, pelo manifesto de proveniência e pelo MSI.
- A geração do pacote interrompe a publicação se os executáveis e assemblies do aplicativo ou do auxiliar administrativo divergirem da versão solicitada.
- A instalação de driver do Windows Update é individual: selecionar mais de um candidato bloqueia a transação, e a confirmação detalha apenas o dispositivo escolhido.
- Candidatos sem fabricante/modelo identificáveis ou com data ausente, inválida, sentinela ou futura continuam visíveis como indisponíveis, mas não podem ser selecionados para instalação pelo ZEUS; o auxiliar repete a validação antes do download.
- A interface mostra a seleção de servidor WUA e, para serviços específicos, o `ServiceID`; o auxiliar exige a mesma seleção/identidade na reconsulta e bloqueia downloads quando a origem não pode ser reconhecida.
- O pacote portátil inclui este histórico de versões.
