[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CasePath,

    [Parameter(Mandatory = $true)]
    [string]$TracePath,

    [Parameter(Mandatory = $true)]
    [string]$MetadataPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-PropertyValue {
    param(
        [AllowNull()]
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [ref]$Found
    )

    $Found.Value = $false
    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    $Found.Value = $true
    return $property.Value
}

function Test-InputEquals {
    param(
        [AllowNull()]
        [object]$InputObject,
        [AllowNull()]
        [object]$Expected
    )

    if ($null -eq $Expected) {
        return $true
    }

    foreach ($property in $Expected.PSObject.Properties) {
        $found = $false
        $actual = Get-PropertyValue -Object $InputObject -Name $property.Name -Found ([ref]$found)
        if (-not $found) {
            return $false
        }

        $expectedJson = $property.Value | ConvertTo-Json -Depth 20 -Compress
        $actualJson = $actual | ConvertTo-Json -Depth 20 -Compress
        if ($actualJson -cne $expectedJson) {
            return $false
        }
    }

    return $true
}

function Convert-ValueToText {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) {
        return ''
    }
    if ($Value -is [string]) {
        return $Value
    }
    return ($Value | ConvertTo-Json -Depth 100 -Compress)
}

$case = Get-Content -LiteralPath $CasePath -Raw | ConvertFrom-Json
$metadata = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
$rawTrace = if (Test-Path -LiteralPath $TracePath) {
    Get-Content -LiteralPath $TracePath -Raw
}
else {
    ''
}

$events = @()
if (-not [string]::IsNullOrWhiteSpace($rawTrace)) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($TracePath)) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        try {
            $events += ($line | ConvertFrom-Json)
        }
        catch {
            throw "Invalid OpenCode JSON event at ${TracePath}:$lineNumber. $($_.Exception.Message)"
        }
    }
}

$toolEvents = @($events | Where-Object { $_.type -eq 'tool_use' -and $null -ne $_.part })
$finalText = (($events |
            Where-Object { $_.type -eq 'text' -and $null -ne $_.part.text } |
            ForEach-Object { [string]$_.part.text }) -join "`n")
$decodedTraceSegments = [System.Collections.Generic.List[string]]::new()
foreach ($event in $events) {
    if ($event.type -eq 'text' -and $null -ne $event.part.text) {
        $decodedTraceSegments.Add([string]$event.part.text)
    }
    elseif ($event.type -eq 'tool_use' -and $null -ne $event.part) {
        $decodedTraceSegments.Add([string]$event.part.tool)
        $decodedTraceSegments.Add((Convert-ValueToText -Value $event.part.state.input))
        $foundToolOutput = $false
        $toolOutput = Get-PropertyValue -Object $event.part.state -Name 'output' -Found ([ref]$foundToolOutput)
        if (-not $foundToolOutput) {
            $foundToolError = $false
            $toolOutput = Get-PropertyValue -Object $event.part.state -Name 'error' -Found ([ref]$foundToolError)
        }
        $decodedTraceSegments.Add((Convert-ValueToText -Value $toolOutput))
    }
}
$decodedTrace = $decodedTraceSegments -join "`n"

$assertionResults = @()
$earnedPoints = 0.0
$totalPoints = 0.0
$criticalFailure = $false

