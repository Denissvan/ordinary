param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot), [switch]$StateOnly)
$ErrorActionPreference = 'Stop'
$state = Join-Path $RepoRoot 'UI\Class\FeedDistanceInspectionState.cs'
if (-not (Test-Path -LiteralPath $state)) { throw 'Missing feed inspection cycle/operation gate.' }
$exe = Join-Path $env:TEMP ('FeedDistanceRegression-' + [Guid]::NewGuid().ToString('N') + '.exe')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try {
    & $csc /nologo /target:exe "/out:$exe" $state (Join-Path $PSScriptRoot 'FeedDistanceInspectionRegression.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Regression compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Behavior regression failed.' }
} finally { if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe } }
if ($StateOnly) { return }
$master = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI\Class\myfunc.cs'))
$master = $master.Substring($master.IndexOf('public static void test_th()'))
$loading = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'))
$table = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI\Class\Turntable.cs'))
$schedule = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI\FormView\FrMain.EnvironmentSchedule.cs'))
$runtime = [IO.File]::ReadAllText((Join-Path $RepoRoot 'UI\Class\FeedDistanceInspection.cs'))
if ($master.IndexOf('FeedDistanceInspection.RunBeforeFeed(workstation)') -lt 0 -or
    $master.IndexOf('FeedDistanceInspection.RunBeforeFeed(workstation)') -gt $master.IndexOf('res = workstation.SetupForFeed')) { throw 'Inspection must precede opening clamps in SetupForFeed.' }
if (-not $runtime.Contains('PrepareHorizontal(ws)') -or
    -not $runtime.Contains('ws.isUInFeedPos') -or
    -not $runtime.Contains('AxisStopped(ws.ax_u)') -or
    -not $runtime.Contains('ClampsHeld(ws)')) { throw 'Inspection requires horizontal preparation, stopped U axis and held clamps.' }
if ($runtime.Contains('ws.isInFeedPos')) { throw 'Open-clamp feed pose is not the inspection pose.' }
if ($runtime.IndexOf('PrepareHorizontal(ws)') -gt $runtime.IndexOf('comm.SetLaserStatus(true)')) { throw 'Laser cannot precede horizontal preparation.' }
foreach ($text in @('AuthorizeLoading(workstation)', 'TryResetEnvironmentCycle(workstation)')) {
    if (-not $master.Contains($text)) { throw "Missing master integration: $text" }
}
if (-not $loading.Contains('FeedDistanceInspection.TryBeginLoading(ws, out feedMotion)') -or
    -not $loading.Contains('feedMotion.Dispose()')) { throw 'Loading consumer needs a scoped admission guard.' }
if (-not $table.Contains('FeedDistanceInspection.TryBeginMotion(out inspectionMotion)')) { throw 'Turntable motion must exclude inspection.' }
if (-not $schedule.Contains('FeedDistanceInspection.RequestEnvironmentCheck()')) { throw 'Scheduler must request the feed cycle.' }
if (-not $runtime.Contains('finally') -or -not $runtime.Contains('SetLaserStatus(false)') -or
    -not $runtime.Contains('guard.Release()')) { throw 'Distance operation needs unconditional cleanup.' }
Write-Host 'PASS: source integration contracts (not hardware/concurrency execution).'
