# Processo de release do ZEUS

Pushes comuns continuam gerando builds de desenvolvimento nos artefatos do GitHub Actions. Uma release de produção só pode começar com uma tag `vMAJOR.MINOR.PATCH` apontando para um commit já presente em `main`, e só depois de passarem os jobs portável e Windows (suíte completa, diagnóstico, inicialização do aplicativo e ciclo do MSI).

## Preparação de assinatura

Configure no GitHub o ambiente `zeus-release`, com revisores obrigatórios antes do uso, e adicione nele:

- Secret `ZEUS_SIGNING_PFX_BASE64`: certificado Authenticode de assinatura de código, com chave privada exportável em PFX codificado em Base64.
- Secret `ZEUS_SIGNING_PFX_PASSWORD`: senha do PFX.
- Variable `ZEUS_SIGNING_CERTIFICATE_THUMBPRINT`: thumbprint SHA-1 do certificado público esperado. O arquivo é assinado com SHA-256; o thumbprint serve somente para fixar a identidade do certificado.

O fluxo valida validade temporal, chave privada, EKU `Code Signing`, thumbprint fixado e confiança Authenticode. Ele não aceita certificado inválido, build sujo, versão diferente, arquivo divergente do manifesto, falha de timestamp, assinatura ausente ou erro no MSI. As chaves ficam apenas no ambiente protegido do GitHub Actions. O PFX é importado temporariamente no repositório `CurrentUser\My` para o SignTool selecionar o certificado pelo thumbprint; a senha não é passada como argumento ao SignTool. Certificados novos que vieram do PFX são removidos em `finally`, inclusive quando a assinatura falha, sem apagar entradas que já existiam. As variáveis Base64 e de senha são removidas do ambiente do processo antes de iniciar as ferramentas externas. A cópia de trabalho assinada fica em pasta isolada; falhas não alteram o payload original.

O Windows SDK SignTool assina os executáveis e assemblies próprios, carimba com RFC 3161/SHA-256, confere cada assinatura, abre e encerra o ZEUS, recalcula o manifesto SHA-256 após assinar, constrói o MSI a partir dos arquivos assinados e assina/verifica também o MSI. O mesmo runner executa instalação, atualização, abertura, preservação de dados e desinstalação do MSI assinado.

O job Windows normal também testa a fronteira de assinatura sem usar o certificado de produção: cria um certificado de código de teste, exporta/importa no repositório do usuário, assina uma cópia temporária do executável com SignTool pelo thumbprint e confirma que a assinatura identifica o certificado de teste. Em seguida remove a entrada temporária do repositório e os arquivos de teste. Este ensaio confirma a integração com o armazenamento e o SignTool; não substitui a assinatura, o timestamp ou a validação da cadeia de confiança de uma release real.

## Revisão e publicação

Depois da validação, o workflow cria uma **release em rascunho** com o ZIP portátil assinado, o MSI assinado, hashes SHA-256 e `build-info.json`. Uma pessoa deve revisar o certificado, os hashes, as notas e os artefatos e publicar o rascunho manualmente. Isso mantém a checagem de atualização do aplicativo ligada somente a releases estáveis publicadas.

No aplicativo, a consulta de versões é manual por padrão. A pessoa pode habilitar uma consulta automática no início do aplicativo, limitada a uma tentativa a cada 24 horas e com o horário persistido no SQLite para não repetir em cada abertura. A solicitação usa a API pública de releases do GitHub e, como qualquer conexão HTTP, revela o endereço IP ao GitHub; não envia inventário do computador. A resposta só apresenta a publicação oficial e nunca baixa, instala ou reinicia o PC. O instalador continua sob revisão manual até haver assinatura Authenticode de produção e um mecanismo de instalação/recuperação validado.

Cada pacote portátil inclui `SBOM.spdx.json` em formato SPDX 2.3, derivado dos manifests `.deps.json` das duas aplicações. O inventário lista projetos, pacotes NuGet e runtime packs, suas versões, metadados de licença conhecidos e relações `DEPENDS_ON`; `build-info.json` registra o hash do SBOM junto aos arquivos entregues. A validação falha se faltar uma biblioteca publicada, destino de relação ou identificador SPDX duplicado. O SBOM não afirma inventariar arquivos ou componentes externos que os manifests não declaram.

O fluxo não cria nem envia tags. Não há certificado de produção configurado neste repositório neste momento; sem os segredos e a variável acima, o job encerra antes da compilação/assinatura e nenhum rascunho é criado. Os comandos seguem as recomendações do [SignTool e timestamp Authenticode da Microsoft](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures), e os valores do certificado são fornecidos por [secrets de ambiente do GitHub Actions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
