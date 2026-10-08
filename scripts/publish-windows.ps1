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
    Copy-Item docs/validacao-windows.md -Destination $destination
    $sourceCommit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Source commit could not be identified.' }
    $sourceDirty = @(git status --porcelain).Count -gt 0
    $files = @('Zeus.Desktop.exe', 'Zeus.Maintenance.exe', 'Zeus.Core.dll', 'Zeus.Windows.dll', 'Zeus.Cleanup.dll')
    $hashes = [ordered]@{}
    foreach ($file in $files) {
        $path = Join-Path $destination $file
        if (!(Test-Path $path -PathType Leaf)) { throw "Required package file missing: $file" }
        $hashes[$file] = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{
        sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty
        runtime = $Runtime
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        developmentBuild = $true
        sha256 = $hashes
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'build-info.json') -Encoding utf8
    $archive = Join-Path $OutputRoot "zeus-$Runtime.zip"
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $archive -Force
    $archiveHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$archiveHash  $(Split-Path -Leaf $archive)" | Set-Content "$archive.sha256" -Encoding ascii
    Write-Output "Portable development build: $archive"
} finally {
    Pop-Location
}
