[CmdletBinding()]
param(
    [string]$UpstreamRef,
    [ValidateRange(1, 65534)]
    [int]$ForkRevision = 2,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$originalLocation = Get-Location
try {
    Set-Location -LiteralPath $root
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect checkout.' }
    if ($dirty) { throw 'Commit or stash local changes before an upstream update. No checkout changes were made.' }
    if (!$UpstreamRef) {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/Tyrrrz/LightBulb/releases/latest' -Headers @{ 'User-Agent' = 'LightBulb-Fork-LocalBuild' }
        if ($release.prerelease -or $release.draft) { throw 'Latest release is not stable; specify -UpstreamRef explicitly.' }
        $UpstreamRef = $release.tag_name
    }
    if ($UpstreamRef.StartsWith('-')) { throw 'An upstream ref cannot be a Git option.' }
    & git fetch upstream --tags
    if ($LASTEXITCODE -ne 0) { throw 'Upstream fetch failed.' }
    $upstreamCommit = & git rev-parse --verify "$UpstreamRef^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Unknown upstream ref: $UpstreamRef" }
    $tag = & git describe --tags --match '[0-9]*' --abbrev=0 $upstreamCommit
    if ($LASTEXITCODE -ne 0 -or $tag -notmatch '^\d+\.\d+\.\d+$') { throw 'Cannot derive a numeric upstream release version.' }
    $version = "$tag.$ForkRevision"
    $suffix = $upstreamCommit.Substring(0, 8) + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $worktree = Join-Path $root ".worktrees\update-$suffix"
    $branch = "candidate/update-$suffix"
    & git worktree add -b $branch $worktree HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated update checkout.' }
    Set-Location -LiteralPath $worktree
    & git merge --no-edit --no-ff $upstreamCommit
    if ($LASTEXITCODE -ne 0) { throw "Merge failed. Inspect retained worktree $worktree; the main checkout was not modified." }
    & (Join-Path $worktree 'scripts\build.ps1') -Version $version -UpstreamRef $upstreamCommit -ArtifactRoot (Join-Path $root 'artifacts\candidate')
    $candidate = Join-Path $root "artifacts\candidate\$version"
    if (!(Test-Path -LiteralPath (Join-Path $candidate 'candidate.json'))) { throw 'Build did not produce a verified candidate.' }
    Write-Output "Upstream update retained on $branch at $worktree"
    Write-Output "Candidate: $candidate"
    if ($Install) {
        $installer = Join-Path $root 'scripts\install.ps1'
        if (!(Test-Path -LiteralPath $installer)) { throw 'The verified candidate exists, but the local install helper is not available.' }
        & $installer -CandidatePath $candidate
    }
} finally {
    Set-Location -LiteralPath $originalLocation.Path
}
