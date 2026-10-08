[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Install')]
    [string]$CandidatePath,
    [Parameter(Mandatory, ParameterSetName = 'Rollback')]
    [string]$RollbackPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $root 'artifacts'
$statePath = Join-Path $artifacts 'installed.json'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\LightBulb.Fork'
$executable = Join-Path $installDir 'LightBulb.Fork.exe'
$settings = Join-Path $env:APPDATA 'LightBulb.Fork\Settings.json'
$originalSettings = Join-Path $env:APPDATA 'LightBulb\Settings.json'
$originalHash = if (Test-Path -LiteralPath $originalSettings) { (Get-FileHash -LiteralPath $originalSettings).Hash } else { $null }
$rollback = $null
if ($PSCmdlet.ParameterSetName -eq 'Rollback') {
    $RollbackPath = [IO.Path]::GetFullPath($RollbackPath)
    $rollbackRoot = Join-Path $artifacts 'rollback'
    if (!$RollbackPath.StartsWith($rollbackRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Rollback must use a snapshot created by this project under artifacts/rollback.'
    }
    $rollback = Get-Content -LiteralPath (Join-Path $RollbackPath 'rollback.json') -Raw | ConvertFrom-Json
    $CandidatePath = Join-Path $RollbackPath 'package'
}
$CandidatePath = [IO.Path]::GetFullPath($CandidatePath)
$manifestPath = Join-Path $CandidatePath 'candidate.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.TestsPassed -ne $true -or $manifest.Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Candidate is not verified.' }
foreach ($package in $manifest.Packages) {
    if ([IO.Path]::GetFileName($package.Name) -ne $package.Name) { throw 'Invalid package filename.' }
    if ((Get-FileHash -LiteralPath (Join-Path $CandidatePath $package.Name)).Hash -ne $package.SHA256) { throw "Package integrity failure: $($package.Name)" }
}
$installerName = 'LightBulb.Fork-Installer.exe'
if (@($manifest.Packages | Where-Object Name -eq $installerName).Count -ne 1) { throw 'Verified fork installer is missing.' }
$zipName = "LightBulb.Fork.$($manifest.Runtime).zip"
if (@($manifest.Packages | Where-Object Name -eq $zipName).Count -ne 1) { throw 'Verified portable package is missing.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $CandidatePath $zipName))
try {
    $entry = $zip.GetEntry('LightBulb.Fork.exe')
    if (!$entry) { throw 'Portable package lacks fork executable.' }
    $stream = $entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $expectedHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
} finally { $zip.Dispose() }

$running = @(Get-Process -Name 'LightBulb.Fork' -ErrorAction SilentlyContinue)
if ($running.Count) { throw 'Exit the fork normally before installing or rolling back. No process was killed.' }
$snapshot = $null
if ($PSCmdlet.ParameterSetName -eq 'Install' -and (Test-Path -LiteralPath $executable)) {
    if (!(Test-Path -LiteralPath $statePath)) { throw 'Existing fork installation has no managed package record; refusing to overwrite it.' }
    $previous = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $executable).Hash -ne $previous.ExecutableSHA256) { throw 'Installed fork differs from the recorded package.' }
    $previousManifest = Get-Content -LiteralPath (Join-Path $previous.CandidatePath 'candidate.json') -Raw | ConvertFrom-Json
    $snapshot = Join-Path $artifacts ('rollback\' + $previous.Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $backupPackage = Join-Path $snapshot 'package'
    $null = New-Item -ItemType Directory -Path $backupPackage -Force
    foreach ($package in $previousManifest.Packages) {
        $source = Join-Path $previous.CandidatePath $package.Name
        if ((Get-FileHash -LiteralPath $source).Hash -ne $package.SHA256) { throw 'Previous package integrity failure; refusing upgrade without recoverable backup.' }
        Copy-Item -LiteralPath $source -Destination $backupPackage
    }
    Copy-Item -LiteralPath (Join-Path $previous.CandidatePath 'candidate.json') -Destination $backupPackage
    $settingsExisted = Test-Path -LiteralPath $settings
    $settingsHash = $null
    if ($settingsExisted) {
        Copy-Item -LiteralPath $settings -Destination (Join-Path $snapshot 'Settings.json')
        $settingsHash = (Get-FileHash -LiteralPath $settings).Hash
    }
    @{ SettingsExisted = $settingsExisted; SettingsSHA256 = $settingsHash; PreviousVersion = $previous.Version } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $snapshot 'rollback.json') -Encoding UTF8
}
if ($rollback -and $rollback.SettingsExisted) {
    if ((Get-FileHash -LiteralPath (Join-Path $RollbackPath 'Settings.json')).Hash -ne $rollback.SettingsSHA256) { throw 'Rollback settings integrity failure.' }
}
$null = New-Item -ItemType Directory -Path $artifacts -Force
$log = Join-Path $artifacts ('install-' + $manifest.Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.log')
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$log`"")
$process = Start-Process -FilePath (Join-Path $CandidatePath $installerName) -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer failed ($($process.ExitCode)); inspect $log. Backup: $snapshot" }
if ((Get-FileHash -LiteralPath $executable).Hash -ne $expectedHash) { throw 'Installed executable does not match the verified package.' }
if ($rollback) {
    if ($rollback.SettingsExisted) {
        $null = New-Item -ItemType Directory -Path (Split-Path $settings) -Force
        Copy-Item -LiteralPath (Join-Path $RollbackPath 'Settings.json') -Destination $settings -Force
        if ((Get-FileHash -LiteralPath $settings).Hash -ne $rollback.SettingsSHA256) { throw 'Restored settings do not match snapshot.' }
    } elseif (Test-Path -LiteralPath $settings) {
        Remove-Item -LiteralPath $settings
    }
}
if ($originalHash -and (Get-FileHash -LiteralPath $originalSettings).Hash -ne $originalHash) { throw 'Original LightBulb settings changed unexpectedly.' }
@{ Version = $manifest.Version; CandidatePath = $CandidatePath; ExecutableSHA256 = $expectedHash; LastRollbackPath = $snapshot } |
    ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
Write-Output "Installed and hash-verified: $executable ($($manifest.Version)). App was not launched."
if ($snapshot) { Write-Output "Rollback snapshot: $snapshot" }
