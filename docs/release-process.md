# Processo de release do ZEUS

Pushes comuns continuam gerando builds de desenvolvimento nos artefatos do GitHub Actions. Uma release de produção só pode começar com uma tag `vMAJOR.MINOR.PATCH` apontando para um commit já presente em `main`, e só depois de passarem os jobs portável e Windows (suíte completa, diagnóstico, inicialização do aplicativo e ciclo do MSI).

## Preparação de assinatura

Configure no GitHub o ambiente `zeus-release`, com revisores obrigatórios antes do uso, e adicione nele:

- Secret `ZEUS_SIGNING_PFX_BASE64`: certificado Authenticode de assinatura de código, com chave privada exportável em PFX codificado em Base64.
- Secret `ZEUS_SIGNING_PFX_PASSWORD`: senha do PFX.
- Variable `ZEUS_SIGNING_CERTIFICATE_THUMBPRINT`: thumbprint SHA-1 do certificado público esperado. O arquivo é assinado com SHA-256; o thumbprint serve somente para fixar a identidade do certificado.

O fluxo valida validade temporal, chave privada, EKU `Code Signing`, thumbprint fixado e confiança Authenticode. Ele não aceita certificado inválido, build sujo, versão diferente, arquivo divergente do manifesto, falha de timestamp, assinatura ausente ou erro no MSI. As chaves ficam apenas no ambiente protegido do GitHub Actions. O PFX é importado temporariamente no repositório `CurrentUser\My` para o SignTool selecionar o certificado pelo thumbprint; a senha não é passada como argumento ao SignTool. Certificados novos que vieram do PFX são removidos em `finally`, inclusive quando a assinatura falha, sem apagar entradas que já existiam. As variáveis Base64 e de senha são removidas do ambiente do processo antes de iniciar as ferramentas externas. A cópia de trabalho assinada fica em pasta isolada; falhas não alteram o payload original.

O Windows SDK SignTool assina os executáveis e assemblies próprios, carimba com RFC 3161/SHA-256, confere cada assinatura, abre e encerra o ZEUS, recalcula o manifesto SHA-256 após assinar, constrói o MSI a partir dos arquivos assinados e assina/verifica também o MSI. O mesmo runner executa instalação, atualização, abertura, preservação de dados e desinstalação do MSI assinado.

## Revisão e publicação

Depois da validação, o workflow cria uma **release em rascunho** com o ZIP portátil assinado, o MSI assinado, hashes SHA-256 e `build-info.json`. Uma pessoa deve revisar o certificado, os hashes, as notas e os artefatos e publicar o rascunho manualmente. Isso mantém a checagem de atualização do aplicativo ligada somente a releases estáveis publicadas.

O fluxo não cria nem envia tags. Não há certificado de produção configurado neste repositório neste momento; sem os segredos e a variável acima, o job encerra antes da compilação/assinatura e nenhum rascunho é criado. Os comandos seguem as recomendações do [SignTool e timestamp Authenticode da Microsoft](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures), e os valores do certificado são fornecidos por [secrets de ambiente do GitHub Actions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
