[CmdletBinding()]
param(
    [ValidateSet(500)] [double]$Temperature = 500,
    [ValidateRange(0.1, 1)] [double]$Brightness = 1,
    [switch]$Polling,
    [switch]$Smoothing,
    [ValidateSet('Green', 'Blue')] [string]$Boundary,
    [switch]$RefreshOffsets
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($RefreshOffsets -and !$Polling) { throw 'Refresh-offset observation requires polling.' }
if (@(Get-Process -Name LightBulb,LightBulb.Fork -ErrorAction SilentlyContinue).Count) { throw 'Exit both gamma controllers before the probe.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ForkDisplayProbe {
    [StructLayout(LayoutKind.Sequential)]
    public struct Ramp {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=256)] public ushort[] Blue;
    }
    [DllImport("gdi32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr init);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool GetDeviceGammaRamp(IntPtr dc, ref Ramp ramp);
    [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr dc, ref Ramp ramp);
    public static Ramp Read(IntPtr dc) {
        var ramp = new Ramp { Red=new ushort[256], Green=new ushort[256], Blue=new ushort[256] };
        if (!GetDeviceGammaRamp(dc, ref ramp)) throw new Exception("Gamma readback failed.");
        return ramp;
    }
    public static bool Equal(Ramp a, Ramp b) {
        for (int i=0;i<256;i++) if(a.Red[i]!=b.Red[i] || a.Green[i]!=b.Green[i] || a.Blue[i]!=b.Blue[i]) return false;
        return true;
    }
}
'@
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $root ('artifacts\probe\' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$null = New-Item -ItemType Directory -Path $evidence -Force
$settingsPath = Join-Path $evidence 'Settings.json'
$profile = Get-Content -LiteralPath (Join-Path $env:APPDATA 'LightBulb\Settings.json') -Raw | ConvertFrom-Json
$profile.DayConfiguration.Temperature = $Temperature
$profile.NightConfiguration.Temperature = $Temperature
$profile.DayConfiguration.Brightness = $Brightness
$profile.NightConfiguration.Brightness = $Brightness
$profile.IsGammaPollingEnabled = [bool]$Polling
$profile.IsConfigurationSmoothingEnabled = [bool]$Smoothing
$profile.IsFirstTimeExperienceEnabled = $false
$profile.IsUkraineSupportMessageEnabled = $false
$profile.IsPauseWhenFullScreenEnabled = $false
$profile.IsApplicationWhitelistEnabled = $false
$profile.IsAutoUpdateEnabled = $false
if ($Boundary) {
    $nightTemperature = if ($Boundary -eq 'Green') { 500 } else { 1900 }
    $profile.NightConfiguration.Temperature = $nightTemperature
    $profile.DayConfiguration.Temperature = $nightTemperature + 10
    $sunrise = [DateTimeOffset]::Now.AddSeconds(16)
    $sunset = $sunrise.AddSeconds(6)
    $profile.IsManualSunriseSunsetEnabled = $true
    $profile.ManualSunriseTime = $sunrise.ToString('HH:mm:ss')
    $profile.ManualSunsetTime = $sunset.ToString('HH:mm:ss')
    $profile | Add-Member -NotePropertyName MorningFade -NotePropertyValue @{ Duration = '00:00:00'; FinishOffset = '00:00:00' } -Force
    $profile | Add-Member -NotePropertyName EveningFade -NotePropertyValue @{ Duration = '00:00:00'; FinishOffset = '00:00:00' } -Force
}
$profile | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
$executable = Join-Path $env:LOCALAPPDATA 'Programs\LightBulb.Fork\LightBulb.Fork.exe'
$dc = [ForkDisplayProbe]::CreateDC('\\.\DISPLAY1', '\\.\DISPLAY1', $null, [IntPtr]::Zero)
if ($dc -eq [IntPtr]::Zero) { throw 'Cannot open the active monitor device context.' }
$baseline = [ForkDisplayProbe]::Read($dc)
$baseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'baseline.json') -Encoding UTF8
$oldOverride = $env:LIGHTBULB_FORK_SETTINGS_PATH
$process = $null
$readback = $null
$phases = [Collections.Generic.List[object]]::new()
$offsets = [Collections.Generic.HashSet[int]]::new()
function Wait-ForTarget([bool]$PositiveBoundaryChannel, [DateTimeOffset]$Deadline) {
    $expectedRed = [int][Math]::Truncate(65025 * $Brightness)
    do {
        if ([DateTimeOffset]::Now -ge $Deadline) { break }
        if ($process.HasExited) { throw "Fork exited early ($($process.ExitCode))." }
        $ramp = [ForkDisplayProbe]::Read($dc)
        $greenPositive = @($ramp.Green | Where-Object { $_ -ne 0 }).Count -gt 0
        $bluePositive = @($ramp.Blue | Where-Object { $_ -ne 0 }).Count -gt 0
        $correctChannels = if ($Boundary -eq 'Blue') {
            $greenPositive -and ($bluePositive -eq $PositiveBoundaryChannel)
        } elseif ($Boundary -eq 'Green') {
            !$bluePositive -and ($greenPositive -eq $PositiveBoundaryChannel)
        } else { !$greenPositive -and !$bluePositive }
        $correctRed = $ramp.Red[255] -ge $expectedRed -and $ramp.Red[255] -le ($expectedRed + 4)
        if ($correctChannels -and $correctRed -and [DateTimeOffset]::Now -lt $Deadline) { return $ramp }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::Now -lt $Deadline)
    throw 'Readback did not reach the requested endpoint before its observation deadline.'
}
try {
    $env:LIGHTBULB_FORK_SETTINGS_PATH = $settingsPath
    $process = Start-Process -FilePath $executable -ArgumentList '--start-hidden' -WindowStyle Hidden -PassThru
    if ($Boundary) {
        $before = Wait-ForTarget $false $sunrise.AddSeconds(-1)
        $phases.Add(@{ Phase = 'night-before'; Instant = [DateTimeOffset]::Now.ToString('o'); Ramp = $before })
        $day = Wait-ForTarget $true $sunset.AddSeconds(-1)
        $phases.Add(@{ Phase = 'day'; Instant = [DateTimeOffset]::Now.ToString('o'); Ramp = $day })
        $readback = Wait-ForTarget $false $sunset.AddSeconds(5)
        $phases.Add(@{ Phase = 'night-after'; Instant = [DateTimeOffset]::Now.ToString('o'); Ramp = $readback })
    } else {
        $readback = Wait-ForTarget $false ([DateTimeOffset]::Now.AddSeconds(30))
    }
    if ($RefreshOffsets) {
        $deadline = [DateTimeOffset]::Now.AddSeconds(10)
        $expectedRed = [int][Math]::Truncate(65025 * $Brightness)
        do {
            $readback = Wait-ForTarget $false $deadline
            $null = $offsets.Add([int]$readback.Red[255] - $expectedRed)
            Start-Sleep -Milliseconds 100
        } while ($offsets.Count -lt 5 -and [DateTimeOffset]::Now -lt $deadline)
        if ($offsets.Count -ne 5) { throw 'Not all refresh offsets were observed.' }
    }
    $readback | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'readback.json') -Encoding UTF8
    if ($phases.Count) { $phases.ToArray() | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'phases.json') -Encoding UTF8 }
} finally {
    $env:LIGHTBULB_FORK_SETTINGS_PATH = $oldOverride
    if ($process -and !$process.HasExited) {
        $exitRequest = Start-Process -FilePath $executable -ArgumentList '--exit' -WindowStyle Hidden -PassThru -Wait
        if ($exitRequest.ExitCode -ne 0 -or !$process.WaitForExit(10000)) { throw 'Normal shutdown failed; exit the fork from its tray menu before continuing.' }
    }
    if (![ForkDisplayProbe]::SetDeviceGammaRamp($dc, [ref]$baseline)) { throw 'Display restoration was rejected.' }
    $restored = [ForkDisplayProbe]::Read($dc)
    if (![ForkDisplayProbe]::Equal($baseline, $restored)) { throw 'Display restoration did not match captured baseline.' }
    [ForkDisplayProbe]::DeleteDC($dc) | Out-Null
}
@{ Temperature = $Temperature; Boundary = $Boundary; Brightness = $Brightness; Polling = [bool]$Polling; Smoothing = [bool]$Smoothing; ObservedRefreshOffsets = @($offsets | Sort-Object); Version = (Get-Item -LiteralPath $executable).VersionInfo.FileVersion; ExactZeroReadback = $true; BaselineRestored = $true } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
Write-Output "Live endpoint readback and exact restoration verified: $evidence"
