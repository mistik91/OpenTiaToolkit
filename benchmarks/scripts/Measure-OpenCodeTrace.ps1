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

function Test-ValueSubset {
    param(
        [AllowNull()][object]$Actual,
        [AllowNull()][object]$Expected
    )

    if ($null -eq $Expected) {
        return $null -eq $Actual
    }

    if ($Expected -is [System.Management.Automation.PSCustomObject] -or
        $Expected -is [System.Collections.IDictionary]) {
        if ($null -eq $Actual) {
            return $false
        }
        foreach ($property in $Expected.PSObject.Properties) {
            # Read the property value directly here. Returning it through a helper
            # lets Windows PowerShell enumerate a one-element nested array into a
            # scalar, which makes otherwise identical JSON arrays compare unequal.
            $actualProperty = $Actual.PSObject.Properties[$property.Name]
            if ($null -eq $actualProperty -or
                -not (Test-ValueSubset -Actual $actualProperty.Value -Expected $property.Value)) {
                return $false
            }
        }
        return $true
    }

    if ($Expected -is [System.Collections.IEnumerable] -and $Expected -isnot [string]) {
        if ($null -eq $Actual -or $Actual -is [string]) {
            return $false
        }
        $expectedItems = @($Expected)
        $actualItems = @($Actual)
        if ($expectedItems.Count -ne $actualItems.Count) {
            return $false
        }
        for ($index = 0; $index -lt $expectedItems.Count; $index++) {
            if (-not (Test-ValueSubset -Actual $actualItems[$index] -Expected $expectedItems[$index])) {
                return $false
            }
        }
        return $true
    }

    return (($Actual | ConvertTo-Json -Depth 100 -Compress) -ceq
        ($Expected | ConvertTo-Json -Depth 100 -Compress))
}

function Get-PropertyPathValue {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Path,
        [ref]$Found
    )

    $Found.Value = $false
    $current = $Object
    foreach ($segment in $Path.Split('.')) {
        $segmentFound = $false
        $current = Get-PropertyValue -Object $current -Name $segment -Found ([ref]$segmentFound)
        if (-not $segmentFound) {
            return $null
        }
    }
    $Found.Value = $true
    return $current
}

