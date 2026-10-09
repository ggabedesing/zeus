param(
    [Parameter(Mandatory = $true)] [string]$PayloadDirectory,
    [Parameter(Mandatory = $true)] [string]$ProductVersion,
    [Parameter(Mandatory = $true)] [string]$CertificatePath,
    [Parameter(Mandatory = $true)] [string]$CertificatePassword,
    [Parameter(Mandatory = $true)] [string]$ExpectedThumbprint,
    [Parameter(Mandatory = $true)] [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$timestampUrl = 'https://timestamp.digicert.com'
if ($ProductVersion -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}$') { throw 'Release version must use three numeric fields.' }
if (@($ProductVersion.Split('.') | Where-Object { [int]$_ -gt 255 }).Count -gt 0) { throw 'Each MSI version field must be 255 or lower.' }
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath $PayloadDirectory -PathType Container)) { throw 'Published payload folder was not found.' }
if (!(Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw 'Signing certificate file was not found; release signing stopped before files were changed.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Release output folder already exists; choose a new empty output path.' }
$payloadRootForOutputCheck = $PayloadDirectory.TrimEnd('\') + '\'
if ($OutputDirectory.StartsWith($payloadRootForOutputCheck, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be outside the unsigned payload folder.'
}

$infoPath = Join-Path $PayloadDirectory 'build-info.json'
if (!(Test-Path -LiteralPath $infoPath -PathType Leaf)) { throw 'Published payload has no build-info.json provenance manifest.' }
$info = Get-Content -LiteralPath $infoPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($info.sourceDirty -ne $false -or $info.productVersion -ne $ProductVersion -or $info.runtime -ne 'win-x64') {
    throw 'Payload provenance does not match a clean x64 build of the requested release version.'
}
if ($info.sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Payload provenance has no valid source commit.' }

# Validate every unsigned input against the build manifest before applying a signature.
$payloadRoot = $PayloadDirectory.TrimEnd('\') + '\'
foreach ($entry in $info.packageFilesSha256.PSObject.Properties) {
    $relative = $entry.Name.Replace('/', '\')
    $filePath = [IO.Path]::GetFullPath((Join-Path $PayloadDirectory $relative))
    if (!$filePath.StartsWith($payloadRoot, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Manifest path is missing or outside the payload: $($entry.Name)"
    }
    $actual = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne [string]$entry.Value) { throw "Payload hash mismatch before signing: $($entry.Name)" }
}
if (@($info.packageFilesSha256.PSObject.Properties).Count -eq 0) { throw 'Payload hash manifest is empty.' }

$normalizedThumbprint = ($ExpectedThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
if ($normalizedThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Expected certificate thumbprint must contain 40 hexadecimal characters.' }
$certificateCollection = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
$certificateCollection.Import($CertificatePath, $CertificatePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
$pfxThumbprints = @($certificateCollection | ForEach-Object Thumbprint)
$certificate = @($certificateCollection | Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey })
if ($certificate.Count -ne 1) { throw 'The PFX must contain exactly one private-key certificate matching the pinned thumbprint.' }
$certificate = $certificate[0]
$nowUtc = [DateTime]::UtcNow
if ($nowUtc -lt $certificate.NotBefore.ToUniversalTime() -or $nowUtc -gt $certificate.NotAfter.ToUniversalTime()) {
    throw 'The pinned signing certificate is not currently valid.'
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$ekuExtension = $certificate.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | Select-Object -First 1
if ($null -eq $ekuExtension -or !($ekuExtension.EnhancedKeyUsages | Where-Object { $_.Value -eq $codeSigningOid })) {
    throw 'The pinned certificate does not contain the Code Signing enhanced key usage.'
}
$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($null -eq $signtool) {
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signtool = Get-ChildItem -LiteralPath $windowsKits -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
}
if ($null -eq $signtool) { throw 'Windows SDK SignTool was not found.' }
$signtoolPath = if ($signtool.Path) { $signtool.Path } else { $signtool.FullName }

$certificateStoreModule = Join-Path $PSScriptRoot 'ZeusSigningCertificateStore.psm1'
Import-Module -Name $certificateStoreModule -Force -ErrorAction Stop
$signingCertificateState = $null
$secureCertificatePassword = ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force
$CertificatePassword = $null

try {
    Remove-Item Env:ZEUS_SIGNING_PFX_PASSWORD -ErrorAction SilentlyContinue
    $signingCertificateState = Add-ZeusSigningCertificateToUserStore -PfxPath $CertificatePath `
        -Password $secureCertificatePassword -ExpectedThumbprint $normalizedThumbprint -PfxThumbprints $pfxThumbprints
    $certificate = $signingCertificateState.Certificate

# Sign an isolated copy so a failed release attempt cannot partially modify its unsigned input.
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$stagedPayload = Join-Path $OutputDirectory 'payload'
New-Item -ItemType Directory -Path $stagedPayload | Out-Null
Get-ChildItem -LiteralPath $PayloadDirectory -Force | Copy-Item -Destination $stagedPayload -Recurse
$PayloadDirectory = $stagedPayload
$filesToSign = @(
    Get-ChildItem -LiteralPath $PayloadDirectory -File |
        Where-Object { $_.Name -match '^Zeus.*\.(exe|dll)$' } |
        Sort-Object Name
)
foreach ($required in @('Zeus.Desktop.exe', 'Zeus.Maintenance.exe', 'Zeus.Desktop.dll', 'Zeus.Maintenance.dll')) {
    if (!($filesToSign | Where-Object Name -eq $required)) { throw "Required first-party binary is missing: $required" }
}

function Invoke-ZeusSigning([string]$Path) {
    & $signtoolPath sign /fd SHA256 /tr $timestampUrl /td SHA256 /s My /sha1 $normalizedThumbprint $Path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing or timestamping failed for $(Split-Path -Leaf $Path)." }
    & $signtoolPath verify /pa /all /v $Path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $(Split-Path -Leaf $Path)." }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Thumbprint -ne $normalizedThumbprint -or $null -eq $signature.TimeStamperCertificate) {
        throw "Signature, signer identity, or timestamp could not be verified for $(Split-Path -Leaf $Path)."
    }
}

foreach ($file in $filesToSign) { Invoke-ZeusSigning $file.FullName }

# Repeat the portable-app smoke after signing, then package only the verified binaries.
$app = Join-Path $PayloadDirectory 'Zeus.Desktop.exe'
$process = Start-Process -FilePath $app -WorkingDirectory $PayloadDirectory -PassThru
$windowReady = $false
try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "Signed ZEUS exited during startup with code $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -eq 'ZEUS · Otimização e diagnóstico' -and $process.Responding) {
            $windowReady = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (!$windowReady) { throw 'Signed ZEUS did not show a responsive main window within 45 seconds.' }
} finally {
    $process.Refresh()
    if (!$process.HasExited) {
        if (!$process.CloseMainWindow()) { $process.Kill(); [void]$process.WaitForExit(5000); throw 'Signed ZEUS had no closable main window.' }
        if (!$process.WaitForExit(15000)) { $process.Kill(); [void]$process.WaitForExit(5000); throw 'Signed ZEUS did not close cleanly after smoke.' }
    }
    $process.Dispose()
}

$manifestHashes = [ordered]@{}
foreach ($entry in $info.packageFilesSha256.PSObject.Properties) {
    $filePath = Join-Path $PayloadDirectory $entry.Name.Replace('/', '\')
    $manifestHashes[$entry.Name] = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$topLevelHashes = [ordered]@{}
foreach ($entry in $info.sha256.PSObject.Properties) {
    $filePath = Join-Path $PayloadDirectory $entry.Name
    if (!(Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "Top-level provenance file is missing: $($entry.Name)" }
    $topLevelHashes[$entry.Name] = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$info.developmentBuild = $false
$info.sha256 = $topLevelHashes
$info.packageFilesSha256 = $manifestHashes
$info | Add-Member -NotePropertyName signing -NotePropertyValue ([ordered]@{
    algorithm = 'Authenticode SHA-256'
    timestampProtocol = 'RFC 3161 SHA-256'
    timestampUrl = $timestampUrl
    certificateSubject = $certificate.Subject
    certificateThumbprint = $normalizedThumbprint
    signedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    signedFiles = @($filesToSign | ForEach-Object Name)
    installerSigned = $true
}) -Force
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $infoPath -Encoding utf8

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDirectory $PayloadDirectory -ProductVersion $ProductVersion -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Signed MSI build failed.' }
} finally { Pop-Location }

$msiPath = Join-Path $OutputDirectory 'Zeus.Installer.msi'
Invoke-ZeusSigning $msiPath
$msiHash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$msiHash  Zeus.Installer.msi" | Set-Content -LiteralPath "$msiPath.sha256" -Encoding ascii

$archivePath = Join-Path $OutputDirectory 'zeus-win-x64.zip'
Compress-Archive -Path (Join-Path $PayloadDirectory '*') -DestinationPath $archivePath
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($relativePath in $info.packageFilesSha256.PSObject.Properties.Name) {
        $entry = $zip.GetEntry($relativePath)
        if ($null -eq $entry) { throw "Signed release archive is missing $relativePath." }
        $stream = $entry.Open()
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha256.Dispose(); $stream.Dispose() }
        if ($actual -ne [string]$info.packageFilesSha256.$relativePath) { throw "Signed release archive hash mismatch: $relativePath" }
    }
} finally { $zip.Dispose() }
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  zeus-win-x64.zip" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
Copy-Item -LiteralPath $infoPath -Destination (Join-Path $OutputDirectory 'build-info.json')

Write-Output "Signed portable package: $archivePath"
Write-Output "Signed MSI installer: $msiPath"
Write-Output "Pinned signer: $($certificate.Subject) [$normalizedThumbprint]"
} finally {
    try {
        if ($null -ne $signingCertificateState) {
            Remove-ZeusSigningCertificateFromUserStore -State $signingCertificateState
        }
    } finally {
        if ($null -ne $signingCertificateState) { $signingCertificateState.Certificate.Dispose() }
        $secureCertificatePassword.Dispose()
        foreach ($entry in $certificateCollection) { $entry.Dispose() }
    }
}
