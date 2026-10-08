# Local Fork Workflow

Fade durations use `[days.]hours:minutes:seconds`: `02:30:00` is 2 hours 30 minutes;
`1.06:00:00` is 30 hours. Enter days explicitly for durations over 24 hours.
Start/finish summaries show local clock times; long fades may start on a prior day.

Run from the repository root. All changes stay local; no GitHub publishing occurs.

This tests and creates an installer/portable candidate without installing it.

```powershell
.\scripts\build.ps1 -Version 2.7.2.6 -UpstreamRef 2.7.2
```

This requests normal fork shutdown; it does not stop the original LightBulb.

```powershell
& "$env:LOCALAPPDATA\Programs\LightBulb.Fork\LightBulb.Fork.exe" --exit
```

This verifies and installs that candidate, retaining the previous package/settings.

```powershell
.\scripts\install.ps1 -CandidatePath artifacts/candidate/2.7.2.6
```

This restores the previous fork package and settings from the last upgrade snapshot.

```powershell
$state = Get-Content artifacts/installed.json -Raw | ConvertFrom-Json
.\scripts\install.ps1 -RollbackPath $state.LastRollbackPath
```

This fetches upstream and builds an isolated merge candidate; a clean committed
checkout and configured Git author are required. Add -Install only to install a
successful candidate. Failed merges remain in their named worktree for inspection.

```powershell
.\scripts\update-upstream.ps1 -UpstreamRef 2.7.2 -ForkRevision 7
```

Build owns artifacts/candidate; install owns artifacts/installed.json and rollback
snapshots. Probe owns artifacts/probe. Superseded artifacts may be removed explicitly
after retaining any package referenced by installed.json or needed for rollback.
Do not remove Git worktrees as ordinary folders: archive/remove them through Git.
