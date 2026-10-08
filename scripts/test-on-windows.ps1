#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [switch]$SkipBuild,
    [switch]$NonInteractive,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$requiredSdk = '10.0.401'
try {
    $sdkPin = (Get-Content -LiteralPath (Join-Path $repository 'global.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sdk.version
    if ($sdkPin -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão estável do SDK ausente em global.json.' }
    $requiredSdk = [string]$sdkPin
} catch {
    Write-Host ('Não foi possível ler a versão do SDK: ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}

function Test-ZeusAdministrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    } finally { $identity.Dispose() }
}

function ConvertTo-ZeusPowerShellLiteral([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Write-Host 'Esta suíte precisa ser executada no Windows do seu computador.' -ForegroundColor Red
    exit 2
}

$runId = [Guid]::NewGuid().ToString('D')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository ('artifacts\TestResults\' + $runId)
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (!(Test-ZeusAdministrator)) {
    if ($Elevated -or $NonInteractive) {
        Write-Host 'Os testes de ACL exigem um token de administrador. Execute este script como administrador; a suíte não foi iniciada.' -ForegroundColor Red
        exit 5
    }
    Write-Host 'O Windows solicitará administrador uma única vez para executar também os testes de permissões.' -ForegroundColor Cyan
    $powershell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $command = ''
    if (![string]::IsNullOrWhiteSpace($env:ZEUS_VALIDATION_DIR)) {
        $command += '$env:ZEUS_VALIDATION_DIR = ' + (ConvertTo-ZeusPowerShellLiteral $env:ZEUS_VALIDATION_DIR) + '; '
    }
    $command += '& ' + (ConvertTo-ZeusPowerShellLiteral $PSCommandPath) + ' -Elevated -OutputDirectory ' + (ConvertTo-ZeusPowerShellLiteral $OutputDirectory)
    if ($SkipBuild) { $command += ' -SkipBuild' }
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    try {
        $child = Start-Process -FilePath $powershell -Verb RunAs -WorkingDirectory $repository -Wait -PassThru `
            -ArgumentList @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedCommand)
        Write-Host ('Resultados da execução: ' + $OutputDirectory)
        Write-Host ('Relatório, quando produzido: ' + (Join-Path $OutputDirectory 'relatorio.json'))
        exit $child.ExitCode
    } catch {
        Write-Host ('Não foi possível obter o token de administrador: ' + $_.Exception.Message) -ForegroundColor Red
        Write-Host 'A suíte não foi iniciada. Abra iniciar-testes.cmd para tentar novamente.'
        exit 5
    }
}

if (!(Test-Path -LiteralPath (Join-Path $repository 'Zeus.slnx') -PathType Leaf)) {
    Write-Host 'Extraia o pacote completo antes de iniciar: Zeus.slnx não está junto dos scripts.' -ForegroundColor Red
    exit 2
}
if ((Test-Path -LiteralPath (Join-Path $OutputDirectory 'relatorio.json')) -or (Test-Path -LiteralPath (Join-Path $OutputDirectory 'report.json'))) {
    Write-Host 'Este diretório já contém um relatório. Escolha outro para preservar os resultados anteriores.' -ForegroundColor Red
    exit 2
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$reportPath = Join-Path $OutputDirectory 'relatorio.json'
$report = [ordered]@{
    schemaVersion = 1
    runId = $runId
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    finishedAtUtc = $null
    accepted = $false
    administrator = $true
    requiredSdk = $requiredSdk
    sourceCommit = ''
    sdk = $null
    stages = @()
    testProjects = @()
    testCounts = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0 }
    failures = @()
    screenshots = @()
    screenshotDirectory = $null
    limitations = @(
        'A suíte exercita código, inventário real, WPF, permissões e fixtures reversíveis nesta máquina.',
        'Reparos, criação de ponto de restauração, instalação de drivers, varredura offline e reinicialização não são executados.',
        'Aprovação dos testes não mede ganho de desempenho nem substitui a validação de reparos em máquinas descartáveis.'
    )
}
$exitCode = 1
$oldLocation = Get-Location
$environmentNames = @(
    'PATH', 'DOTNET_ROOT', 'DOTNET_HOST_PATH', 'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE', 'DOTNET_NOLOGO', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH',
    'NUGET_SCRATCH', 'ZEUS_WINDOWS_ACCEPTANCE', 'ZEUS_VALIDATION_DIR'
)
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$oldSecurityProtocol = [Net.ServicePointManager]::SecurityProtocol

function Add-ZeusStage([string]$Name, [string]$Outcome, [DateTimeOffset]$Started, [string]$Message) {
    $report.stages += [ordered]@{
        name = $Name; outcome = $Outcome; startedAtUtc = $Started.ToString('o')
        durationSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $Started).TotalSeconds, 3); message = $Message
    }
}

function Invoke-ZeusDotnet([string]$Executable, [string[]]$Arguments, [string]$LogPath) {
    $writer = New-Object IO.StreamWriter($LogPath, $false, (New-Object Text.UTF8Encoding($false)))
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 turns redirected native stderr into ErrorRecords.
        # Preserve all output and decide success using the process exit code.
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Write-Host $line
            $writer.WriteLine($line)
            $writer.Flush()
        }
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
        $writer.Dispose()
    }
}

function Test-ZeusSdk([string]$Executable) {
    if (![IO.File]::Exists($Executable)) { return $false }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $installed = @(& $Executable --list-sdks 2>&1)
        if ($LASTEXITCODE -ne 0) { return $false }
        return @($installed | Where-Object { $_.ToString() -match ('^' + [Regex]::Escape($requiredSdk) + '\s') }).Count -gt 0
    } catch { return $false }
    finally { $ErrorActionPreference = $previousPreference }
}

function Get-ZeusSourceCommit {
    $information = Join-Path $repository 'source-info.json'
    if (Test-Path -LiteralPath $information -PathType Leaf) {
        try {
            $source = Get-Content -LiteralPath $information -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($name in @('sourceCommit', 'commit', 'commitSha')) {
                $property = $source.PSObject.Properties[$name]
                if ($null -ne $property -and [string]$property.Value -match '^[0-9a-fA-F]{40}$') { return [string]$property.Value }
            }
        } catch { }
    }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $git = Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $git) {
            $commit = @(& $git.Source -C $repository rev-parse HEAD 2>$null)
            if ($LASTEXITCODE -eq 0 -and $commit.Count -eq 1 -and [string]$commit[0] -match '^[0-9a-fA-F]{40}$') { return [string]$commit[0] }
        }
    } catch { }
    finally { $ErrorActionPreference = $previousPreference }
    return ''
}

function Get-ZeusSdk {
    $architecture = $env:PROCESSOR_ARCHITEW6432
    if ([string]::IsNullOrWhiteSpace($architecture)) { $architecture = $env:PROCESSOR_ARCHITECTURE }
    switch ($architecture.ToUpperInvariant()) {
        'AMD64' { $rid = 'win-x64' }
        'ARM64' { $rid = 'win-arm64' }
        'X86' { $rid = 'win-x86' }
        default { throw 'A arquitetura Windows não pôde ser identificada para instalar o SDK oficial.' }
    }
    $tools = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Zeus\Testing\Tools'
    [void][IO.Directory]::CreateDirectory($tools)
    $candidates = @()
    $command = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { $candidates += $command.Source }
    if (![string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
    foreach ($directory in @(Get-ChildItem -LiteralPath $tools -Directory -Filter ('dotnet-' + $requiredSdk + '-' + $rid + '*'))) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName '.zeus-sdk-verified.json') -PathType Leaf) {
            $candidates += Join-Path $directory.FullName 'dotnet.exe'
        }
    }
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-ZeusSdk $candidate) {
            $report.sdk = [ordered]@{ version = $requiredSdk; source = 'existing'; executable = $candidate; sha512Verified = $null }
            Write-Host ('SDK .NET ' + $requiredSdk + ' já disponível.') -ForegroundColor Green
            return $candidate
        }
    }

    Write-Host ('Baixando o SDK .NET ' + $requiredSdk + ' para a pasta local do ZEUS. Nenhum instalador de sistema será usado.') -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = $oldSecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $channel = ($requiredSdk.Split('.')[0..1] -join '.')
    $metadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/' + $channel + '/releases.json'
    $metadata = (Invoke-WebRequest -Uri $metadataUrl -UseBasicParsing -TimeoutSec 90).Content | ConvertFrom-Json
    $artifact = $null
    foreach ($release in $metadata.releases) {
        $sdks = @()
        if ($null -ne $release.sdk) { $sdks += $release.sdk }
        if ($null -ne $release.sdks) { $sdks += $release.sdks }
        foreach ($sdk in $sdks) {
            if ($sdk.version -ne $requiredSdk) { continue }
            $artifact = $sdk.files | Where-Object { $_.rid -eq $rid -and $_.name.EndsWith('.zip') } | Select-Object -First 1
            if ($null -ne $artifact) { break }
        }
        if ($null -ne $artifact) { break }
    }
    if ($null -eq $artifact -or $artifact.hash -notmatch '^[0-9a-fA-F]{128}$') {
        throw ('O SDK ' + $requiredSdk + ' para ' + $rid + ' não consta com SHA-512 nos metadados oficiais.')
    }
    $uri = New-Object Uri($artifact.url)
    $trustedHosts = @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com', 'dotnetcli.azureedge.net', 'dotnetcli.blob.core.windows.net')
    if ($uri.Scheme -ne 'https' -or $trustedHosts -notcontains $uri.DnsSafeHost) {
        throw 'O endereço de download não é um destino HTTPS oficial reconhecido do .NET.'
    }
    $downloadId = [Guid]::NewGuid().ToString('N')
    $archive = Join-Path $tools ('sdk-' + $requiredSdk + '-' + $rid + '-' + $downloadId + '.zip')
    Invoke-WebRequest -Uri $uri.AbsoluteUri -UseBasicParsing -OutFile $archive -TimeoutSec 1800
    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
    if (![string]::Equals($actualHash, $artifact.hash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SHA-512 diferente do publicado pela Microsoft. O arquivo foi preservado para diagnóstico e não será extraído nem executado.'
    }
    Write-Host 'SHA-512 oficial confirmado. Preparando o SDK local.' -ForegroundColor Green
    $destination = Join-Path $tools ('dotnet-' + $requiredSdk + '-' + $rid + '-' + $downloadId)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
    $executable = Join-Path $destination 'dotnet.exe'
    if (!(Test-ZeusSdk $executable)) { throw 'O SDK extraído não confirmou a versão esperada; os testes não foram iniciados.' }
    $verified = [ordered]@{ sdkVersion = $requiredSdk; rid = $rid; sha512 = $actualHash.ToLowerInvariant() }
    [IO.File]::WriteAllText((Join-Path $destination '.zeus-sdk-verified.json'), ($verified | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $report.sdk = [ordered]@{
        version = $requiredSdk; source = 'official-local-download'; executable = $executable
        releaseMetadata = $metadataUrl; artifactUrl = $uri.AbsoluteUri; sha512Verified = $true; sha512 = $actualHash.ToLowerInvariant()
    }
    return $executable
}

try {
    Write-Host 'ZEUS: testes completos neste Windows' -ForegroundColor Cyan
    Write-Host 'A suíte abre a interface real, lê o hardware e verifica permissões, regras e recuperação de fixtures.'
    Write-Host 'Fixtures de inicialização e preferências visuais alteram temporariamente a conta desta execução e restauram seus estados.'
    Write-Host 'Reparos, instalação de drivers, varredura offline e reinicialização não fazem parte desta suíte.'
    Write-Host ('Resultados: ' + $OutputDirectory)
    Set-Location -LiteralPath $repository
    $report.sourceCommit = Get-ZeusSourceCommit

    $env:DOTNET_CLI_HOME = Join-Path $OutputDirectory '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $OutputDirectory '.nuget\packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $OutputDirectory '.nuget\http-cache'
    $env:NUGET_SCRATCH = Join-Path $OutputDirectory '.nuget\scratch'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:ZEUS_WINDOWS_ACCEPTANCE = '1'
    if ([string]::IsNullOrWhiteSpace($env:ZEUS_VALIDATION_DIR)) { $env:ZEUS_VALIDATION_DIR = Join-Path $OutputDirectory 'screenshots' }
    $report.screenshotDirectory = $env:ZEUS_VALIDATION_DIR
    foreach ($directory in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_SCRATCH, $env:ZEUS_VALIDATION_DIR)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    $sdkStarted = [DateTimeOffset]::UtcNow
    try { $dotnet = Get-ZeusSdk }
    catch {
        Add-ZeusStage 'sdk' 'failed' $sdkStarted $_.Exception.Message
        throw
    }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_HOST_PATH = $dotnet
    # Child processes (including the actual maintenance worker boundary tests)
    # receive this process-only PATH. No Windows environment setting is changed.
    $env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $savedEnvironment['PATH']
    Add-ZeusStage 'sdk' 'success' $sdkStarted ('SDK ' + $requiredSdk + ' confirmado.')

    $buildStarted = [DateTimeOffset]::UtcNow
    if ($SkipBuild) {
        Add-ZeusStage 'build' 'skipped' $buildStarted 'Build prévio será utilizado por solicitação explícita.'
    } else {
        Write-Host 'Compilando toda a solução em Release...' -ForegroundColor Cyan
        $buildExit = Invoke-ZeusDotnet $dotnet @('build', 'Zeus.slnx', '-c', 'Release', '--nologo') (Join-Path $OutputDirectory 'build.log')
        if ($buildExit -ne 0) {
            Add-ZeusStage 'build' 'failed' $buildStarted ('dotnet build retornou ' + $buildExit + '.')
            throw 'A compilação falhou. Os testes não serão executados sobre binários antigos; consulte build.log.'
        }
        Add-ZeusStage 'build' 'success' $buildStarted 'Toda a solução compilada em Release.'
    }

    $expected = @('Zeus.Core.Tests', 'Zeus.Cleanup.Tests', 'Zeus.Maintenance.Protocol.Tests', 'Zeus.UserOptimization.Tests', 'Zeus.Hardware.Tests', 'Zeus.Storage.Tests', 'Zeus.Windows.Acceptance.Tests')
    $projects = @(Get-ChildItem -LiteralPath (Join-Path $repository 'tests') -Recurse -Filter '*.csproj' | Sort-Object FullName)
    foreach ($name in $expected) {
        if (@($projects | Where-Object { $_.BaseName -eq $name }).Count -ne 1) { throw ('Projeto de testes ausente ou duplicado: ' + $name) }
    }
    foreach ($project in $projects) {
        $testStarted = [DateTimeOffset]::UtcNow
        Write-Host ('Executando ' + $project.BaseName + '...') -ForegroundColor Cyan
        $directory = Join-Path $OutputDirectory $project.BaseName
        [void][IO.Directory]::CreateDirectory($directory)
        $trxPath = Join-Path $directory 'results.trx'
        if (Test-Path -LiteralPath $trxPath) { throw ('Existe um TRX anterior em ' + $directory + '; use um diretório de saída novo.') }
        $arguments = @('test', $project.FullName, '-c', 'Release', '--no-build', '--nologo', '--results-directory', $directory,
            '--logger', 'trx;LogFileName=results.trx', '--blame-hang-timeout', '5m', '--blame-hang-dump-type', 'none')
        $testExit = Invoke-ZeusDotnet $dotnet $arguments (Join-Path $directory 'test.log')
        $counts = [ordered]@{ project = $project.BaseName; exitCode = $testExit; total = 0; passed = 0; failed = 0; skipped = 0; accepted = $false }
        $problem = $null
        try {
            if (!(Test-Path -LiteralPath $trxPath -PathType Leaf)) { throw 'O processo não produziu results.trx.' }
            [xml]$trx = Get-Content -LiteralPath $trxPath -Raw -Encoding UTF8
            $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
            if ($null -eq $counters) { throw 'TRX sem contadores de execução.' }
            $counts.total = [int]$counters.total
            $counts.passed = [int]$counters.passed
            $counts.failed = [int]$counters.failed
            $counts.skipped = [Math]::Max([int]$counters.notExecuted, ([int]$counters.total - [int]$counters.executed))
            $counts.accepted = $testExit -eq 0 -and $counts.total -gt 0 -and $counts.passed -eq $counts.total -and $counts.failed -eq 0 -and $counts.skipped -eq 0
            foreach ($failure in @($trx.SelectNodes("//*[local-name()='UnitTestResult' and @outcome='Failed']"))) {
                $message = $failure.SelectSingleNode(".//*[local-name()='Message']")
                $stack = $failure.SelectSingleNode(".//*[local-name()='StackTrace']")
                $report.failures += [ordered]@{
                    project = $project.BaseName; test = [string]$failure.testName
                    message = $(if ($null -ne $message) { $message.InnerText } else { 'TRX não forneceu a mensagem da falha.' })
                    stackTrace = $(if ($null -ne $stack) { $stack.InnerText } else { $null })
                }
            }
            if (!$counts.accepted) { $problem = 'O projeto tem falhas, testes ignorados, nenhum teste executado ou código de saída diferente de zero.' }
        } catch { $problem = $_.Exception.Message }
        $report.testProjects += $counts
        foreach ($key in @('total', 'passed', 'failed', 'skipped')) { $report.testCounts[$key] += $counts[$key] }
        if ($null -ne $problem) {
            $report.failures += [ordered]@{ project = $project.BaseName; test = $null; message = $problem; stackTrace = $null }
            Add-ZeusStage ('test:' + $project.BaseName) 'failed' $testStarted $problem
        } else {
            Add-ZeusStage ('test:' + $project.BaseName) 'success' $testStarted ($counts.passed.ToString() + ' testes aprovados.')
        }
    }
    $report.screenshots = @(Get-ChildItem -LiteralPath $env:ZEUS_VALIDATION_DIR -Filter 'zeus-*.png' | Sort-Object Name | ForEach-Object { $_.Name })
    $report.accepted = $report.testProjects.Count -ge $expected.Count -and @($report.testProjects | Where-Object { !$_.accepted }).Count -eq 0 `
        -and $report.testCounts.total -gt 0 -and $report.testCounts.passed -eq $report.testCounts.total -and $report.screenshots.Count -ge 11
    if (!$report.accepted -and $report.screenshots.Count -lt 11) {
        $report.failures += [ordered]@{ project = 'WPF'; test = $null; message = 'Não foram produzidas as 11 capturas previstas da interface.'; stackTrace = $null }
    }
    if ($report.accepted) { $exitCode = 0 }
} catch {
    $report.failures += [ordered]@{ project = $null; test = $null; message = $_.Exception.Message; stackTrace = $null }
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    $report.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        [IO.File]::WriteAllText($reportPath, (($report | ConvertTo-Json -Depth 10) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
    } catch {
        $exitCode = 1
        Write-Host ('Não foi possível gravar relatorio.json: ' + $_.Exception.Message) -ForegroundColor Red
    }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    [Net.ServicePointManager]::SecurityProtocol = $oldSecurityProtocol
    Set-Location -LiteralPath $oldLocation.Path
}

if ($exitCode -eq 0) {
    Write-Host ('APROVADO: ' + $report.testCounts.passed + ' testes executados e aprovados neste Windows.') -ForegroundColor Green
} else {
    Write-Host ('A suíte encontrou problemas: ' + $report.testCounts.passed + ' aprovados de ' + $report.testCounts.total + ' executados.') -ForegroundColor Red
}
Write-Host ('Relatório completo: ' + $reportPath)
Write-Host ('Capturas da interface: ' + $report.screenshotDirectory)
Write-Host 'Os logs e TRX ficaram preservados. Compartilhe relatorio.json para analisar o resultado.'
exit $exitCode
