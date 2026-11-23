param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$myfuncPath = Join-Path $RepoRoot "UI\Class\myfunc.cs"
$wsPath = Join-Path $RepoRoot "UI\Class\WS.cs"
$myfunc = Get-Content -LiteralPath $myfuncPath -Raw -Encoding UTF8
$ws = Get-Content -LiteralPath $wsPath -Raw -Encoding UTF8

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$hallStart = $myfunc.IndexOf("if (PT_SET.HallOnFeedFace && !WS.Demo)")
$runHall = $myfunc.IndexOf("res = workstation.RunHallStage(ref VAR.gsys_set.bquit, WS.Demo);", $hallStart)
$setupForTest = $myfunc.IndexOf("res = workstation.SetupForTest(ref VAR.gsys_set.bquit);", $runHall)
$waitAllStations = $myfunc.IndexOf("COM.ws1.WaitTestTask(ref VAR.gsys_set.bquit);", $runHall)

Assert-Condition ($hallStart -ge 0) "Hall flow was not found."
Assert-Condition ($runHall -gt $hallStart) "Hall flow no longer runs RunHallStage."
Assert-Condition ($setupForTest -gt $runHall -and $setupForTest -lt $waitAllStations) "Hall completion must call SetupForTest before waiting for all workstations."

$downFlipGuard = $ws.IndexOf("if (sta == 403 && PT_SET.bDownFlipTest && !demo)")
$run403 = $ws.IndexOf("return Run403TurnStageAfterSta(ref bquit, demo);", $downFlipGuard)
$run403Method = $ws.IndexOf("public EM_RES Run403TurnStageAfterSta(ref bool bquit, bool demo = false)")
$setupForTestPos2 = $ws.IndexOf("res = SetupForTestPos2(ref bquit);", $run403Method)
$returnToTestPos = $ws.IndexOf("res = TurnToTest(ref bquit);", $setupForTestPos2)

Assert-Condition ($downFlipGuard -ge 0) "403 down-flip must remain guarded by bDownFlipTest."
Assert-Condition ($run403 -gt $downFlipGuard) "403 down-flip guard must continue into Run403TurnStageAfterSta."
Assert-Condition ($run403Method -ge 0) "403 down-flip flow was not found."
Assert-Condition ($setupForTestPos2 -gt $run403Method) "403 flow must still enter position 2."
Assert-Condition ($returnToTestPos -gt $setupForTestPos2) "403 flow must still return to the standard test position."

Write-Host "PASS: Hall standard flip and guarded 403 down-flip flow are both present."
