Set-StrictMode -Version Latest

function Get-OpenCodeModelProvider {
    param([Parameter(Mandatory = $true)][string]$Model)

    if ($Model -notmatch '^(?<provider>[A-Za-z0-9._-]+)/.+$') {
        throw "OpenCode model '$Model' must use the provider/model form."
    }
    return [string]$Matches.provider
}

function Resolve-OpenCodeUserAuthPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "OpenCode auth file does not exist: $resolved"
        }
        return $resolved
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    $xdgDataHome = [Environment]::GetEnvironmentVariable('XDG_DATA_HOME')
    if (-not [string]::IsNullOrWhiteSpace($xdgDataHome)) {
        $candidates.Add((Join-Path $xdgDataHome 'opencode\auth.json'))
    }

    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $candidates.Add((Join-Path $userProfile '.local\share\opencode\auth.json'))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Could not find the OpenCode user auth file. Checked: $($candidates -join ', ')"
}

function Get-OpenCodeProviderCredential {
    param(
        [Parameter(Mandatory = $true)][string]$AuthPath,
        [Parameter(Mandatory = $true)][string]$Provider
    )

    $auth = Get-Content -LiteralPath $AuthPath -Raw | ConvertFrom-Json
    $credentialProperty = $auth.PSObject.Properties[$Provider]
    if ($null -eq $credentialProperty) {
        throw "OpenCode user auth has no credential for provider '$Provider'."
    }

    $credential = $credentialProperty.Value
    $typeProperty = $credential.PSObject.Properties['type']
    if ($null -eq $typeProperty) {
        throw "OpenCode credential for provider '$Provider' has no type."
    }

    switch ([string]$typeProperty.Value) {
        'api' {
            $keyProperty = $credential.PSObject.Properties['key']
            if ($null -eq $keyProperty -or
                [string]::IsNullOrWhiteSpace([string]$keyProperty.Value)) {
                throw "OpenCode API credential for provider '$Provider' has no key."
            }
        }
        'oauth' {
            foreach ($propertyName in @('refresh', 'access', 'expires')) {
                $property = $credential.PSObject.Properties[$propertyName]
                if ($null -eq $property -or $null -eq $property.Value -or
                    ($property.Value -is [string] -and
                        [string]::IsNullOrWhiteSpace([string]$property.Value))) {
                    throw "OpenCode OAuth credential for provider '$Provider' has no '$propertyName' value."
                }
            }
        }
        default {
            throw "OpenCode credential for provider '$Provider' has unsupported type '$($typeProperty.Value)'."
        }
    }

    return $credential
}

function New-ProviderScopedOpenCodeAuthFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourceAuthPath,
        [Parameter(Mandatory = $true)][string]$Provider,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $credential = Get-OpenCodeProviderCredential -AuthPath $SourceAuthPath -Provider $Provider
    $destinationDirectory = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

    $scopedAuth = [ordered]@{}
    $scopedAuth[$Provider] = $credential
    $scopedAuth |
        ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath $DestinationPath -Encoding UTF8

    return [pscustomobject]@{
        Provider = $Provider
        Path     = $DestinationPath
    }
}

function Remove-BenchmarkSecretFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    $stream = $null
    try {
        $length = (Get-Item -LiteralPath $Path).Length
        if ($length -gt 0) {
            $stream = [System.IO.File]::Open(
                $Path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            $buffer = [byte[]]::new([Math]::Min(8192, [int]$length))
            $remaining = [long]$length
            while ($remaining -gt 0) {
                $count = [Math]::Min($buffer.Length, [int]$remaining)
                $stream.Write($buffer, 0, $count)
                $remaining -= $count
            }
            $stream.Flush($true)
        }
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}

function ConvertFrom-OpenCodeModelListing {
    param(
        [Parameter(Mandatory = $true)][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Model
    )

    $modelIndex = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ([string]$Lines[$index] -ceq $Model) {
            $modelIndex = $index
            break
        }
    }
    if ($modelIndex -lt 0) {
        throw "Model '$Model' was not present in the OpenCode catalog."
    }

    $jsonLines = [System.Collections.Generic.List[string]]::new()
    $foundStart = $false
    for ($index = $modelIndex + 1; $index -lt $Lines.Count; $index++) {
        $line = [string]$Lines[$index]
        if (-not $foundStart) {
            if ($line -ceq '{') {
                $foundStart = $true
                $jsonLines.Add($line)
            }
            continue
        }

        $jsonLines.Add($line)
        if ($line -ceq '}') {
            try {
                return (($jsonLines -join "`n") | ConvertFrom-Json)
            }
            catch {
                throw "OpenCode returned invalid verbose metadata for '$Model': $($_.Exception.Message)"
            }
        }
    }

    throw "OpenCode returned no complete metadata object for '$Model'."
}

function Get-OpenCodeModelMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$OpenCodeExecutable,
        [Parameter(Mandatory = $true)][string]$Model,
        [switch]$Refresh
    )

    $provider = Get-OpenCodeModelProvider -Model $Model
    $arguments = @('models', $provider, '--verbose', '--pure')
    if ($Refresh) {
        $arguments += '--refresh'
    }

    $lines = @(& $OpenCodeExecutable @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "OpenCode model discovery failed for provider '$provider' with exit code $LASTEXITCODE."
    }
    return ConvertFrom-OpenCodeModelListing -Lines $lines -Model $Model
}

