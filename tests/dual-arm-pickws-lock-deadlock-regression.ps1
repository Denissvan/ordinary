param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

# 2026-11-12 log: module 2 held the shared pickws lock inside PickWsMod while waiting for dual-arm
# avoidance (needed module 1 to move away). Module 1 had nextProc=PICKTRAY but blocked on
# "lock (pickws)" every loop iteration, so it could never move -> both arms stalled until the
# 30 s avoidance timeout / operator pause (3 occurrences: 14:29:40, 14:31:30, 14:32:43).

$upDownLoad = Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'
$source = Get-Content -Raw -LiteralPath $upDownLoad

if ($source.Contains('lock (pickws)')) {
    throw 'pickws must not be taken with an unbounded lock; the other arm may hold it while waiting for this arm to yield.'
}

$cases = @(
    @{ Lock = 'pickws';    Need = 'needPickWs';    Taken = 'pickWsLockTaken' },
    @{ Lock = 'placetray'; Need = 'needPlaceTray'; Taken = 'placeTrayLockTaken' },
    @{ Lock = 'picktray';  Need = 'needPickTray';  Taken = 'pickTrayLockTaken' },
    @{ Lock = 'placews';   Need = 'needPlaceWs';   Taken = 'placeWsLockTaken' }
)

foreach ($c in $cases) {
    $tryEnter = "Monitor.TryEnter($($c.Lock), 100, ref $($c.Taken));"
    $tryIndex = $source.IndexOf($tryEnter, [System.StringComparison]::Ordinal)
    if ($tryIndex -lt 0) { throw "$($c.Lock) must use a bounded TryEnter." }

    $gate = "if ($($c.Need))"
    $gateIndex = $source.LastIndexOf($gate, $tryIndex, [System.StringComparison]::Ordinal)
    if ($gateIndex -lt 0 -or ($tryIndex - $gateIndex) -gt 200) {
        throw "$($c.Lock) must only be contended when this arm actually needs that step ($($c.Need))."
    }

    $exit = "if ($($c.Taken)) Monitor.Exit($($c.Lock));"
    if ($source.IndexOf($exit, $tryIndex, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$($c.Lock) must be released in finally."
    }
}

Write-Host 'PASS: step locks are only contended when needed and never block an arm the other arm is waiting on.'
