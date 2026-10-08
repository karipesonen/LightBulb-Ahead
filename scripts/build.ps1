[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '2.7.2.1',
    [string]$UpstreamRef = '2.7.2',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$ArtifactRoot,
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Get-SourceFingerprint {
    $files = @(& git ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
    $entries = foreach ($file in ($files | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $file + ':' + (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        } else {
            $file + ':missing'
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).Replace('-', '')
    } finally { $sha.Dispose() }
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ArtifactRoot) { $ArtifactRoot = Join-Path $root 'artifacts\candidate' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$destination = Join-Path $ArtifactRoot $Version
if (Test-Path -LiteralPath $destination) { throw "Candidate already exists: $destination. Choose a new fork revision." }
if (!$CompilerPath) {
    $CompilerPath = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CompilerPath) { throw 'Inno Setup 6 ISCC.exe is required; pass -CompilerPath if installed elsewhere.' }
$staging = Join-Path $ArtifactRoot ('.building-' + $Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$app = Join-Path $staging 'app'
$null = New-Item -ItemType Directory -Path $app -Force
$originalLocation = Get-Location
$oldSource = $env:INSTALLER_SOURCE_DIR
$oldOutput = $env:INSTALLER_OUTPUT_DIR
$oldVersion = $env:INSTALLER_APP_VERSION
try {
    Set-Location -LiteralPath $root
    $fingerprint = Get-SourceFingerprint
    $sourceCommit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source commit.' }
    & dotnet test '-p:CSharpier_Bypass=true' --configuration Release --logger trx --results-directory (Join-Path $staging 'tests')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; candidate will not be packaged.' }
    if ((Get-SourceFingerprint) -ne $fingerprint) { throw 'Source changed during testing; refusing to package a mixed candidate.' }
    & dotnet publish (Join-Path $root 'LightBulb') '-p:CSharpier_Bypass=true' "-p:Version=$Version" "-p:UpstreamRef=$UpstreamRef" --configuration Release --runtime $Runtime --self-contained --output $app
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; candidate will not be packaged.' }
    if ((Get-SourceFingerprint) -ne $fingerprint) { throw 'Source changed during publish; refusing installer compilation.' }
    if (!(Test-Path -LiteralPath (Join-Path $app 'LightBulb.Fork.exe'))) { throw 'Fork executable missing from published output.' }
    Copy-Item -LiteralPath (Join-Path $root 'License.txt') -Destination $app
    $env:INSTALLER_SOURCE_DIR = $app
    $env:INSTALLER_OUTPUT_DIR = $staging
    $env:INSTALLER_APP_VERSION = $Version
    & $CompilerPath (Join-Path $root 'Installer\Installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $zip = Join-Path $staging "LightBulb.Fork.$Runtime.zip"
    Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip
    if ((Get-SourceFingerprint) -ne $fingerprint) { throw 'Source changed during packaging; candidate was not published or installed.' }
    $currentCommit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $currentCommit -ne $sourceCommit) { throw 'Source commit changed during the build.' }
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record checkout state.' }
    $packages = @($zip, (Join-Path $staging 'LightBulb.Fork-Installer.exe')) | ForEach-Object {
        @{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    }
    @{
        Version = $Version; UpstreamRef = $UpstreamRef; SourceCommit = "$sourceCommit";
        HasUncommittedChanges = [bool]$dirty; SourceFingerprint = $fingerprint; Runtime = $Runtime; TestsPassed = $true;
        Packages = $packages; BuiltAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'candidate.json') -Encoding UTF8
    if (![IO.Path]::GetFullPath($staging).StartsWith($ArtifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging directory is outside the artifact root.'
    }
    Move-Item -LiteralPath $staging -Destination $destination
    Write-Output "Tested candidate: $destination"
} finally {
    $env:INSTALLER_SOURCE_DIR = $oldSource
    $env:INSTALLER_OUTPUT_DIR = $oldOutput
    $env:INSTALLER_APP_VERSION = $oldVersion
    Set-Location -LiteralPath $originalLocation.Path
}