function Test-OperationSet {
    param(
        [AllowNull()][object]$ActualOperations,
        [AllowNull()][object]$ExpectedOperations,
        [bool]$NormalizeResultReferences = $false
    )

    $nestedOperationsFound = $false
    $nestedOperations = Get-PropertyValue `
        -Object $ActualOperations `
        -Name 'operations' `
        -Found ([ref]$nestedOperationsFound)
    if ($nestedOperationsFound) {
        $ActualOperations = $nestedOperations
    }

    $actual = @($ActualOperations)
    $expected = @($ExpectedOperations)
    if ($actual.Count -ne $expected.Count) {
        return $false
    }

    if ($NormalizeResultReferences) {
        return Test-OperationSetWithNormalizedResultReferences `
            -ActualOperations $actual `
            -ExpectedOperations $expected `
            -ExpectedIndex 0 `
            -Used ([bool[]]::new($actual.Count)) `
            -ExpectedToActual @{} `
            -ActualToExpected @{}
    }

    $used = [bool[]]::new($actual.Count)
    foreach ($expectedOperation in $expected) {
        $matched = $false
        for ($index = 0; $index -lt $actual.Count; $index++) {
            if (-not $used[$index] -and
                (Test-ValueSubset -Actual $actual[$index] -Expected $expectedOperation)) {
                $used[$index] = $true
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            return $false
        }
    }
    return $true
}

function Test-ValueSubsetWithNormalizedResultReferences {
    param(
        [AllowNull()][object]$Actual,
        [AllowNull()][object]$Expected,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedToActual,
        [Parameter(Mandatory = $true)][hashtable]$ActualToExpected
    )

    if ($null -eq $Expected) {
        return $null -eq $Actual
    }

    if ($Expected -is [System.Management.Automation.PSCustomObject] -or
        $Expected -is [System.Collections.IDictionary]) {
        if ($null -eq $Actual) {
            return $false
        }
        foreach ($property in $Expected.PSObject.Properties) {
            $actualProperty = $Actual.PSObject.Properties[$property.Name]
            if ($null -eq $actualProperty) {
                return $false
            }

            if ($property.Name -in @('resultId', 'targetRef') -and
                $property.Value -is [string]) {
                if ($actualProperty.Value -isnot [string]) {
                    return $false
                }

                $expectedReference = [string]$property.Value
                $actualReference = [string]$actualProperty.Value
                if ($ExpectedToActual.ContainsKey($expectedReference)) {
                    if ([string]$ExpectedToActual[$expectedReference] -cne $actualReference) {
                        return $false
                    }
                }
                elseif ($ActualToExpected.ContainsKey($actualReference)) {
                    return $false
                }
                else {
                    $ExpectedToActual[$expectedReference] = $actualReference
                    $ActualToExpected[$actualReference] = $expectedReference
                }
            }
            elseif (-not (Test-ValueSubsetWithNormalizedResultReferences `
                        -Actual $actualProperty.Value `
                        -Expected $property.Value `
                        -ExpectedToActual $ExpectedToActual `
                        -ActualToExpected $ActualToExpected)) {
                return $false
            }
        }
        return $true
    }

    if ($Expected -is [System.Collections.IEnumerable] -and $Expected -isnot [string]) {
        if ($null -eq $Actual -or $Actual -is [string]) {
            return $false
        }
        $expectedItems = @($Expected)
        $actualItems = @($Actual)
        if ($expectedItems.Count -ne $actualItems.Count) {
            return $false
        }
        for ($index = 0; $index -lt $expectedItems.Count; $index++) {
            if (-not (Test-ValueSubsetWithNormalizedResultReferences `
                        -Actual $actualItems[$index] `
                        -Expected $expectedItems[$index] `
                        -ExpectedToActual $ExpectedToActual `
                        -ActualToExpected $ActualToExpected)) {
                return $false
            }
        }
        return $true
    }

    return (($Actual | ConvertTo-Json -Depth 100 -Compress) -ceq
        ($Expected | ConvertTo-Json -Depth 100 -Compress))
}

function Test-OperationSetWithNormalizedResultReferences {
    param(
        [Parameter(Mandatory = $true)][object[]]$ActualOperations,
        [Parameter(Mandatory = $true)][object[]]$ExpectedOperations,
        [Parameter(Mandatory = $true)][int]$ExpectedIndex,
        [Parameter(Mandatory = $true)][bool[]]$Used,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedToActual,
        [Parameter(Mandatory = $true)][hashtable]$ActualToExpected
    )

    if ($ExpectedIndex -ge $ExpectedOperations.Count) {
        return $true
    }

    for ($actualIndex = 0; $actualIndex -lt $ActualOperations.Count; $actualIndex++) {
        if ($Used[$actualIndex]) {
            continue
        }

        $nextUsed = [bool[]]$Used.Clone()
        $nextExpectedToActual = @{}
        foreach ($entry in $ExpectedToActual.GetEnumerator()) {
            $nextExpectedToActual[$entry.Key] = $entry.Value
        }
        $nextActualToExpected = @{}
        foreach ($entry in $ActualToExpected.GetEnumerator()) {
            $nextActualToExpected[$entry.Key] = $entry.Value
        }

        if (-not (Test-ValueSubsetWithNormalizedResultReferences `
                    -Actual $ActualOperations[$actualIndex] `
                    -Expected $ExpectedOperations[$ExpectedIndex] `
                    -ExpectedToActual $nextExpectedToActual `
                    -ActualToExpected $nextActualToExpected)) {
            continue
        }

        $nextUsed[$actualIndex] = $true
        if (Test-OperationSetWithNormalizedResultReferences `
                -ActualOperations $ActualOperations `
                -ExpectedOperations $ExpectedOperations `
                -ExpectedIndex ($ExpectedIndex + 1) `
                -Used $nextUsed `
                -ExpectedToActual $nextExpectedToActual `
                -ActualToExpected $nextActualToExpected) {
            return $true
        }
    }

    return $false
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
        'toolOperationSet' {
            $namePattern = [string]$assertion.namePattern
            $operationPath = [string]$assertion.operationPath
            $normalizeResultReferences = $null -ne $assertion.PSObject.Properties['normalizeResultReferences'] -and
                [bool]$assertion.normalizeResultReferences
            $matchingTools = @($toolEvents | Where-Object {
                    if ((([string]$_.part.tool -replace '-', '_') -notmatch $namePattern) -or
                        -not (Test-InputEquals -InputObject $_.part.state.input -Expected $expectedInput)) {
                        return $false
                    }
                    $foundOperations = $false
                    $actualOperations = Get-PropertyPathValue `
                        -Object $_.part.state.input `
                        -Path $operationPath `
                        -Found ([ref]$foundOperations)
                    return $foundOperations -and
                        (Test-OperationSet `
                            -ActualOperations $actualOperations `
                            -ExpectedOperations $assertion.expectedOperations `
                            -NormalizeResultReferences $normalizeResultReferences)
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
            $passed = $matchingTools.Count -ge $minimum -and $matchingTools.Count -le $maximum
            $details = "matchedOperationSets=$($matchingTools.Count); expected=$minimum..$maximum; path=$operationPath"
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
    schemaVersion   = 2
    caseId          = [string]$case.id
    model           = [string]$metadata.model
    trialIndex      = if ($null -ne $metadata.PSObject.Properties['trialIndex']) {
        [int]$metadata.trialIndex
    }
    else {
        1
    }
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
