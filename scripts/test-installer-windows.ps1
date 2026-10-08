param(
    [Parameter(Mandatory = $true)]
    [string]$PreviousInstaller,
    [Parameter(Mandatory = $true)]
    [string]$CurrentInstaller,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [string]$LogDirectory = ''
)

$ErrorActionPreference = 'Stop'
$PreviousInstaller = [IO.Path]::GetFullPath($PreviousInstaller)
$CurrentInstaller = [IO.Path]::GetFullPath($CurrentInstaller)
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'ExpectedVersion must contain three or four numeric fields.' }
foreach ($installer in @($PreviousInstaller, $CurrentInstaller)) {
    if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer not found: $installer" }
}
if ([string]::IsNullOrWhiteSpace($LogDirectory)) { $LogDirectory = Join-Path $env:RUNNER_TEMP 'zeus-installer-smoke' }
$LogDirectory = [IO.Path]::GetFullPath($LogDirectory)
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installDirectory = Join-Path $programFiles 'ZEUS'
$shortcutDirectory = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\ZEUS'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'Zeus'
$sentinel = Join-Path $dataDirectory 'installer-smoke-preservation.txt'
$uninstallRoots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)
$msiexec = Join-Path $env:SystemRoot 'System32\msiexec.exe'
$sentinelCreated = $false

function Get-ZeusUninstallEntries {
    foreach ($root in $uninstallRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($entry.DisplayName -eq 'ZEUS' -and $entry.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') {
                [pscustomobject]@{ Key = $key.PSChildName; Version = [string]$entry.DisplayVersion; Path = $key.PSPath }
            }
        }
    }
}

function Invoke-Msi([string[]]$Arguments, [string]$LogName) {
    $logPath = Join-Path $LogDirectory $LogName
    $quotedLogPath = '"' + $logPath + '"'
    $process = Start-Process -FilePath $msiexec -ArgumentList (@($Arguments) + @('/qn', '/norestart', '/l*v', $quotedLogPath)) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "msiexec exited with $($process.ExitCode); inspect $logPath" }
}

function Assert-InstalledVersion([string]$Version) {
    $entries = @(Get-ZeusUninstallEntries)
    if ($entries.Count -ne 1 -or $entries[0].Version -ne $Version) {
        throw "Expected one ZEUS uninstall entry at version $Version; found $($entries.Count) entry/entries: $($entries.Version -join ', ')."
    }
    if (!(Test-Path -LiteralPath (Join-Path $installDirectory 'Zeus.Desktop.exe') -PathType Leaf) -or
        !(Test-Path -LiteralPath (Join-Path $installDirectory 'Zeus.Maintenance.exe') -PathType Leaf)) {
        throw 'The installed application or maintenance helper is missing.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $shortcutDirectory 'ZEUS.lnk') -PathType Leaf)) {
        throw 'The Start menu shortcut is missing.'
    }
}

if (@(Get-ZeusUninstallEntries).Count -ne 0 -or (Test-Path -LiteralPath $installDirectory)) {
    throw 'The hosted runner is not clean; refusing to touch an existing ZEUS installation.'
}

try {
    Invoke-Msi @('/i', ('"' + $PreviousInstaller + '"')) 'install-previous.log'
    $previousVersion = (Get-ZeusUninstallEntries | Select-Object -First 1).Version
    if ([string]::IsNullOrWhiteSpace($previousVersion)) { throw 'The previous MSI did not register ZEUS.' }
    Assert-InstalledVersion $previousVersion

    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    'preserve' | Set-Content -LiteralPath $sentinel -Encoding ascii
    $sentinelCreated = $true

    Invoke-Msi @('/i', ('"' + $CurrentInstaller + '"')) 'major-upgrade.log'
    Assert-InstalledVersion $ExpectedVersion
    if (!(Test-Path -LiteralPath $sentinel -PathType Leaf)) { throw 'The major upgrade removed user-local data.' }

    Invoke-Msi @('/x', ('"' + $CurrentInstaller + '"')) 'uninstall.log'
    if (@(Get-ZeusUninstallEntries).Count -ne 0) { throw 'ZEUS is still registered after uninstall.' }
    if (Test-Path -LiteralPath $installDirectory) { throw 'The ZEUS installation directory remains after uninstall.' }
    if (Test-Path -LiteralPath $shortcutDirectory) { throw 'The ZEUS Start menu shortcut directory remains after uninstall.' }
    if (!(Test-Path -LiteralPath $sentinel -PathType Leaf)) { throw 'Uninstall removed user-local data.' }

    Write-Output "Installer smoke passed: $previousVersion -> $ExpectedVersion -> uninstalled; local data preserved."
}
finally {
    foreach ($entry in @(Get-ZeusUninstallEntries)) {
        try { Invoke-Msi @('/x', $entry.Key) ('cleanup-' + $entry.Version + '.log') } catch { Write-Warning $_ }
    }
    if ($sentinelCreated -and (Test-Path -LiteralPath $sentinel -PathType Leaf)) {
        Remove-Item -LiteralPath $sentinel -Force
        if ((Test-Path -LiteralPath $dataDirectory) -and
            @(Get-ChildItem -LiteralPath $dataDirectory -Force -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item -LiteralPath $dataDirectory -Force
        }
    }
}
