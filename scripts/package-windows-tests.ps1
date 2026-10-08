param([string]$OutputRoot = '')

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repository 'artifacts' }
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$staging = Join-Path $OutputRoot ('.zeus-tests-' + [guid]::NewGuid().ToString('N'))
$sourceArchive = Join-Path $staging 'source.zip'
$package = Join-Path $staging 'ZEUS-Testes'
New-Item -ItemType Directory -Force $staging | Out-Null
Push-Location $repository
try {
    $source = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $source -notmatch '^[0-9a-f]{40}$') { throw 'Commit de origem indisponivel.' }
    if (@(git status --porcelain).Count -ne 0) { throw 'O pacote de testes exige codigo commitado, sem alteracoes pendentes.' }
    git archive --format=zip "--output=$sourceArchive" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel exportar o codigo para testes.' }
    Expand-Archive $sourceArchive -DestinationPath $package
    foreach ($required in @('scripts/test-on-windows.ps1', 'scripts/iniciar-testes.cmd', 'global.json', 'Zeus.slnx')) {
        if (!(Test-Path (Join-Path $package $required) -PathType Leaf)) { throw "Arquivo necessario ausente: $required" }
    }
    $launcher = '@echo off' + "`r`n" + 'call "%~dp0scripts\iniciar-testes.cmd"' + "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $package 'INICIAR-TESTES.cmd'), $launcher, [System.Text.Encoding]::ASCII)
    $guide = @'
TESTES DO ZEUS NO SEU WINDOWS

1. Extraia a pasta ZEUS-Testes inteira antes de executar.
2. Abra INICIAR-TESTES.cmd e autorize o pedido de administrador do Windows.
3. Aguarde os testes. Se necessario, o iniciador baixa o SDK oficial da Microsoft,
   confere SHA-512 e guarda as ferramentas localmente. E preciso acesso a Internet.
4. O resultado fica em artifacts\TestResults, com relatorio.json, logs, TRX e telas.

A suite abre a interface, coleta hardware/carga reais e testa limpeza/recuperacao
usando arquivos proprios. Cria/restaura entradas de inicializacao de teste e muda
temporariamente efeitos visuais, restaurando o estado anterior ao final.

Os testes nao executam reparos do Windows, instalacao de drivers, verificacao
offline, reinicio nem exclusao dos seus documentos. Esses fluxos administrativos
tem revisao propria no aplicativo. Veja docs\validacao-windows.md.

Para rodar o aplicativo, use o pacote zeus-win-x64.zip e Zeus.Desktop.exe.
'@
    [System.IO.File]::WriteAllText((Join-Path $package 'LEIA-ME.txt'), $guide, [System.Text.UTF8Encoding]::new($true))
    $hashes = [ordered]@{}
    foreach ($file in @(Get-ChildItem $package -File -Recurse | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($package.Length + 1).Replace('\', '/')
        $hashes[$relative] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{ sourceCommit = $source; createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); sha256 = $hashes } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $package 'source-info.json') -Encoding utf8
    $archive = Join-Path $OutputRoot 'zeus-testes-windows.zip'
    Compress-Archive -Path $package -DestinationPath $archive -Force
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  zeus-testes-windows.zip" | Set-Content "$archive.sha256" -Encoding ascii
    Write-Output "Pacote de testes: $archive"
} finally {
    Pop-Location
    if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
