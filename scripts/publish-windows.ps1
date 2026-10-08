param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repository 'artifacts'
}
$destination = Join-Path $OutputRoot "zeus-$Runtime"
New-Item -ItemType Directory -Path $destination -Force | Out-Null

Push-Location $repository
try {
    dotnet publish src/Zeus.Desktop/Zeus.Desktop.csproj -c Release -r $Runtime --self-contained true -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publication failed.' }
    dotnet publish src/Zeus.Maintenance/Zeus.Maintenance.csproj -c Release -r $Runtime --self-contained true -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Maintenance helper publication failed.' }
    Copy-Item README.md, SECURITY.md -Destination $destination
    $archive = Join-Path $OutputRoot "zeus-$Runtime.zip"
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $archive -Force
    Write-Output "Portable development build: $archive"
} finally {
    Pop-Location
}
