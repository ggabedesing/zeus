$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$signingScript = Join-Path $PSScriptRoot 'sign-windows-release.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($signingScript, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Release signing script has PowerShell syntax errors: $($parseErrors[0].Message)"
}

$signFunction = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-ZeusSigning'
}, $true)
if ($null -eq $signFunction) { throw 'Invoke-ZeusSigning was not found.' }
$signCommands = @($signFunction.Body.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match '^& \$signtoolPath\s'
}, $true))
if ($signCommands.Count -ne 2) { throw 'Expected one SignTool signing command and one verification command.' }
$signCommand = $signCommands | Where-Object { $_.Extent.Text -match '\bsign\b' }
if ($null -eq $signCommand -or $signCommand.Extent.Text -notmatch '/sha1 \$normalizedThumbprint' -or $signCommand.Extent.Text -notmatch '/s My') {
    throw 'SignTool must select the pinned certificate from the current-user My store.'
}
if ($signCommand.Extent.Text -match '(?i)(/p\s|CertificatePassword|CertificatePath)') {
    throw 'The PFX password or PFX path must not be passed to the SignTool process.'
}

$scriptText = Get-Content -LiteralPath $signingScript -Raw
$modulePath = Join-Path $PSScriptRoot 'ZeusSigningCertificateStore.psm1'
$moduleText = Get-Content -LiteralPath $modulePath -Raw
foreach ($required in @('ConvertTo-SecureString', '$secureCertificatePassword.Dispose()', 'Add-ZeusSigningCertificateToUserStore', 'Remove-ZeusSigningCertificateFromUserStore', 'finally {')) {
    if ($scriptText -notlike "*$required*") { throw "Release signing cleanup contract is missing: $required" }
}
foreach ($required in @('Import-PfxCertificate', 'ThumbprintsBeforeImport', 'PfxThumbprints', 'Remove-ZeusPfxCertificatesAddedToUserStore')) {
    if ($moduleText -notlike "*$required*") { throw "Temporary certificate store contract is missing: $required" }
}
if ($moduleText -notmatch '\$_.Thumbprint -notin \$ThumbprintsBeforeImport' -or
    $moduleText -notmatch '\$_.Thumbprint -in \$PfxThumbprints') {
    throw 'Certificate cleanup must remove only PFX certificates added by this run.'
}
if ($scriptText -notmatch 'Remove-Item Env:ZEUS_SIGNING_PFX_PASSWORD') {
    throw 'The PFX password environment variable must be cleared before launching SignTool.'
}

