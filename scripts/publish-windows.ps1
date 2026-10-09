param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '',
    [string]$ProductVersion = '1.0.0'
)

$ErrorActionPreference = 'Stop'
if ($ProductVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'ProductVersion must contain three or four numeric fields.' }

function Test-PortableApplicationLaunch([string]$Executable, [string]$WorkingDirectory) {
    $process = Start-Process -FilePath $Executable -WorkingDirectory $WorkingDirectory -PassThru
    $windowReady = $false
    try {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) { throw "Portable ZEUS exited during startup with code $($process.ExitCode)." }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero -and
                $process.MainWindowTitle -eq 'ZEUS · Otimização e diagnóstico' -and $process.Responding) {
                $windowReady = $true
                break
            }
            Start-Sleep -Milliseconds 250
        }
        if (!$windowReady) { throw 'Portable ZEUS did not show a responsive main window within 45 seconds.' }
    } finally {
        $process.Refresh()
        if (!$process.HasExited) {
            if (!$process.CloseMainWindow()) {
                $process.Kill()
                [void]$process.WaitForExit(5000)
                throw 'Portable ZEUS had no closable main window; its smoke process was stopped.'
            }
            if (!$process.WaitForExit(15000)) {
                $process.Kill()
                if (!$process.WaitForExit(5000)) { throw 'Could not stop the portable ZEUS smoke process.' }
                throw 'Portable ZEUS did not close cleanly after its launch smoke.'
            }
        }
        $process.Dispose()
    }
    Write-Output 'Portable application smoke passed: main window opened, responded, and closed cleanly.'
}