function Test-BenchmarkTrialValidForModelComparison {
    param([Parameter(Mandatory = $true)][object]$Trial)

    $property = $Trial.PSObject.Properties['validForModelComparison']
    return $null -eq $property -or [bool]$property.Value
}

function Get-BenchmarkTrialExecutionFailureKind {
    param([Parameter(Mandatory = $true)][object]$Trial)

    $failureProperty = $Trial.PSObject.Properties['executionFailure']
    if ($null -ne $failureProperty -and $null -ne $failureProperty.Value) {
        $kindProperty = $failureProperty.Value.PSObject.Properties['kind']
        if ($null -ne $kindProperty) {
            return [string]$kindProperty.Value
        }
    }
    $kindProperty = $Trial.PSObject.Properties['executionFailureKind']
    if ($null -ne $kindProperty) {
        return [string]$kindProperty.Value
    }
    return $null
}

function Test-BenchmarkTrialRuntimeFailure {
    param([Parameter(Mandatory = $true)][object]$Trial)

    if (-not (Test-BenchmarkTrialValidForModelComparison -Trial $Trial)) {
        return $true
    }
    $timedOutProperty = $Trial.PSObject.Properties['timedOut']
    if ($null -ne $timedOutProperty -and [bool]$timedOutProperty.Value) {
        return $true
    }
    $exitCodeProperty = $Trial.PSObject.Properties['exitCode']
    return $null -ne $exitCodeProperty -and [int]$exitCodeProperty.Value -ne 0
}

function Get-BenchmarkModelStatus {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Trials,
        [Parameter(Mandatory = $true)][int]$ExpectedTrialCount
    )

    $items = @($Trials)
    $invalidItems = @($items | Where-Object {
            -not (Test-BenchmarkTrialValidForModelComparison -Trial $_)
        })
    $hasEndpointInvalid = @($invalidItems | Where-Object {
            (Get-BenchmarkTrialExecutionFailureKind -Trial $_) -eq 'provider-error'
        }).Count -gt 0
    $hasRuntimeFailure = @($items | Where-Object {
            Test-BenchmarkTrialRuntimeFailure -Trial $_
        }).Count -gt 0
    $hasCriticalFailure = @($items | Where-Object {
            (Test-BenchmarkTrialValidForModelComparison -Trial $_) -and [bool]$_.criticalFailure
        }).Count -gt 0
    $allPassed = $items.Count -eq $ExpectedTrialCount -and $invalidItems.Count -eq 0 -and
        @($items | Where-Object { -not [bool]$_.passed }).Count -eq 0

    if ($hasEndpointInvalid) {
        return 'endpoint-invalid'
    }
    if ($hasRuntimeFailure) {
        return 'runtime-failure'
    }
    if ($hasCriticalFailure) {
        return 'disqualified-critical-failure'
    }
    if ($allPassed) {
        return 'qualified'
    }
    if ($items.Count -eq $ExpectedTrialCount) {
        return 'not-qualified'
    }
    return 'incomplete'
}

