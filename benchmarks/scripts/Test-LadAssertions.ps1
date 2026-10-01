[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'LadAssertions.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaLadTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

. (Join-Path $PSScriptRoot 'LadTestFixtures.ps1')
function Assert-TestLad {
    param([string]$Label, [string]$Xml, [object]$Assertion, [bool]$ExpectedPass, [switch]$Timer)
    $path = Join-Path $testRoot 'network.xml'
    [IO.File]::WriteAllText($path, $Xml, [Text.UTF8Encoding]::new($true))
    $failure = $null
    try {
        if ($Timer) { $null = Test-BenchmarkLadTimerNetwork $testRoot $Assertion }
        else { $null = Test-BenchmarkLadBooleanNetwork $testRoot $Assertion }
    }
    catch { $failure = $_.Exception.Message }
    if (($null -eq $failure) -ne $ExpectedPass) { throw "Unexpected result for '$Label': $failure" }
}
try {
    $boolean = [pscustomobject]@{ relativePath='network.xml'; networkIndex=1; inputNames=@('AutoStart','JogRequest','Permit','GuardClosed','Fault','Reset'); expectedTrueRows=@(13,14,15); outputName='RunRequest' }
    $timer = [pscustomobject]@{ relativePath='network.xml'; networkIndex=1; inputName='RunRequest'; outputName='DelayedRun'; timerInstance='RunDelay'; presetMilliseconds=750 }
    foreach ($variant in @('', 'renamed-ids', 'commuted-branches', 'commuted-guards')) { Assert-TestLad "boolean-$variant" (New-TestBooleanXml $variant) $boolean $true }
    $booleanNegatives = @('jog-bypasses-interlocks','fault-inverted','reset-inverted','permit-inverted','guard-inverted','wrong-output','unknown-input','latched-output','duplicate-id','missing-wire-id','duplicate-connector','duplicate-container','duplicate-pin-wire','missing-operand','unsupported-pin','unsupported-part','wrong-cardinality','extra-part','extra-wire','wrong-language','wrong-namespace','dtd','cycle')
    foreach ($variant in $booleanNegatives) { Assert-TestLad $variant (New-TestBooleanXml $variant) $boolean $false }
    foreach ($variant in @('', 'equivalent-preset')) { Assert-TestLad "timer-$variant" (New-TestTimerXml $variant) $timer $true -Timer }
    $timerNegatives = @('wrong-preset','wrong-input','wrong-output','wrong-instance','wrong-static-type','temporary-state','retained-state','retentive-timer','q-bypass','wrong-template','bound-et','inverted-contact')
    foreach ($variant in $timerNegatives) { Assert-TestLad $variant (New-TestTimerXml $variant) $timer $false -Timer }
    foreach ($badPath in @('../network.xml', '/network.xml')) {
        $rejected = $false
        try { $null = Resolve-BenchmarkWorkspaceArtifact $testRoot $badPath } catch { $rejected = $true }
        if (-not $rejected) { throw 'Unsafe workspace path accepted.' }
    }
    Write-Host 'LAD XML oracle passed (6 equivalent positives, 35 circuit/XML negatives, 2 unsafe paths).'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaLadTests-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