foreach ($assertion in $case.assertions) {
    $points = [double]$assertion.points
    $totalPoints += $points
    $passed = $false
    $details = ''
    $isCritical = if ($null -ne $assertion.PSObject.Properties['critical']) {
        [bool]$assertion.critical
    }
    else {
        $false
    }
    $expectedInput = if ($null -ne $assertion.PSObject.Properties['inputEquals']) {
        $assertion.inputEquals
    }
    else {
        $null
    }

    switch ([string]$assertion.type) {
        'run' {
            $hasStop = @($events | Where-Object {
                    $_.type -eq 'step_finish' -and $_.part.reason -eq 'stop'
                }).Count -gt 0
            $passed = (-not [bool]$metadata.timedOut) -and ([int]$metadata.exitCode -eq 0) -and $hasStop
            $details = "exitCode=$($metadata.exitCode); timedOut=$($metadata.timedOut); finalStop=$hasStop"
        }
        'tool' {
            $namePattern = [string]$assertion.namePattern
            $matchingTools = @($toolEvents | Where-Object {
                    (([string]$_.part.tool -replace '-', '_') -match $namePattern) -and
                    (Test-InputEquals -InputObject $_.part.state.input -Expected $expectedInput)
                })
            $minimum = if ($null -ne $assertion.PSObject.Properties['minCount']) {
                [int]$assertion.minCount
            }
            else {
                1
            }
            $maximum = if ($null -ne $assertion.PSObject.Properties['maxCount']) {
                [int]$assertion.maxCount
            }
            else {
                [int]::MaxValue
            }

            $patternsPassed = $true
            $combinedOutput = (($matchingTools | ForEach-Object {
                        $foundOutput = $false
                        $stateOutput = Get-PropertyValue -Object $_.part.state -Name 'output' -Found ([ref]$foundOutput)
                        if (-not $foundOutput) {
                            $foundError = $false
                            $stateOutput = Get-PropertyValue -Object $_.part.state -Name 'error' -Found ([ref]$foundError)
                        }
                        Convert-ValueToText -Value $stateOutput
                    }) -join "`n")
            if ($null -ne $assertion.PSObject.Properties['outputPatterns']) {
                foreach ($pattern in $assertion.outputPatterns) {
                    if ($combinedOutput -notmatch [string]$pattern) {
                        $patternsPassed = $false
                        break
                    }
                }
            }

            $passed = $matchingTools.Count -ge $minimum -and
                $matchingTools.Count -le $maximum -and
                $patternsPassed
            $details = "matched=$($matchingTools.Count); expected=$minimum..$maximum; outputsMatched=$patternsPassed"
        }
        'forbiddenTool' {
            $namePattern = [string]$assertion.namePattern
            $matchingTools = @($toolEvents | Where-Object {
                    (([string]$_.part.tool -replace '-', '_') -match $namePattern) -and
                    (Test-InputEquals -InputObject $_.part.state.input -Expected $expectedInput)
                })
            $passed = $matchingTools.Count -eq 0
            $details = "forbiddenMatches=$($matchingTools.Count)"
        }
        'finalRegex' {
            $passed = $finalText -match [string]$assertion.pattern
            $details = "finalTextLength=$($finalText.Length)"
        }
        'traceRegex' {
            $passed = $decodedTrace -match [string]$assertion.pattern
            $details = "decodedTraceLength=$($decodedTrace.Length)"
        }
        default {
            throw "Unsupported assertion type '$($assertion.type)' in '$CasePath'."
        }
    }

    if ($passed) {
        $earnedPoints += $points
    }
    elseif ($isCritical) {
        $criticalFailure = $true
    }

    $assertionResults += [ordered]@{
        id       = [string]$assertion.id
        type     = [string]$assertion.type
        points   = $points
        critical = $isCritical
        passed   = $passed
        details  = $details
    }
}

$score = if ($totalPoints -gt 0) {
    [Math]::Round(($earnedPoints / $totalPoints) * 100.0, 2)
}
else {
    0.0
}

$stepFinishes = @($events | Where-Object { $_.type -eq 'step_finish' -and $null -ne $_.part.tokens })
$totalTokens = 0L
$inputTokens = 0L
$outputTokens = 0L
$reasoningTokens = 0L
$totalCost = 0.0
foreach ($step in $stepFinishes) {
    $totalTokens += [long]$step.part.tokens.total
    $inputTokens += [long]$step.part.tokens.input
    $outputTokens += [long]$step.part.tokens.output
    $reasoningTokens += [long]$step.part.tokens.reasoning
    $totalCost += [double]$step.part.cost
}
$usage = [ordered]@{
    tokens = [ordered]@{
        total     = $totalTokens
        input     = $inputTokens
        output    = $outputTokens
        reasoning = $reasoningTokens
    }
    cost   = $totalCost
}

$result = [ordered]@{
    schemaVersion   = 1
    caseId          = [string]$case.id
    model           = [string]$metadata.model
    reasoningEffort = if ($null -ne $metadata.PSObject.Properties['reasoningEffort']) {
        [string]$metadata.reasoningEffort
    }
    elseif ([string]::IsNullOrWhiteSpace([string]$metadata.variant)) {
        'provider-default'
    }
    else {
        [string]$metadata.variant
    }
    variant         = $metadata.variant
    authProvider    = if ($null -ne $metadata.PSObject.Properties['authProvider']) {
        $metadata.authProvider
    }
    else {
        $null
    }
    authSource      = if ($null -ne $metadata.PSObject.Properties['authSource']) {
        [string]$metadata.authSource
    }
    else {
        'isolated'
    }
    openCodeVersion = [string]$metadata.openCodeVersion
    gitCommit       = [string]$metadata.gitCommit
    gitBranch       = [string]$metadata.gitBranch
    gitDirty        = if ($null -ne $metadata.PSObject.Properties['gitDirty']) {
        [bool]$metadata.gitDirty
    }
    else {
        $false
    }
    gitStateSha256  = if ($null -ne $metadata.PSObject.Properties['gitStateSha256']) {
        [string]$metadata.gitStateSha256
    }
    else {
        '0000000000000000000000000000000000000000000000000000000000000000'
    }
    fixtureId       = [string]$case.fixtureId
    fixtureSha256   = if ($null -ne $metadata.PSObject.Properties['fixtureSha256']) {
        $metadata.fixtureSha256
    }
    else {
        $null
    }
    startedAtUtc    = [string]$metadata.startedAtUtc
    finishedAtUtc   = [string]$metadata.finishedAtUtc
    durationSeconds = [double]$metadata.durationSeconds
    exitCode        = [int]$metadata.exitCode
    timedOut        = [bool]$metadata.timedOut
    score           = $score
    passed          = (-not $criticalFailure) -and ($score -ge [double]$case.passScore)
    passScore       = [double]$case.passScore
    assertions      = $assertionResults
    usage           = $usage
}

$result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result
