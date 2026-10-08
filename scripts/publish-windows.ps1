param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '',
    [string]$ProductVersion = '1.0.0'
)

$ErrorActionPreference = 'Stop'
if ($ProductVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'ProductVersion must contain three or four numeric fields.' }
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repository 'artifacts'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$sourceCommit = (git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Source commit could not be identified.' }
$sourceDirty = @(git -C $repository status --porcelain).Count -gt 0
$buildId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$destination = Join-Path $OutputRoot "zeus-$Runtime-$($sourceCommit.Substring(0, 12))-$buildId"
New-Item -ItemType Directory -Path $destination -Force | Out-Null

Push-Location $repository
try {
    dotnet publish src/Zeus.Desktop/Zeus.Desktop.csproj -c Release -r $Runtime --self-contained true "-p:Version=$ProductVersion" -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publication failed.' }
    dotnet publish src/Zeus.Maintenance/Zeus.Maintenance.csproj -c Release -r $Runtime --self-contained true "-p:Version=$ProductVersion" -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Maintenance helper publication failed.' }
    foreach ($assemblyName in @('Zeus.Desktop.dll', 'Zeus.Maintenance.dll')) {
        $assemblyPath = Join-Path $destination $assemblyName
        if (!(Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw "Required versioned assembly missing: $assemblyName" }
        $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
        $versionAttribute = [System.Reflection.CustomAttributeExtensions]::GetCustomAttribute(
            $assembly, [Reflection.AssemblyInformationalVersionAttribute])
        if ($null -eq $versionAttribute -or $versionAttribute.InformationalVersion -notmatch "^$([regex]::Escape($ProductVersion))(?:\+.*)?$") {
            throw "Published $assemblyName does not report ProductVersion $ProductVersion."
        }
    }
    foreach ($executableName in @('Zeus.Desktop.exe', 'Zeus.Maintenance.exe')) {
        $executablePath = Join-Path $destination $executableName
        $executableVersion = (Get-Item -LiteralPath $executablePath).VersionInfo.ProductVersion
        if ($executableVersion -notmatch "^$([regex]::Escape($ProductVersion))(?:\+.*)?$") {
            throw "Published $executableName does not report ProductVersion $ProductVersion."
        }
    }
    Copy-Item README.md, SECURITY.md, CHANGELOG.md -Destination $destination
    Copy-Item docs/validacao-windows.md -Destination $destination
    $files = @('Zeus.Desktop.exe', 'Zeus.Maintenance.exe', 'Zeus.Core.dll', 'Zeus.Windows.dll', 'Zeus.Cleanup.dll')
    $hashes = [ordered]@{}
    foreach ($file in $files) {
        $path = Join-Path $destination $file
        if (!(Test-Path $path -PathType Leaf)) { throw "Required package file missing: $file" }
        $hashes[$file] = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $packageFiles = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $destination -File -Recurse | Sort-Object FullName) {
        $relativePath = $file.FullName.Substring($destination.TrimEnd('\').Length + 1).Replace('\', '/')
        $packageFiles[$relativePath] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{
        sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty
        runtime = $Runtime
        productVersion = $ProductVersion
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        developmentBuild = $true
        sha256 = $hashes
        packageFilesSha256 = $packageFiles
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'build-info.json') -Encoding utf8
    $archive = Join-Path $OutputRoot "zeus-$Runtime.zip"
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $archive -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -eq 'build-info.json' -or $entry.FullName.EndsWith('/')) { continue }
            if (!$packageFiles.Contains($entry.FullName)) { throw "Unexpected file in package archive: $($entry.FullName)" }
        }
        foreach ($relativePath in $packageFiles.Keys) {
            $entry = $zip.GetEntry($relativePath)
            if ($null -eq $entry) { throw "Package archive is missing: $relativePath" }
            $stream = $entry.Open()
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                $actualHash = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
            } finally {
                $sha256.Dispose()
                $stream.Dispose()
            }
            if ($actualHash -ne $packageFiles[$relativePath]) { throw "Package archive hash mismatch: $relativePath" }
        }
    } finally {
        $zip.Dispose()
    }
    $archiveHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$archiveHash  $(Split-Path -Leaf $archive)" | Set-Content "$archive.sha256" -Encoding ascii
    Write-Output "Portable development build: $archive"
    Write-Output "Unpacked files with provenance: $destination"
} finally {
    Pop-Location
}
