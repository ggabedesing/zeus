# Zeus PC — aplicativo simples para Windows

Esta interface veio do pacote `zeus-pc (1).zip` fornecido pelo usuário. Os arquivos originais e os dois ZIPs foram preservados. O código foi integrado ao repositório Zeus para aproveitar o diagnóstico e as transações do Windows existentes.

## Usar

Abra `release/Zeus-PC-0.2.0-Windows.exe`. É um aplicativo portátil: não requer Node.js ou .NET instalado, conta, navegador ou execução como administrador. O primeiro diagnóstico pode levar alguns segundos. Dados que o Windows não fornece aparecem como indisponíveis.

## Funções desta entrega

- Início e Meu PC: inventário e amostragem reais do Windows, sem números de demonstração.
- Aparência: seleção de estilos, prévia explicada, confirmação e aplicação de um papel de parede estático gerado pelo Zeus.
- Histórico: alterações registradas e reversão quando o estado atual permite restaurar o backup.
- Modo Técnico: detalhes e avisos das fontes, sem sobrecarregar a experiência normal.

Dock, barra alternativa, Rainmeter, animações globais e outras ferramentas externas não são instalados por esta versão. A prévia visual não deve ser confundida com um shell completo já aplicado. Os motores e telas anteriores permanecem no repositório; a nova interface ainda não incorpora todos os recursos administrativos do aplicativo anterior.

## Construir

Na raiz do repositório execute `scripts/build-desktop-ui.ps1`. Precisa de .NET 10 SDK e Node.js. O resultado é um EXE portátil em `src/Zeus.DesktopUI/release`. Dependências são fixadas pelo package-lock. O pacote inclui o serviço nativo e o coletor Observer.

## Arquitetura

React é executado em Electron com sandbox e contextIsolation, sem Node.js na página. O preload expõe seis métodos específicos. O processo principal valida a origem e o formato dos argumentos, chama apenas rotas permitidas e mantém o token fora da página. Links externos e navegação são bloqueados. Uma política CSP impede conexões de rede pelo renderer.

O serviço .NET escuta exclusivamente `127.0.0.1`, com porta aleatória e token gerado a cada abertura. Ele encerra com o aplicativo. Não existe endpoint para executar shell/PowerShell ou escolher um caminho arbitrário. O diagnóstico usa provedores internos já existentes; a personalização usa o motor com captura, journal, verificação e reversão.

Dados de personalização ficam separados em `%LocalAppData%\Zeus\desktop-ui`. O estado durável permite identificar alterações interrompidas; não declare uma alteração aplicada sem confirmação do Windows.

O executável desta entrega não tem assinatura digital. Assinatura e instalador automático são etapas de distribuição ainda pendentes.
