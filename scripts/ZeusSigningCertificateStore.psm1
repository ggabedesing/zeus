function Remove-ZeusPfxCertificatesAddedToUserStore {
    param(
        [Parameter(Mandatory = $true)] [string[]]$PfxThumbprints,
        [Parameter(Mandatory = $true)] [string[]]$ThumbprintsBeforeImport
    )

    $storePath = 'Cert:\CurrentUser\My'
    $createdFromPfx = @(
        Get-ChildItem -LiteralPath $storePath -ErrorAction Stop |
            Where-Object {
                $_.Thumbprint -notin $ThumbprintsBeforeImport -and
                $_.Thumbprint -in $PfxThumbprints
            }
    )
    foreach ($entry in $createdFromPfx) {
        Remove-Item -LiteralPath $entry.PSPath -Force -ErrorAction Stop
    }
}

function Add-ZeusSigningCertificateToUserStore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string]$PfxPath,
        [Parameter(Mandatory = $true)] [System.Security.SecureString]$Password,
        [Parameter(Mandatory = $true)] [string]$ExpectedThumbprint,
        [Parameter(Mandatory = $true)] [string[]]$PfxThumbprints
    )

    $storePath = 'Cert:\CurrentUser\My'
    $thumbprintsBeforeImport = @(
        Get-ChildItem -LiteralPath $storePath -ErrorAction Stop |
            ForEach-Object Thumbprint
    )
    $existing = @(
        Get-ChildItem -LiteralPath $storePath -ErrorAction Stop |
            Where-Object Thumbprint -eq $ExpectedThumbprint
    )
    if ($existing.Count -gt 1) {
        throw 'More than one certificate with the pinned thumbprint exists in the current-user store.'
    }
    if ($existing.Count -eq 1 -and !$existing[0].HasPrivateKey) {
        throw 'The pinned certificate already exists in the current-user store without a private key; signing stopped without replacing it.'
    }

    $importStarted = $false
    try {
        if ($existing.Count -eq 0) {
            $importStarted = $true
            Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation $storePath `
                -Password $Password -ErrorAction Stop | Out-Null
        }

        $pinned = @(
            Get-ChildItem -LiteralPath $storePath -ErrorAction Stop |
                Where-Object Thumbprint -eq $ExpectedThumbprint
        )
        if ($pinned.Count -ne 1 -or !$pinned[0].HasPrivateKey) {
            throw 'The pinned private-key certificate was not available in the current-user My store.'
        }

        return [pscustomobject]@{
            Certificate = $pinned[0]
            ImportedByThisRun = $importStarted
            ThumbprintsBeforeImport = $thumbprintsBeforeImport
            PfxThumbprints = $PfxThumbprints
        }
    } catch {
        if ($importStarted) {
            Remove-ZeusPfxCertificatesAddedToUserStore -PfxThumbprints $PfxThumbprints `
                -ThumbprintsBeforeImport $thumbprintsBeforeImport
        }
        throw
    }
}

function Remove-ZeusSigningCertificateFromUserStore {
    param([Parameter(Mandatory = $true)] [psobject]$State)

    if (!$State.ImportedByThisRun) { return }
    Remove-ZeusPfxCertificatesAddedToUserStore -PfxThumbprints $State.PfxThumbprints `
        -ThumbprintsBeforeImport $State.ThumbprintsBeforeImport
}

Export-ModuleMember -Function Add-ZeusSigningCertificateToUserStore, Remove-ZeusSigningCertificateFromUserStore