function Get-BenchmarkTrialStatistics {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Trials
    )

    $items = @($Trials)
    if ($items.Count -eq 0) {
        return [pscustomobject][ordered]@{
            trialCount             = 0
            validTrialCount        = 0
            invalidTrialCount      = 0
            endpointInvalidCount   = 0
            passedCount            = 0
            passRate               = 0.0
            criticalFailureCount   = 0
            criticalFailureRate    = 0.0
            runtimeFailureCount    = 0
            meanScore              = 0.0
            minScore               = 0.0
            maxScore               = 0.0
            rawMeanScore           = 0.0
            totalCostUsd           = 0.0
            meanCostUsd            = 0.0
            costPerPassedTrialUsd  = $null
            meanDurationSeconds    = 0.0
            p50DurationSeconds     = 0.0
            meanReasoningTokens    = 0.0
        }
    }

    $validItems = @($items | Where-Object {
            Test-BenchmarkTrialValidForModelComparison -Trial $_
        })
    $invalidItems = @($items | Where-Object {
            -not (Test-BenchmarkTrialValidForModelComparison -Trial $_)
        })
    $endpointInvalidCount = @($invalidItems | Where-Object {
            (Get-BenchmarkTrialExecutionFailureKind -Trial $_) -eq 'provider-error'
        }).Count
    $passedCount = @($validItems | Where-Object { [bool]$_.passed }).Count
    $criticalFailureCount = @($validItems | Where-Object { [bool]$_.criticalFailure }).Count
    $runtimeFailureCount = @($items | Where-Object {
            Test-BenchmarkTrialRuntimeFailure -Trial $_
        }).Count
    $scores = @($validItems | ForEach-Object { [double]$_.score })
    $rawScores = @($items | ForEach-Object { [double]$_.score })
    $durations = @($validItems | ForEach-Object { [double]$_.durationSeconds } | Sort-Object)
    $costs = @($items | ForEach-Object { [double]$_.cost })
    $reasoningTokens = @($validItems | ForEach-Object {
            $property = $_.PSObject.Properties['reasoningTokens']
            if ($null -eq $property) {
                0L
            }
            else {
                [long]$property.Value
            }
        })

    $middle = [int][Math]::Floor($durations.Count / 2)
    $medianDuration = if ($durations.Count -eq 0) {
        $null
    }
    elseif ($durations.Count % 2 -eq 1) {
        [double]$durations[$middle]
    }
    else {
        ([double]$durations[$middle - 1] + [double]$durations[$middle]) / 2.0
    }
    $totalCost = [double](($costs | Measure-Object -Sum).Sum)

    return [pscustomobject][ordered]@{
        trialCount             = $items.Count
        validTrialCount        = $validItems.Count
        invalidTrialCount      = $invalidItems.Count
        endpointInvalidCount   = $endpointInvalidCount
        passedCount            = $passedCount
        passRate               = if ($validItems.Count -gt 0) {
            [Math]::Round($passedCount / [double]$validItems.Count, 4)
        }
        else {
            $null
        }
        criticalFailureCount   = $criticalFailureCount
        criticalFailureRate    = if ($validItems.Count -gt 0) {
            [Math]::Round($criticalFailureCount / [double]$validItems.Count, 4)
        }
        else {
            $null
        }
        runtimeFailureCount    = $runtimeFailureCount
        meanScore              = if ($scores.Count -gt 0) {
            [Math]::Round([double](($scores | Measure-Object -Average).Average), 2)
        }
        else { $null }
        minScore               = if ($scores.Count -gt 0) {
            [Math]::Round([double](($scores | Measure-Object -Minimum).Minimum), 2)
        }
        else { $null }
        maxScore               = if ($scores.Count -gt 0) {
            [Math]::Round([double](($scores | Measure-Object -Maximum).Maximum), 2)
        }
        else { $null }
        rawMeanScore           = [Math]::Round(
            [double](($rawScores | Measure-Object -Average).Average), 2)
        totalCostUsd           = [Math]::Round($totalCost, 8)
        meanCostUsd            = [Math]::Round($totalCost / $items.Count, 8)
        costPerPassedTrialUsd  = if ($passedCount -gt 0) {
            [Math]::Round($totalCost / $passedCount, 8)
        }
        else {
            $null
        }
        meanDurationSeconds    = if ($durations.Count -gt 0) {
            [Math]::Round([double](($durations | Measure-Object -Average).Average), 3)
        }
        else { $null }
        p50DurationSeconds     = if ($null -ne $medianDuration) {
            [Math]::Round($medianDuration, 3)
        }
        else { $null }
        meanReasoningTokens    = if ($reasoningTokens.Count -gt 0) {
            [Math]::Round([double](($reasoningTokens | Measure-Object -Average).Average), 2)
        }
        else { $null }
    }
}

function Get-BenchmarkExpectedTrialCount {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$CaseIds,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 10)]
        [int]$TrialCount
    )

    return @($CaseIds).Count * $TrialCount
}

function Get-BenchmarkCaseAggregates {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Trials
    )

    $aggregates = [System.Collections.Generic.List[object]]::new()
    foreach ($group in @($Trials | Group-Object -Property caseId | Sort-Object Name)) {
        $statistics = Get-BenchmarkTrialStatistics -Trials @($group.Group)
        $record = [ordered]@{ caseId = [string]$group.Name }
        foreach ($property in $statistics.PSObject.Properties) {
            $record[$property.Name] = $property.Value
        }
        $aggregates.Add([pscustomobject]$record)
    }
    return $aggregates
}
