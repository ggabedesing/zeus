param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,
    [string]$ProductVersion = '1.0.0',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
if (!(Test-Path -LiteralPath (Join-Path $PayloadDirectory 'Zeus.Desktop.exe') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $PayloadDirectory 'Zeus.Maintenance.exe') -PathType Leaf)) {
    throw 'Payload must be a complete published ZEUS folder containing both application executables.'
}
if ($ProductVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'ProductVersion must contain three or four numeric fields.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repository 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

dotnet build (Join-Path $PSScriptRoot '../installer/Zeus.Installer.wixproj') -c Release -t:Rebuild `
    "-p:PayloadDirectory=$PayloadDirectory" "-p:ProductVersion=$ProductVersion" "-p:OutputPath=$OutputDirectory\"
if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed.' }
$installer = Join-Path $OutputDirectory 'Zeus.Installer.msi'
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'WiX build succeeded but installer output was not found.' }
$msi = New-Object -ComObject WindowsInstaller.Installer
$database = $msi.OpenDatabase($installer, 0)
$view = $database.OpenView('SELECT `File`, `FileName`, `Language`, `Version` FROM `File` WHERE `File`=''SQLiteNativeLibraryFile''')
$view.Execute()
$sqliteFile = $view.Fetch()
$view.Close()
if ($null -eq $sqliteFile -or $sqliteFile.StringData(2) -notmatch 'e_sqlite3\.dll$' -or
    $sqliteFile.StringData(3) -ne '0' -or [string]::IsNullOrWhiteSpace($sqliteFile.StringData(4))) {
    throw 'The MSI must contain the language-neutral SQLite native library with LANGID 0 and its file version.'
}
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$installerHash  Zeus.Installer.msi" | Set-Content "$installer.sha256" -Encoding ascii
Write-Output "Windows MSI installer: $installer"
Write-Output "SHA-256: $installerHash"
