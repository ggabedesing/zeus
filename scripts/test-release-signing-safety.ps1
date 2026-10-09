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
foreach ($required in @('ConvertTo-SecureString', 'Import-PfxCertificate', '$secureCertificatePassword.Dispose()', 'finally {')) {
    if ($scriptText -notlike "*$required*") { throw "Release signing cleanup contract is missing: $required" }
}
if ($scriptText -notmatch '\$_.Thumbprint -notin \$thumbprintsBeforeSigning' -or
    $scriptText -notmatch '\$_.Thumbprint -in \$pfxThumbprints') {
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

Write-Output 'PASS: signing uses the pinned certificate store entry; secrets are excluded from SignTool arguments and temporary certificate entries are cleaned.'
