param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$exe = Join-Path $env:TEMP ('FeedDistancePose-' + [Guid]::NewGuid().ToString('N') + '.exe')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try {
    & $csc /nologo /target:exe "/out:$exe" (Join-Path $RepoRoot 'UI\Class\FeedDistanceInspectionState.cs') (Join-Path $RepoRoot 'UI\Class\FeedDistanceInspection.cs') (Join-Path $PSScriptRoot 'FeedDistancePoseRegression.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Pose harness compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Pose regression failed.' }
} finally { if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe } }
