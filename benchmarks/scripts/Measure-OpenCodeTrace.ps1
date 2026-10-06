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

. (Join-Path $PSScriptRoot 'LadAssertions.ps1')
. (Join-Path $PSScriptRoot 'PlcBehaviorAssertions.ps1')
. (Join-Path $PSScriptRoot 'BlueprintAssertions.ps1')
. (Join-Path $PSScriptRoot 'MultiuserCaseAssertions.ps1')
. (Join-Path $PSScriptRoot 'OpenCodeToolOutputs.ps1')

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

function Test-HardwareHexTextEquals {
    param(
        [AllowNull()][object]$Actual,
        [AllowNull()][object]$Expected
    )

    if ($Actual -isnot [string] -or $Expected -isnot [string]) {
        return $false
    }

    $actualCompact = ([string]$Actual) -replace '[\s\-,]', ''
    $expectedCompact = ([string]$Expected) -replace '[\s\-,]', ''
    if ($actualCompact.StartsWith('0x', [System.StringComparison]::OrdinalIgnoreCase)) {
        $actualCompact = $actualCompact.Substring(2)
    }
    if ($expectedCompact.StartsWith('0x', [System.StringComparison]::OrdinalIgnoreCase)) {
        $expectedCompact = $expectedCompact.Substring(2)
    }

    if ($actualCompact.Length -eq 0 -or
        $actualCompact.Length % 2 -ne 0 -or
        $expectedCompact.Length -eq 0 -or
        $expectedCompact.Length % 2 -ne 0 -or
        $actualCompact -notmatch '^[0-9A-Fa-f]+$' -or
        $expectedCompact -notmatch '^[0-9A-Fa-f]+$') {
        return $false
    }

    return $actualCompact.Equals($expectedCompact, [System.StringComparison]::OrdinalIgnoreCase)
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
            if ($null -eq $actualProperty) {
                return $false
            }
            if ($property.Name -in @('dataHex', 'expectedDataHex')) {
                if (-not (Test-HardwareHexTextEquals `
                            -Actual $actualProperty.Value `
                            -Expected $property.Value)) {
                    return $false
                }
                continue
            }
            if (-not (Test-ValueSubset -Actual $actualProperty.Value -Expected $property.Value)) {
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

            if ($property.Name -in @('dataHex', 'expectedDataHex')) {
                if (-not (Test-HardwareHexTextEquals `
                            -Actual $actualProperty.Value `
                            -Expected $property.Value)) {
                    return $false
                }
                continue
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

function ConvertFrom-ToolStatePayload {
    param([AllowNull()][object]$State)

    if ($null -eq $State) {
        return $null
    }

    $found = $false
    $payload = Get-PropertyValue -Object $State -Name 'output' -Found ([ref]$found)
    if (-not $found) {
        $payload = Get-PropertyValue -Object $State -Name 'error' -Found ([ref]$found)
    }
    if (-not $found -or $null -eq $payload) {
        return $null
    }
    if ($payload -isnot [string]) {
        return $payload
    }

    try {
        return ([string]$payload | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Get-HardwarePatchOperations {
    param([AllowNull()][object]$InputObject)

    $foundOperations = $false
    $operations = Get-PropertyPathValue `
        -Object $InputObject `
        -Path 'operations.operations' `
        -Found ([ref]$foundOperations)
    if (-not $foundOperations) {
        $operations = Get-PropertyPathValue `
            -Object $InputObject `
            -Path 'operations' `
            -Found ([ref]$foundOperations)
    }
    if (-not $foundOperations -or $null -eq $operations) {
        return @()
    }
    return @($operations)
}

function Get-ValidatorInterventions {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$ToolEvents)

    $patchEvents = @($ToolEvents | Where-Object {
            (([string]$_.part.tool -replace '-', '_') -match '(^|_)apply_hardware_patch$')
        })
    $calls = [System.Collections.Generic.List[object]]::new()
    $allIssues = [System.Collections.Generic.List[object]]::new()

    for ($patchIndex = 0; $patchIndex -lt $patchEvents.Count; $patchIndex++) {
        $event = $patchEvents[$patchIndex]
        $payload = ConvertFrom-ToolStatePayload -State $event.part.state
        $dataFound = $false
        $data = Get-PropertyValue -Object $payload -Name 'data' -Found ([ref]$dataFound)
        $validationBlockedFound = $false
        $validationBlocked = Get-PropertyValue `
            -Object $data `
            -Name 'ValidationBlocked' `
            -Found ([ref]$validationBlockedFound)
        if (-not $dataFound -or $null -eq $data -or -not $validationBlockedFound -or
            -not [bool]$validationBlocked) {
            continue
        }

        $issuesFound = $false
        $issuesValue = Get-PropertyValue -Object $data -Name 'ValidationIssues' -Found ([ref]$issuesFound)
        $issues = @()
        if ($issuesFound) {
            $issues = @($issuesValue)
        }
        foreach ($issue in $issues) {
            $allIssues.Add($issue)
        }

        $nextPayload = $null
        $nextIssues = @()
        $nextValidationBlocked = $null
        if ($patchIndex + 1 -lt $patchEvents.Count) {
            $nextPayload = ConvertFrom-ToolStatePayload -State $patchEvents[$patchIndex + 1].part.state
            $nextDataFound = $false
            $nextData = Get-PropertyValue -Object $nextPayload -Name 'data' -Found ([ref]$nextDataFound)
            if ($nextDataFound -and $null -ne $nextData) {
                $nextBlockedFound = $false
                $nextBlocked = Get-PropertyValue `
                    -Object $nextData `
                    -Name 'ValidationBlocked' `
                    -Found ([ref]$nextBlockedFound)
                if ($nextBlockedFound) {
                    $nextValidationBlocked = [bool]$nextBlocked
                }
                $nextIssuesFound = $false
                $nextIssuesValue = Get-PropertyValue `
                    -Object $nextData `
                    -Name 'ValidationIssues' `
                    -Found ([ref]$nextIssuesFound)
                if ($nextIssuesFound) {
                    $nextIssues = @($nextIssuesValue)
                }
            }
        }

        $issueRecords = @($issues | ForEach-Object {
                $indexProperty = $_.PSObject.Properties['OperationIndex']
                if ($null -eq $indexProperty) {
                    $indexProperty = $_.PSObject.Properties['Index']
                }
                [ordered]@{
                    operationIndex = if ($null -ne $indexProperty) { [int]$indexProperty.Value } else { -1 }
                    code           = [string]$_.Code
                    field          = if ($null -ne $_.PSObject.Properties['Field']) {
                        [string]$_.Field
                    }
                    else {
                        $null
                    }
                }
            })
        $remainingKeys = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal)
        foreach ($nextIssue in $nextIssues) {
            $nextIndexProperty = $nextIssue.PSObject.Properties['OperationIndex']
            if ($null -eq $nextIndexProperty) {
                $nextIndexProperty = $nextIssue.PSObject.Properties['Index']
            }
            $nextIndex = if ($null -ne $nextIndexProperty) { [int]$nextIndexProperty.Value } else { -1 }
            [void]$remainingKeys.Add("${nextIndex}:$([string]$nextIssue.Code)")
        }
        $correctedOnNextPatch = if ($patchIndex + 1 -ge $patchEvents.Count) {
            $null
        }
        else {
            @($issueRecords | Where-Object {
                    $remainingKeys.Contains("$($_.operationIndex):$($_.code)")
                }).Count -eq 0
        }
        $operationResultsFound = $false
        $operationResultsValue = Get-PropertyValue `
            -Object $data `
            -Name 'Operations' `
            -Found ([ref]$operationResultsFound)
        $operationResults = @()
        if ($operationResultsFound) {
            $operationResults = @($operationResultsValue)
        }
        $rolledBackFound = $false
        $rolledBack = Get-PropertyValue -Object $data -Name 'RolledBack' -Found ([ref]$rolledBackFound)
        $rejectedBeforeTia = $rolledBackFound -and -not [bool]$rolledBack -and
            $operationResults.Count -eq 0

        $calls.Add([pscustomobject][ordered]@{
                patchCallIndex              = $patchIndex + 1
                operationCount              = @(Get-HardwarePatchOperations -InputObject $event.part.state.input).Count
                issueCount                  = $issues.Count
                issues                      = $issueRecords
                rejectedBeforeTia           = $rejectedBeforeTia
                correctedOnNextPatch        = $correctedOnNextPatch
                nextPatchValidationBlocked  = $nextValidationBlocked
            })
    }

    $codeCounts = @($allIssues |
            Group-Object -Property { [string]$_.Code } |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{ code = [string]$_.Name; count = [int]$_.Count }
            })
    return [pscustomobject][ordered]@{
        blockedCallCount           = $calls.Count
        rejectedBeforeTiaCount     = @($calls | Where-Object { [bool]$_.rejectedBeforeTia }).Count
        correctedOnNextPatchCount  = @($calls | Where-Object { $_.correctedOnNextPatch -eq $true }).Count
        issueCount                 = $allIssues.Count
        codes                      = $codeCounts
        calls                      = @($calls)
    }
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
$toolOutputCaptureFailure = $null
try { Restore-BenchmarkToolOutputs -Events $events -Metadata $metadata -ArtifactDirectory (Split-Path -Parent $MetadataPath) }
catch { $toolOutputCaptureFailure = $_.Exception.Message }
$skillCalls = @($toolEvents | Where-Object {
        ([string]$_.part.tool -replace '-', '_') -ceq 'skill'
    } | ForEach-Object {
        $state = $_.part.state
        $name = if ($null -ne $state -and $null -ne $state.input -and
            $null -ne $state.input.PSObject.Properties['name']) {
            [string]$state.input.name
        }
        else {
            ''
        }
        [pscustomobject][ordered]@{
            name   = $name
            status = if ($null -ne $state -and
                $null -ne $state.PSObject.Properties['status']) {
                [string]$state.status
            }
            else {
                'unknown'
            }
        }
    })
$loadedSkills = @($skillCalls | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_.name) -and
        [string]$_.status -ceq 'completed'
    } | ForEach-Object { [string]$_.name } | Sort-Object -Unique)
$skillUsage = [pscustomobject][ordered]@{
    callCount    = $skillCalls.Count
    loadedSkills = $loadedSkills
    calls        = $skillCalls
}
$hasStop = @($events | Where-Object {
        $_.type -eq 'step_finish' -and $_.part.reason -eq 'stop'
    }).Count -gt 0
$terminalError = @($events | Where-Object { $_.type -eq 'error' } | Select-Object -Last 1)
$workspacePolicyFailure = $null
$workspaceRoot = $null
$workspaceProperty = $metadata.PSObject.Properties['workspacePath']
if ($null -ne $workspaceProperty -and
    -not [string]::IsNullOrWhiteSpace([string]$workspaceProperty.Value)) {
    $workspaceRoot = [System.IO.Path]::GetFullPath(
        [string]$workspaceProperty.Value
    ).TrimEnd([char[]]'\/')
    $workspacePrefix = $workspaceRoot + [System.IO.Path]::DirectorySeparatorChar
    foreach ($toolEvent in $toolEvents) {
        $state = $toolEvent.part.state
        if ($null -eq $state -or [string]$state.status -cne 'error' -or
            $null -eq $state.PSObject.Properties['error']) {
            continue
        }
        $rawError = [string]$state.error
        $message = $rawError
        try {
            $errorPayload = $rawError | ConvertFrom-Json
            if ($null -ne $errorPayload.PSObject.Properties['message']) {
                $message = [string]$errorPayload.message
            }
        }
        catch {
            # Preserve the raw tool error when it is not JSON.
        }
        $deniedPathMatch = [regex]::Match(
            $message,
            "(?i)MCP file policy rejected [^']+ '(?<path>[^']+)'"
        )
        if (-not $deniedPathMatch.Success) {
            continue
        }
        try {
            $deniedPath = [System.IO.Path]::GetFullPath(
                [string]$deniedPathMatch.Groups['path'].Value
            )
        }
        catch {
            continue
        }
        if ([string]::Equals(
                $deniedPath,
                $workspaceRoot,
                [System.StringComparison]::OrdinalIgnoreCase
            ) -or
            $deniedPath.StartsWith(
                $workspacePrefix,
                [System.StringComparison]::OrdinalIgnoreCase
            )) {
            $workspacePolicyFailure = [pscustomobject][ordered]@{
                tool    = [string]$toolEvent.part.tool
                path    = $deniedPath
                message = $message
            }
            break
        }
    }
}
$executionFailure = $null
if ($null -ne $toolOutputCaptureFailure) {
    $executionFailure = [pscustomobject][ordered]@{
        kind = 'harness-error'; statusCode = $null
        message = "Full native OpenCode tool output could not be verified: $toolOutputCaptureFailure"
    }
}
elseif ($null -ne $workspacePolicyFailure) {
    $executionFailure = [pscustomobject][ordered]@{
        kind       = 'harness-error'
        statusCode = $null
        message    = "Benchmark MCP rejected its own workspace path '$($workspacePolicyFailure.path)' during '$($workspacePolicyFailure.tool)'."
    }
}
elseif (-not $hasStop -and $terminalError.Count -gt 0) {
    $errorData = $terminalError[0].error.data
    $message = if ($null -ne $errorData -and
        $null -ne $errorData.PSObject.Properties['message']) {
        ([string]$errorData.message).Trim('"')
    }
    else {
        [string]$terminalError[0].error.name
    }
    $statusCode = $null
    if ($message -match '\[(?<status>[45][0-9]{2})\]') {
        $statusCode = [int]$Matches.status
    }
    elseif ($null -ne $errorData) {
        foreach ($statusPropertyName in @('statusCode', 'status')) {
            $statusProperty = $errorData.PSObject.Properties[$statusPropertyName]
            if ($null -ne $statusProperty -and
                [string]$statusProperty.Value -match '^[45][0-9]{2}$') {
                $statusCode = [int]$statusProperty.Value
                break
            }
        }
    }
    $isRateLimit = $statusCode -eq 429 -or
        $message -match '(?i)rate[ -]?limit|too many requests|usage limit|limit exhausted|temporarily overloaded|high (?:concurrency|frequency)|fair use|quota|insufficient balance|no resource package' -or
        $message -match '(?<!\d)(?:1113|1302|1305|1308|1309|1310|1311|1313|1314|1315|1316|1317|1318|1319|1320|1321)(?!\d)'
    $isProviderError = $isRateLimit -or
        $message -match '(?i)streaming response failed|provider|request queue' -or
        $null -ne $statusCode
    $executionFailure = [pscustomobject][ordered]@{
        kind       = if ($isRateLimit) { 'rate-limit' } elseif ($isProviderError) { 'provider-error' } else { 'runtime-error' }
        statusCode = $statusCode
        message    = $message
    }
}
elseif (-not [bool]$metadata.timedOut -and -not $hasStop) {
    if ([int]$metadata.exitCode -ne 0) {
        $executionFailure = [pscustomobject][ordered]@{
            kind       = 'process-error'
            statusCode = $null
            message    = "OpenCode exited with code $([int]$metadata.exitCode) without a final stop."
        }
    }
    else {
        $executionFailure = [pscustomobject][ordered]@{
            kind       = 'incomplete-trace'
            statusCode = $null
            message    = 'OpenCode exited without a final stop or terminal error event.'
        }
    }
}
$validForModelComparison = $null -eq $executionFailure
if ($null -ne $metadata.PSObject.Properties['multiuser'] -and $null -ne $metadata.multiuser -and
    $null -ne $metadata.multiuser.PSObject.Properties['readbackFailureKind'] -and $metadata.multiuser.readbackFailureKind -ceq 'harness-error') {
    $executionFailure=[pscustomobject]@{kind='harness-error';statusCode=$null;message=('Independent Multiuser readback failed: '+$metadata.multiuser.readbackFailure)}
    $validForModelComparison=$false
}
$validatorInterventions = Get-ValidatorInterventions -ToolEvents $toolEvents
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
        'multiuserLocalSave' {
            try {
                $details = Test-BenchmarkMultiuserLocalSave -ToolEvents $toolEvents `
                    -Metadata $metadata -WorkspaceRoot $workspaceRoot -Assertion $assertion
                $passed = $true
            }
            catch { $details = $_.Exception.Message; $passed = $false }
        }
        'blueprintLifecycle' {
            try {
                $details = Test-BenchmarkBlueprintLifecycle -ToolEvents $toolEvents `
                    -Metadata $metadata -WorkspaceRoot $workspaceRoot -Assertion $assertion
                $passed = $true
            }
            catch { $details = $_.Exception.Message; $passed = $false }
        }
        'plcBehaviorEvidence' {
            try {
                $details = Test-BenchmarkPlcBehaviorEvidence -ToolEvents $toolEvents `
                    -Metadata $metadata -WorkspaceRoot $workspaceRoot -Assertion $assertion
                $passed = $true
            }
            catch { $details = $_.Exception.Message; $passed = $false }
        }
        'run' {
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
        'workspaceFile' {
            $relativePath = [string]$assertion.relativePath
            $artifactPath = $null
            $artifactError = $null
            $artifactBytes = $null
            $artifactText = $null
            $contentPassed = $true
            $forbiddenPassed = $true
            $missingContentPatternIndexes = [System.Collections.Generic.List[int]]::new()
            $presentForbiddenPatternIndexes = [System.Collections.Generic.List[int]]::new()
            try {
                if ([string]::IsNullOrWhiteSpace([string]$workspaceRoot)) {
                    throw 'metadata does not contain workspacePath'
                }
                if ([System.IO.Path]::IsPathRooted($relativePath) -or
                    @($relativePath -split '[\\/]' | Where-Object { $_ -eq '..' }).Count -gt 0) {
                    throw 'relativePath is rooted or contains traversal'
                }
                $artifactPath = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $relativePath))
                $workspacePrefixForArtifact = $workspaceRoot + [System.IO.Path]::DirectorySeparatorChar
                if (-not $artifactPath.StartsWith(
                        $workspacePrefixForArtifact,
                        [System.StringComparison]::OrdinalIgnoreCase
                    )) {
                    throw 'resolved artifact is outside workspacePath'
                }
                if (-not [System.IO.File]::Exists($artifactPath)) {
                    throw 'artifact file does not exist'
                }
                $artifactBytes = [System.IO.File]::ReadAllBytes($artifactPath)
                $hasUtf8Bom = $artifactBytes.Length -ge 3 -and
                    $artifactBytes[0] -eq 0xef -and
                    $artifactBytes[1] -eq 0xbb -and
                    $artifactBytes[2] -eq 0xbf
                $offset = if ($hasUtf8Bom) { 3 } else { 0 }
                $artifactText = [System.Text.UTF8Encoding]::new($false, $true).GetString(
                    $artifactBytes,
                    $offset,
                    $artifactBytes.Length - $offset
                )

                if ($null -ne $assertion.PSObject.Properties['minBytes'] -and
                    $artifactBytes.Length -lt [int64]$assertion.minBytes) {
                    $contentPassed = $false
                }
                if ($null -ne $assertion.PSObject.Properties['maxBytes'] -and
                    $artifactBytes.Length -gt [int64]$assertion.maxBytes) {
                    $contentPassed = $false
                }
                if ($null -ne $assertion.PSObject.Properties['requireUtf8Bom'] -and
                    [bool]$assertion.requireUtf8Bom -ne $hasUtf8Bom) {
                    $contentPassed = $false
                }
                if ($null -ne $assertion.PSObject.Properties['contentPatterns']) {
                    $contentPatternIndex = 0
                    foreach ($pattern in @($assertion.contentPatterns)) {
                        if ($artifactText -notmatch [string]$pattern) {
                            $contentPassed = $false
                            $missingContentPatternIndexes.Add($contentPatternIndex)
                        }
                        $contentPatternIndex++
                    }
                }
                if ($null -ne $assertion.PSObject.Properties['forbiddenContentPatterns']) {
                    $forbiddenPatternIndex = 0
                    foreach ($pattern in @($assertion.forbiddenContentPatterns)) {
                        if ($artifactText -match [string]$pattern) {
                            $forbiddenPassed = $false
                            $presentForbiddenPatternIndexes.Add($forbiddenPatternIndex)
                        }
                        $forbiddenPatternIndex++
                    }
                }
            }
            catch {
                $artifactError = $_.Exception.Message
                $contentPassed = $false
                $forbiddenPassed = $false
            }
            $passed = $contentPassed -and $forbiddenPassed
            $artifactLength = if ($null -eq $artifactBytes) { 0 } else { $artifactBytes.Length }
            $details = "path=$relativePath; bytes=$artifactLength; contentMatched=$contentPassed; forbiddenAbsent=$forbiddenPassed"
            if ($missingContentPatternIndexes.Count -gt 0) {
                $details += "; missingContentPatternIndexes=$($missingContentPatternIndexes -join ',')"
            }
            if ($presentForbiddenPatternIndexes.Count -gt 0) {
                $details += "; presentForbiddenPatternIndexes=$($presentForbiddenPatternIndexes -join ',')"
            }
            if (-not [string]::IsNullOrWhiteSpace($artifactError)) {
                $details += "; error=$artifactError"
            }
        }
        { $_ -in @('ladBooleanNetwork', 'ladTimerNetwork') } {
            try {
                $details = if ([string]$assertion.type -eq 'ladBooleanNetwork') {
                    Test-BenchmarkLadBooleanNetwork -WorkspaceRoot $workspaceRoot -Assertion $assertion
                }
                else { Test-BenchmarkLadTimerNetwork -WorkspaceRoot $workspaceRoot -Assertion $assertion }
                $passed = $true
            }
            catch { $details = "LAD analysis failed: $($_.Exception.Message)"; $passed = $false }
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
    skillEvaluation = if ($null -ne $metadata.PSObject.Properties['skillEvaluation']) {
        $metadata.skillEvaluation
    }
    else {
        $null
    }
    skillUsage      = $skillUsage
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
    validForModelComparison = $validForModelComparison
    multiuserCheckpoint = if($null -ne $metadata.PSObject.Properties['multiuser'] -and $null -ne $metadata.multiuser){ $metadata.multiuser.checkpoint }else{ $null }
    serverPersistenceVerified = if($null -ne $metadata.PSObject.Properties['multiuser'] -and $null -ne $metadata.multiuser){ $false }else{ $null }
    executionFailure = $executionFailure
    validatorInterventions = $validatorInterventions
    score           = $score
    passed          = $validForModelComparison -and (-not $criticalFailure) -and
        ($score -ge [double]$case.passScore)
    passScore       = [double]$case.passScore
    assertions      = $assertionResults
    usage           = $usage
}

$result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result
