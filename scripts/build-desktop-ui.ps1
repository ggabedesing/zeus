param([switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$frontendRoot = Join-Path $repoRoot 'src\Zeus.DesktopUI'
$nativeOutput = Join-Path $frontendRoot 'native-bin'
$sdkPath = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdkPath)) { $sdkPath = (Get-Command dotnet -ErrorAction Stop).Source }
& $sdkPath publish (Join-Path $repoRoot 'src\Zeus.LocalApi\Zeus.LocalApi.csproj') -c Release -r win-x64 --self-contained true -o $nativeOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o serviço local.' }
& $sdkPath publish (Join-Path $repoRoot 'src\Zeus.Observer\Zeus.Observer.csproj') -c Release -r win-x64 --self-contained true -o $nativeOutput
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o coletor de desempenho.' }
Push-Location $frontendRoot
try {
  if (!$SkipInstall) {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar a interface.' }
  }
  & npm.cmd run build
  if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a interface.' }
  & npm.cmd run package:win
  if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar o aplicativo.' }
} finally { Pop-Location }