function Add-ThirdPartyNotices([string]$Destination, [string]$Repository, [string]$Runtime, [string]$ProductVersion, [string]$SourceCommit, [string]$BuildId) {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
    $dotnetNotices = Join-Path (Split-Path -Parent $dotnetCommand.Source) 'ThirdPartyNotices.txt'
    if (!(Test-Path -LiteralPath $dotnetNotices -PathType Leaf)) {
        throw "The installed .NET distribution has no ThirdPartyNotices.txt: $dotnetNotices"
    }
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $Destination 'THIRD-PARTY-NOTICES.NET.txt')

    $globalPackages = $env:NUGET_PACKAGES
    if ([string]::IsNullOrWhiteSpace($globalPackages)) {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        $globalPackages = Join-Path $userProfile '.nuget/packages'
    }
    if (!(Test-Path -LiteralPath $globalPackages -PathType Container)) {
        throw "NuGet global package directory was not found: $globalPackages"
    }

    $dependencies = @{}
    $runtimePacks = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $projectComponents = @{}
    $dependencyGraphs = [System.Collections.Generic.List[object]]::new()
    $supportedLibraryKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($depsFile in @('Zeus.Desktop.deps.json', 'Zeus.Maintenance.deps.json')) {
        $depsPath = Join-Path $Destination $depsFile
        if (!(Test-Path -LiteralPath $depsPath -PathType Leaf)) { throw "Required dependency manifest missing: $depsFile" }
        $deps = Get-Content -LiteralPath $depsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($library in $deps.libraries.PSObject.Properties) {
            if ($library.Value.type -eq 'runtimepack') {
                [void]$runtimePacks.Add($library.Name)
                [void]$supportedLibraryKeys.Add($library.Name)
                continue
            }
            if ($library.Value.type -eq 'project') {
                $separator = $library.Name.LastIndexOf('/')
                if ($separator -le 0) { throw "Invalid project identity in ${depsFile}: $($library.Name)" }
                $projectComponents[$library.Name] = [pscustomobject]@{
                    LibraryKey = $library.Name
                    Id = $library.Name.Substring(0, $separator)
                    Version = $library.Name.Substring($separator + 1)
                }
                [void]$supportedLibraryKeys.Add($library.Name)
                continue
            }
            if ($library.Value.type -ne 'package') { continue }
            $separator = $library.Name.LastIndexOf('/')
            if ($separator -le 0) { throw "Invalid package identity in ${depsFile}: $($library.Name)" }
            $id = $library.Name.Substring(0, $separator)
            $version = $library.Name.Substring($separator + 1)
            $key = "$id/$version"
            $dependencies[$key] = [pscustomobject]@{ Id = $id; Version = $version }
            [void]$supportedLibraryKeys.Add($library.Name)
        }
        $dependencyGraphs.Add($deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value)
    }

    $licenseDirectory = Join-Path $Destination 'licenses'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    $rows = [System.Collections.Generic.List[string]]::new()
    $licenseExpressions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $licenseByPackage = @{}
    foreach ($package in @($dependencies.Values | Sort-Object Id, Version)) {
        $packageDirectory = Join-Path $globalPackages (Join-Path $package.Id.ToLowerInvariant() $package.Version)
        if (!(Test-Path -LiteralPath $packageDirectory -PathType Container)) {
            throw "NuGet package required by the published application is missing: $($package.Id) $($package.Version)"
        }
        $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
        if ($null -eq $nuspec) { throw "NuGet package has no nuspec license metadata: $($package.Id) $($package.Version)" }
        [xml]$packageMetadata = Get-Content -LiteralPath $nuspec.FullName -Raw -Encoding UTF8
        $license = $packageMetadata.package.metadata.license
        if ($null -eq $license) { throw "NuGet package has no declared license metadata: $($package.Id) $($package.Version)" }
        $licenseType = $license.GetAttribute('type')
        if ($licenseType -eq 'expression') {
            $expression = $license.InnerText.Trim()
            if ($expression -notin @('MIT', 'Apache-2.0')) {
                throw "Unreviewed NuGet SPDX expression for $($package.Id) $($package.Version): $expression"
            }
            [void]$licenseExpressions.Add($expression)
            $licenseText = "licenses/$expression.txt"
            $licenseByPackage["$($package.Id)/$($package.Version)"] = $expression
        } elseif ($licenseType -eq 'file') {
            $licenseRelativePath = $license.InnerText.Trim()
            $licenseSource = Join-Path $packageDirectory $licenseRelativePath
            if (!(Test-Path -LiteralPath $licenseSource -PathType Leaf)) {
                throw "Declared license file is missing for $($package.Id) $($package.Version): $licenseRelativePath"
            }
            $licenseText = "licenses/NuGet-$($package.Id)-$($package.Version).txt"
            Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $Destination $licenseText)
            $licenseByPackage["$($package.Id)/$($package.Version)"] = 'NOASSERTION'
        } else {
            throw "Unsupported NuGet license metadata for $($package.Id) $($package.Version)."
        }
        $packageUrl = "https://www.nuget.org/packages/$($package.Id)/$($package.Version)"
        $copyright = [string]$packageMetadata.package.metadata.copyright
        if ([string]::IsNullOrWhiteSpace($copyright)) { $copyright = 'Veja os avisos incluídos e os metadados do pacote NuGet.' }
        $copyright = $copyright -replace '[|\r\n]+', ' '
        $rows.Add("| [$($package.Id)]($packageUrl) | $($package.Version) | [$licenseText]($licenseText) | $copyright |")

        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(?i)(THIRD-PARTY-NOTICES|NOTICE)' }) {
            $noticeName = "THIRD-PARTY-NOTICES-$($package.Id)-$($package.Version).txt"
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $Destination $noticeName)
        }
    }

    foreach ($expression in $licenseExpressions) {
        $licenseFile = "$expression.txt"
        $licenseSource = Join-Path $Repository (Join-Path 'licenses/SPDX' $licenseFile)
        if (!(Test-Path -LiteralPath $licenseSource -PathType Leaf)) { throw "Bundled SPDX license text missing: $licenseSource" }
        Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $licenseDirectory $licenseFile)
    }

    $runtimeSummary = if ($runtimePacks.Count -gt 0) { ($runtimePacks | Sort-Object) -join ', ' } else { 'runtime self-contained conforme o RID do pacote' }
    $noticeText = @(
        '# Third-party notices',
        '',
        'Este pacote portátil inclui o runtime .NET e dependências NuGet listadas abaixo. Os avisos completos do runtime estão em `THIRD-PARTY-NOTICES.NET.txt`; avisos adicionais de pacotes estão em arquivos `THIRD-PARTY-NOTICES-*.txt`.',
        'Estes avisos cobrem somente componentes de terceiros; não concedem licença para o código ou a marca ZEUS.',
        '',
        "Runtime packs: $runtimeSummary.",
        '',
        '| Pacote | Versão | Licença | Aviso de copyright |',
        '| --- | --- | --- | --- |'
    ) + @($rows)
    $noticeText | Set-Content -LiteralPath (Join-Path $Destination 'THIRD-PARTY-NOTICES.md') -Encoding utf8

    # Emit a machine-readable SPDX inventory from the exact package and project
    # graphs in the two published .deps.json files. Runtime packs are reported
    # separately from ordinary NuGet libraries because .NET labels them so.
    $spdxComponents = [System.Collections.Generic.List[object]]::new()
    $componentByLibrary = @{}
    foreach ($project in @($projectComponents.Values | Sort-Object Id, Version)) {
        $componentByLibrary[$project.LibraryKey] = [ordered]@{
            name = $project.Id
            SPDXID = 'SPDXRef-' + [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($project.LibraryKey))).ToLowerInvariant()
            versionInfo = $project.Version
            downloadLocation = 'NOASSERTION'
            filesAnalyzed = $false
            licenseConcluded = 'NOASSERTION'
            licenseDeclared = 'NOASSERTION'
            copyrightText = 'NOASSERTION'
            externalRefs = @(@{ referenceCategory = 'PACKAGE-MANAGER'; referenceType = 'purl'; referenceLocator = "pkg:generic/$($project.Id.ToLowerInvariant())@$($project.Version)" })
        }
    }
    foreach ($package in @($dependencies.Values | Sort-Object Id, Version)) {
        $libraryKey = "$($package.Id)/$($package.Version)"
        $packageUrl = "https://www.nuget.org/packages/$($package.Id)/$($package.Version)"
        $componentByLibrary[$libraryKey] = [ordered]@{
            name = $package.Id
            SPDXID = 'SPDXRef-' + [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($libraryKey))).ToLowerInvariant()
            versionInfo = $package.Version
            downloadLocation = $packageUrl
            filesAnalyzed = $false
            licenseConcluded = 'NOASSERTION'
            licenseDeclared = $licenseByPackage[$libraryKey]
            copyrightText = 'NOASSERTION'
            externalRefs = @(@{ referenceCategory = 'PACKAGE-MANAGER'; referenceType = 'purl'; referenceLocator = "pkg:nuget/$($package.Id)@$($package.Version)" })
        }
    }
    foreach ($runtimePack in @($runtimePacks | Sort-Object)) {
        $separator = $runtimePack.LastIndexOf('/')
        if ($separator -le 0 -or !$runtimePack.StartsWith('runtimepack.', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Invalid runtime pack identity in dependency manifest: $runtimePack"
        }
        $packageId = $runtimePack.Substring('runtimepack.'.Length, $separator - 'runtimepack.'.Length)
        $version = $runtimePack.Substring($separator + 1)
        $packageDirectory = Join-Path $globalPackages (Join-Path $packageId.ToLowerInvariant() $version)
        $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
        if ($null -eq $nuspec) { throw "Runtime pack has no NuGet license metadata: $runtimePack" }
        [xml]$runtimeMetadata = Get-Content -LiteralPath $nuspec.FullName -Raw -Encoding UTF8
        $runtimeLicense = $runtimeMetadata.package.metadata.license
        if ($null -eq $runtimeLicense) { throw "Runtime pack has no declared license metadata: $runtimePack" }
        if ($runtimeLicense.GetAttribute('type') -ne 'expression' -or $runtimeLicense.InnerText.Trim() -notin @('MIT', 'Apache-2.0')) {
            throw "Unreviewed runtime pack SPDX expression for $runtimePack."
        }
        $libraryKey = $runtimePack
        $componentByLibrary[$libraryKey] = [ordered]@{
            name = $packageId
            SPDXID = 'SPDXRef-' + [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($libraryKey))).ToLowerInvariant()
            versionInfo = $version
            downloadLocation = "https://www.nuget.org/packages/$packageId/$version"
            filesAnalyzed = $false
            licenseConcluded = 'NOASSERTION'
            licenseDeclared = $runtimeLicense.InnerText.Trim()
            copyrightText = 'NOASSERTION'
            externalRefs = @(@{ referenceCategory = 'PACKAGE-MANAGER'; referenceType = 'purl'; referenceLocator = "pkg:nuget/$packageId@$version" })
        }
    }
    foreach ($component in $componentByLibrary.Values) { $spdxComponents.Add($component) }
    foreach ($libraryKey in $supportedLibraryKeys) {
        if (!$componentByLibrary.ContainsKey($libraryKey)) { throw "SPDX inventory omitted published dependency $libraryKey." }
    }

    $spdxRelationships = [System.Collections.Generic.List[object]]::new()
    $relationshipKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($root in @('Zeus.Desktop', 'Zeus.Maintenance')) {
        $rootComponent = @($componentByLibrary.Values | Where-Object name -eq $root | Select-Object -First 1)
        if ($rootComponent.Count -ne 1) { throw "SPDX inventory is missing first-party application $root." }
        $spdxRelationships.Add(@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = $rootComponent[0].SPDXID })
    }
    foreach ($graph in $dependencyGraphs) {
        foreach ($library in $graph.PSObject.Properties) {
            if (!$componentByLibrary.ContainsKey($library.Name) -or $null -eq $library.Value.dependencies) { continue }
            foreach ($dependency in $library.Value.dependencies.PSObject.Properties) {
                $dependencyKey = "$($dependency.Name)/$($dependency.Value)"
                if (!$componentByLibrary.ContainsKey($dependencyKey)) { throw "SPDX dependency relationship target is missing: $dependencyKey" }
                $sourceSpdxId = [string]$componentByLibrary[$library.Name].SPDXID
                $targetSpdxId = [string]$componentByLibrary[$dependencyKey].SPDXID
                $relationshipKey = "$sourceSpdxId|$targetSpdxId"
                if ($relationshipKeys.Add($relationshipKey)) {
                    $spdxRelationships.Add(@{ spdxElementId = $sourceSpdxId; relationshipType = 'DEPENDS_ON'; relatedSpdxElement = $targetSpdxId })
                }
            }
        }
    }
    if ($spdxComponents.Count -eq 0 -or $spdxRelationships.Count -le 2) { throw 'SPDX inventory is incomplete: package components or dependency relationships are missing.' }
    $spdxDocument = [ordered]@{
        spdxVersion = 'SPDX-2.3'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = "ZEUS $ProductVersion $Runtime"
        documentNamespace = "https://spdx.org/spdxdocs/zeus-$Runtime-$ProductVersion-$SourceCommit-$BuildId"
        creationInfo = @{
            created = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
            creators = @('Tool: ZEUS packaging script')
        }
        packages = @($spdxComponents)
        relationships = @($spdxRelationships)
    }
    $sbomPath = Join-Path $Destination 'SBOM.spdx.json'
    $spdxDocument | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $sbomPath -Encoding utf8
    $validatedSbom = Get-Content -LiteralPath $sbomPath -Raw -Encoding utf8 | ConvertFrom-Json
    $validatedSpdxIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$validatedSpdxIds.Add('SPDXRef-DOCUMENT')
    foreach ($component in $validatedSbom.packages) {
        if ([string]::IsNullOrWhiteSpace($component.name) -or [string]::IsNullOrWhiteSpace($component.SPDXID) -or
            [string]::IsNullOrWhiteSpace($component.versionInfo) -or
            [string]::IsNullOrWhiteSpace($component.licenseDeclared) -or !$validatedSpdxIds.Add([string]$component.SPDXID)) {
            throw 'Generated SPDX SBOM has a component with missing identity/metadata or duplicate SPDXID.'
        }
    }
    foreach ($relationship in $validatedSbom.relationships) {
        if (!$validatedSpdxIds.Contains([string]$relationship.spdxElementId) -or
            !$validatedSpdxIds.Contains([string]$relationship.relatedSpdxElement)) {
            throw 'Generated SPDX SBOM contains a relationship to an unknown component.'
        }
    }
    if ($validatedSbom.spdxVersion -ne 'SPDX-2.3' -or $validatedSbom.packages.Count -ne $spdxComponents.Count -or
        $validatedSbom.relationships.Count -ne $spdxRelationships.Count -or
        @($validatedSbom.packages | Select-Object -ExpandProperty SPDXID -Unique).Count -ne $spdxComponents.Count) {
        throw 'Generated SPDX SBOM failed structural validation.'
    }
}

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
    Add-ThirdPartyNotices -Destination $destination -Repository $repository -Runtime $Runtime -ProductVersion $ProductVersion -SourceCommit $sourceCommit -BuildId $buildId
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
    Test-PortableApplicationLaunch (Join-Path $destination 'Zeus.Desktop.exe') $destination
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