$workflowPath = Join-Path $repository '.github\workflows\ci.yml'
$workflow = (Get-Content -LiteralPath $workflowPath -Raw).Replace("`r`n", "`n")
if ($workflow -notmatch 'Remove-Item Env:ZEUS_SIGNING_PFX_BASE64') {
    throw 'The decoded PFX base64 environment variable must be cleared before launching build/signing tools.'
}
if ($workflow -notmatch 'Remove-Item Env:ZEUS_SIGNING_PFX_PASSWORD') {
    throw 'The PFX password environment variable must be cleared before launching build/signing tools.'
}
$releaseMarker = $workflow.IndexOf("  release:`n", [StringComparison]::Ordinal)
if ($releaseMarker -lt 0) { throw 'The release workflow job was not found.' }
$releaseWorkflow = $workflow.Substring($releaseMarker)
$passwordEnvironmentClear = $releaseWorkflow.IndexOf('Remove-Item Env:ZEUS_SIGNING_PFX_PASSWORD', [StringComparison]::Ordinal)
$buildInvocation = $releaseWorkflow.IndexOf('./scripts/publish-windows.ps1', [StringComparison]::Ordinal)
$signInvocation = $releaseWorkflow.IndexOf('./scripts/sign-windows-release.ps1', [StringComparison]::Ordinal)
if ($passwordEnvironmentClear -lt 0 -or $buildInvocation -lt $passwordEnvironmentClear -or $signInvocation -lt $buildInvocation) {
    throw 'The workflow must clear secret environment variables before building, then pass the password only to the in-process signing script.'
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($null -eq $signtool) {
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signtool = Get-ChildItem -LiteralPath $windowsKits -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
}
if ($null -eq $signtool) { throw 'Windows SDK SignTool is required for the release-signing integration test.' }
$signtoolPath = if ($signtool.Path) { $signtool.Path } else { $signtool.FullName }

Import-Module -Name $modulePath -Force -ErrorAction Stop
$temporaryRoot = [IO.Path]::GetFullPath($env:TEMP)
$testDirectory = Join-Path $temporaryRoot ("ZeusSigningSafety-" + [guid]::NewGuid().ToString('N'))
$testDirectoryFullPath = [IO.Path]::GetFullPath($testDirectory).TrimEnd('\') + '\'
if (!$testDirectoryFullPath.StartsWith($temporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Signing safety test output must remain inside the temporary directory.'
}
New-Item -ItemType Directory -Path $testDirectoryFullPath | Out-Null
$pfxPath = Join-Path $testDirectoryFullPath 'test-signing.pfx'
$probeExecutable = Join-Path $testDirectoryFullPath 'unsigned-probe.exe'
$testCertificate = $null
$testCertificateThumbprint = $null
$testCertificateState = $null
$testCertificateCollection = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
$testPassword = 'Zeus signing test only ' + [guid]::NewGuid().ToString('N')
$secureTestPassword = ConvertTo-SecureString -String $testPassword -AsPlainText -Force
try {
    $testCertificate = New-SelfSignedCertificate -DnsName 'zeus-signing-test.invalid' -Type CodeSigningCert `
        -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -NotAfter (Get-Date).AddDays(2)
    $testCertificateThumbprint = $testCertificate.Thumbprint
    Export-PfxCertificate -Cert $testCertificate -FilePath $pfxPath -Password $secureTestPassword -ChainOption EndEntityCertOnly | Out-Null
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$testCertificateThumbprint" -Force -ErrorAction Stop
    $testCertificate.Dispose()
    $testCertificate = $null

    $testCertificateCollection.Import($pfxPath, $testPassword,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    $pfxThumbprints = @($testCertificateCollection | ForEach-Object Thumbprint)
    if ($testCertificateThumbprint -notin $pfxThumbprints) { throw 'The generated test PFX did not contain its expected certificate.' }

    $testCertificateState = Add-ZeusSigningCertificateToUserStore -PfxPath $pfxPath `
        -Password $secureTestPassword -ExpectedThumbprint $testCertificateThumbprint -PfxThumbprints $pfxThumbprints
    if (!$testCertificateState.ImportedByThisRun -or !$testCertificateState.Certificate.HasPrivateKey) {
        throw 'The temporary test PFX was not imported with its private key.'
    }

    $probeSource = Join-Path $repository 'tools\Zeus.SmokeCheck\bin\Release\net10.0-windows\Zeus.SmokeCheck.exe'
    if (!(Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw 'The build did not produce the unsigned SignTool probe executable.' }
    if ((Get-AuthenticodeSignature -LiteralPath $probeSource).Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
        throw 'The SignTool probe source must be unsigned before the test copies it.'
    }
    Copy-Item -LiteralPath $probeSource -Destination $probeExecutable
    $signOutput = & $signtoolPath sign /fd SHA256 /s My /sha1 $testCertificateThumbprint $probeExecutable 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'SignTool could not sign the temporary executable using the imported store certificate.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $probeExecutable
    if ($signature.SignerCertificate.Thumbprint -ne $testCertificateThumbprint) {
        throw 'The temporary executable signature does not contain the expected test certificate.'
    }

    Remove-ZeusSigningCertificateFromUserStore -State $testCertificateState
    $testCertificateState.Certificate.Dispose()
    $testCertificateState = $null
    if (Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object Thumbprint -eq $testCertificateThumbprint) {
        throw 'The temporary signing certificate remained in the current-user store after cleanup.'
    }
} finally {
    if ($null -ne $testCertificateState) {
        try { Remove-ZeusSigningCertificateFromUserStore -State $testCertificateState } finally { $testCertificateState.Certificate.Dispose() }
    }
    if ($null -ne $testCertificate -and $testCertificateThumbprint) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$testCertificateThumbprint" -Force -ErrorAction SilentlyContinue
        $testCertificate.Dispose()
    }
    if ($testCertificateThumbprint -and (Test-Path -LiteralPath "Cert:\CurrentUser\My\$testCertificateThumbprint")) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$testCertificateThumbprint" -Force -ErrorAction Stop
    }
    foreach ($entry in $testCertificateCollection) { $entry.Dispose() }
    $secureTestPassword.Dispose()
    $testPassword = $null
    foreach ($path in @($pfxPath, $probeExecutable)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
    }
    if (Test-Path -LiteralPath $testDirectoryFullPath -PathType Container) {
        Remove-Item -LiteralPath $testDirectoryFullPath -Force -ErrorAction Stop
    }
}

Write-Output 'PASS: SignTool used the imported test certificate by thumbprint; the certificate was removed and secret values were excluded from SignTool arguments.'
