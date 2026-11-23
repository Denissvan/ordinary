param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$mtPath = Join-Path $RepoRoot "UI\\Class\\MT.cs"
$upDownLoadPath = Join-Path $RepoRoot "UI\\Class\\UpDownLoad.cs"
$mt = Get-Content -LiteralPath $mtPath -Raw -Encoding UTF8
$upDownLoad = Get-Content -LiteralPath $upDownLoadPath -Raw -Encoding UTF8

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$beginGate = $mt.IndexOf("BeginDualArmRecoveryGate")
$enterGate = $mt.IndexOf("EnterDualArmRecoveryGate")
$releaseGate = $mt.IndexOf("ReleaseDualArmRecoveryGate")
$moveGate = $mt.IndexOf("WaitForDualArmRecoveryGate")

Assert-Condition ($beginGate -ge 0) "MT must close a dual-arm recovery gate before a high-risk warning is shown."
Assert-Condition ($enterGate -gt $beginGate) "MT must grant the recovery thread a controlled gate entry."
Assert-Condition ($releaseGate -gt $enterGate) "MT must release the recovery gate after the safe recovery sequence."
Assert-Condition ($moveGate -gt $releaseGate) "MT.Move must wait for the dual-arm recovery gate before issuing UDL motion."

$warningWrapper = $upDownLoad.IndexOf("DisplayWarningWithDualArmRecoveryGate")
$waitForIdle = $upDownLoad.IndexOf("EnterDualArmRecoveryGate", $warningWrapper)
$zUp = $upDownLoad.IndexOf("ud.Zup(ref recoveryQuit);", $warningWrapper)
$goZero = $upDownLoad.IndexOf("ud.GoZero(ref recoveryQuit, false);", $warningWrapper)
$highRiskBackCheck = $upDownLoad.IndexOf('"上相机回检", true, out recoveryRes', $warningWrapper)
Assert-Condition ($highRiskBackCheck -ge 0) "Upper-camera back-check warning must be routed through DisplayWarningWithDualArmRecoveryGate with keepPositionOnRetry."
$highRiskPick = $upDownLoad.IndexOf('"工站吸料异常", true, out recoveryRes', $warningWrapper)
Assert-Condition ($highRiskPick -ge 0) "Station pick warning must be routed through DisplayWarningWithDualArmRecoveryGate with keepPositionOnRetry."

Assert-Condition ($warningWrapper -ge 0) "UpDownLoad must centralize high-risk warning recovery."
Assert-Condition ($waitForIdle -gt $warningWrapper) "The warning recovery must enter the gate before moving either arm."
Assert-Condition ($zUp -gt $waitForIdle) "The warning recovery must raise both Z axes after motion becomes idle."
Assert-Condition ($goZero -gt $zUp) "The warning recovery must return both arms to the safe position after Z-up."
Assert-Condition ($highRiskBackCheck -gt $warningWrapper) "Upper-camera back-check warnings must use the recovery gate."
Assert-Condition ($highRiskPick -gt $highRiskBackCheck) "Station pick warnings must use the recovery gate."

Write-Host "PASS: dual-arm recovery gate protects warning recovery before automatic UDL motion resumes."
