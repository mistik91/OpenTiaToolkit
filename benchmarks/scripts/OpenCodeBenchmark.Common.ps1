Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')
. (Join-Path $PSScriptRoot 'OpenCodeToolOutputs.ps1')

function Resolve-ExistingFile {
    param(
        [string]$ExplicitPath,
        [string]$SearchRoot,
        [string]$Filter,
        [string]$Label
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "$Label does not exist: $resolved"
        }
        return $resolved
    }

    $candidates = @(Get-ChildItem -LiteralPath $SearchRoot -Recurse -Filter $Filter -File |
            Sort-Object `
                @{ Expression = { if ($_.FullName -match '\\Debug\\') { 0 } else { 1 } }; Ascending = $true }, `
                @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true })
    if ($candidates.Count -eq 0) {
        throw "Could not find $Label below '$SearchRoot'. Build the repository or pass its explicit path."
    }
    return $candidates[0].FullName
}

function Get-BenchmarkMcpAllowedRoots {
    param([Parameter(Mandatory = $true)][string]$WorkspacePath, [switch]$IncludeDisposableRestore)

    $workspaceRoot = [System.IO.Path]::GetFullPath($WorkspacePath).TrimEnd([char[]]'\/')
    $toolkitTempRoot = [System.IO.Path]::GetFullPath(
        (Join-Path ([System.IO.Path]::GetTempPath()) 'OpenTiaToolkit')
    ).TrimEnd([char[]]'\/')
    $roots=@($workspaceRoot, $toolkitTempRoot)
    if($IncludeDisposableRestore){
        # Permit reopening only the harness-created sibling restore, never an
        # arbitrary repository/user root. Resolve also rejects ancestor junctions.
        $roots+=Resolve-BenchmarkRelativePath -Root (Split-Path -Parent $workspaceRoot) -RelativePath 'restored'
    }
    return ($roots |
            Select-Object -Unique) -join ';'
}

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

function Copy-OpenCodeBenchmarkWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$PluginRoot,
        [Parameter(Mandatory = $true)][string]$Workspace,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$VisibleSkills
    )

    $sourceOpenCodeRoot = Join-Path $PluginRoot '.opencode'
    $sourceSkillsRoot = Join-Path $sourceOpenCodeRoot 'skills'
    $destinationOpenCodeRoot = Join-Path $Workspace '.opencode'
    $destinationSkillsRoot = Join-Path $destinationOpenCodeRoot 'skills'

    if (-not (Test-Path -LiteralPath $sourceOpenCodeRoot -PathType Container)) {
        throw "OpenCode plugin configuration does not exist: $sourceOpenCodeRoot"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $PluginRoot 'AGENTS.md') -PathType Leaf)) {
        throw "OpenCode plugin agent notes do not exist below '$PluginRoot'."
    }

    Copy-Item -LiteralPath $sourceOpenCodeRoot -Destination $Workspace -Recurse
    Copy-Item -LiteralPath (Join-Path $PluginRoot 'AGENTS.md') -Destination $Workspace

    if (Test-Path -LiteralPath $destinationSkillsRoot) {
        Remove-Item -LiteralPath $destinationSkillsRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destinationSkillsRoot -Force | Out-Null

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $copied = [System.Collections.Generic.List[string]]::new()
    foreach ($skill in @($VisibleSkills)) {
        if ([string]::IsNullOrWhiteSpace($skill) -or $skill -notmatch '^[a-z0-9][a-z0-9-]*$') {
            throw "Invalid OpenCode skill name '$skill'."
        }
        if (-not $seen.Add($skill)) {
            continue
        }

        $sourceSkill = Join-Path $sourceSkillsRoot $skill
        if (-not (Test-Path -LiteralPath (Join-Path $sourceSkill 'SKILL.md') -PathType Leaf)) {
            throw "Shipped OpenCode skill '$skill' does not exist below '$sourceSkillsRoot'."
        }
        Copy-Item -LiteralPath $sourceSkill -Destination $destinationSkillsRoot -Recurse
        $copied.Add($skill)
    }

    return @($copied)
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

function Test-BenchmarkTrialRateLimited {
    param([Parameter(Mandatory = $true)][object]$Trial)

    if ((Get-BenchmarkTrialExecutionFailureKind -Trial $Trial) -ceq 'rate-limit') {
        return $true
    }

    $failureProperty = $Trial.PSObject.Properties['executionFailure']
    if ($null -eq $failureProperty -or $null -eq $failureProperty.Value) {
        return $false
    }

    $statusProperty = $failureProperty.Value.PSObject.Properties['statusCode']
    if ($null -ne $statusProperty -and $null -ne $statusProperty.Value -and
        [int]$statusProperty.Value -eq 429) {
        return $true
    }

    $messageProperty = $failureProperty.Value.PSObject.Properties['message']
    if ($null -eq $messageProperty) {
        return $false
    }
    $message = [string]$messageProperty.Value
    return $message -match '(?i)rate[ -]?limit|too many requests|usage limit|limit exhausted|temporarily overloaded|high (?:concurrency|frequency)|fair use|quota|insufficient balance|no resource package' -or
        $message -match '(?<!\d)(?:1113|1302|1305|1308|1309|1310|1311|1313|1314|1315|1316|1317|1318|1319|1320|1321)(?!\d)'
}

function Get-BenchmarkRateLimitResetAtUtc {
    param([Parameter(Mandatory = $true)][object]$Trial)

    if (-not (Test-BenchmarkTrialRateLimited -Trial $Trial)) {
        return $null
    }

    $failureProperty = $Trial.PSObject.Properties['executionFailure']
    if ($null -eq $failureProperty -or $null -eq $failureProperty.Value) {
        return $null
    }
    $messageProperty = $failureProperty.Value.PSObject.Properties['message']
    if ($null -eq $messageProperty) {
        return $null
    }

    $message = [string]$messageProperty.Value
    $matches = [regex]::Matches(
        $message,
        '(?i)(?:reset(?:s| at)?|next_flush_time)[^0-9]{0,16}(?<timestamp>20\d{2}-\d{2}-\d{2}[T ][0-9:.+-]+Z?)')
    foreach ($match in $matches) {
        $parsed = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse(
                [string]$match.Groups['timestamp'].Value,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref]$parsed)) {
            return $parsed.ToUniversalTime().ToString('o')
        }
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
            (Get-BenchmarkTrialExecutionFailureKind -Trial $_) -in @('provider-error', 'rate-limit')
        }).Count -gt 0
    $hasHarnessInvalid = @($invalidItems | Where-Object {
            (Get-BenchmarkTrialExecutionFailureKind -Trial $_) -ceq 'harness-error'
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
    if ($hasHarnessInvalid) {
        return 'harness-invalid'
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
            (Get-BenchmarkTrialExecutionFailureKind -Trial $_) -in @('provider-error', 'rate-limit')
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
        [int]$TrialCount,

        [ValidateRange(1, 10)]
        [int]$EvaluationArmCount = 1
    )

    return @($CaseIds).Count * $TrialCount * $EvaluationArmCount
}

function Get-BenchmarkCaseAggregates {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Trials
    )

    $aggregates = [System.Collections.Generic.List[object]]::new()
    foreach ($caseId in @($Trials | ForEach-Object { [string]$_.caseId } | Sort-Object -Unique)) {
        $caseTrials = @($Trials | Where-Object { [string]$_.caseId -ceq $caseId })
        $armNames = @($caseTrials | ForEach-Object {
                $property = $_.PSObject.Properties['skillEvaluationArm']
                if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                    [string]$property.Value
                }
            } | Sort-Object -Unique)
        if ($armNames.Count -eq 0) {
            $armNames = @($null)
        }

        foreach ($armName in $armNames) {
            $armTrials = if ($null -eq $armName) {
                $caseTrials
            }
            else {
                @($caseTrials | Where-Object { [string]$_.skillEvaluationArm -ceq $armName })
            }
            $statistics = Get-BenchmarkTrialStatistics -Trials $armTrials
            $record = [ordered]@{ caseId = $caseId }
            if ($null -ne $armName) {
                $record.skillEvaluationArm = $armName
            }
            foreach ($property in $statistics.PSObject.Properties) {
                $record[$property.Name] = $property.Value
            }
            $aggregates.Add([pscustomobject]$record)
        }
    }
    return $aggregates
}

function Get-BenchmarkWilsonInterval {
    param(
        [Parameter(Mandatory = $true)][ValidateRange(0, [int]::MaxValue)][int]$Successes,
        [Parameter(Mandatory = $true)][ValidateRange(0, [int]::MaxValue)][int]$Trials
    )

    if ($Trials -eq 0) {
        return $null
    }
    if ($Successes -gt $Trials) {
        throw "Success count '$Successes' exceeds trial count '$Trials'."
    }

    $z = 1.959963984540054
    $p = $Successes / [double]$Trials
    $zSquared = $z * $z
    $denominator = 1.0 + ($zSquared / $Trials)
    $center = ($p + ($zSquared / (2.0 * $Trials))) / $denominator
    $margin = ($z / $denominator) * [Math]::Sqrt(
        (($p * (1.0 - $p)) / $Trials) + ($zSquared / (4.0 * $Trials * $Trials)))

    return [pscustomobject][ordered]@{
        confidence = 0.95
        lower      = [Math]::Round([Math]::Max(0.0, $center - $margin), 4)
        upper      = [Math]::Round([Math]::Min(1.0, $center + $margin), 4)
    }
}

function Get-BenchmarkExactMcNemarPValue {
    param(
        [Parameter(Mandatory = $true)][ValidateRange(0, [int]::MaxValue)][int]$SkillOnlyPassCount,
        [Parameter(Mandatory = $true)][ValidateRange(0, [int]::MaxValue)][int]$BaselineOnlyPassCount
    )

    $discordant = $SkillOnlyPassCount + $BaselineOnlyPassCount
    if ($discordant -eq 0) {
        return 1.0
    }

    $tail = [Math]::Min($SkillOnlyPassCount, $BaselineOnlyPassCount)
    $probability = [Math]::Pow(0.5, $discordant)
    $term = 1.0
    $sum = $probability
    for ($index = 1; $index -le $tail; $index++) {
        $term *= ($discordant - $index + 1) / [double]$index
        $sum += $term * $probability
    }
    return [Math]::Round([Math]::Min(1.0, 2.0 * $sum), 6)
}

function Get-BenchmarkSkillCalls {
    param(
        [Parameter(Mandatory = $true)][object]$Result,
        [Parameter(Mandatory = $true)][string]$SkillName
    )

    $skillUsageProperty = $Result.PSObject.Properties['skillUsage']
    if ($null -eq $skillUsageProperty -or $null -eq $skillUsageProperty.Value) {
        return
    }
    $callsProperty = $skillUsageProperty.Value.PSObject.Properties['calls']
    if ($null -eq $callsProperty -or $null -eq $callsProperty.Value) {
        return
    }

    foreach ($call in @($callsProperty.Value)) {
        if ($null -eq $call) {
            continue
        }
        $nameProperty = $call.PSObject.Properties['name']
        if ($null -ne $nameProperty -and [string]$nameProperty.Value -ceq $SkillName) {
            $call
        }
    }
}

function Get-BenchmarkSkillLiftStatistics {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Trials,
        [Parameter(Mandatory = $true)][string]$TargetSkill
    )

    $items = @($Trials)
    $withSkill = @($items | Where-Object { [string]$_.skillEvaluationArm -ceq 'with-skill' })
    $withoutSkill = @($items | Where-Object { [string]$_.skillEvaluationArm -ceq 'without-skill' })
    $withStatistics = Get-BenchmarkTrialStatistics -Trials $withSkill
    $withoutStatistics = Get-BenchmarkTrialStatistics -Trials $withoutSkill

    $pairRecords = [System.Collections.Generic.List[object]]::new()
    $allPairKeys = @($items | ForEach-Object {
            '{0}::{1}' -f [string]$_.caseId, [int]$_.trialIndex
        } | Sort-Object -Unique)
    foreach ($pairKey in $allPairKeys) {
        $parts = $pairKey -split '::', 2
        $caseId = $parts[0]
        $trialIndex = [int]$parts[1]
        $with = @($withSkill | Where-Object {
                [string]$_.caseId -ceq $caseId -and [int]$_.trialIndex -eq $trialIndex
            })
        $without = @($withoutSkill | Where-Object {
                [string]$_.caseId -ceq $caseId -and [int]$_.trialIndex -eq $trialIndex
            })
        if ($with.Count -ne 1 -or $without.Count -ne 1) {
            continue
        }
        if (-not (Test-BenchmarkTrialValidForModelComparison -Trial $with[0]) -or
            -not (Test-BenchmarkTrialValidForModelComparison -Trial $without[0])) {
            continue
        }
        $pairRecords.Add([pscustomobject][ordered]@{
                caseId              = $caseId
                trialIndex          = $trialIndex
                withSkillPassed     = [bool]$with[0].passed
                withoutSkillPassed  = [bool]$without[0].passed
                withSkillScore      = [double]$with[0].score
                withoutSkillScore   = [double]$without[0].score
                scoreDelta          = [Math]::Round(
                    [double]$with[0].score - [double]$without[0].score, 2)
            })
    }

    $bothPass = @($pairRecords | Where-Object { $_.withSkillPassed -and $_.withoutSkillPassed }).Count
    $skillOnlyPass = @($pairRecords | Where-Object { $_.withSkillPassed -and -not $_.withoutSkillPassed }).Count
    $baselineOnlyPass = @($pairRecords | Where-Object { -not $_.withSkillPassed -and $_.withoutSkillPassed }).Count
    $neitherPass = @($pairRecords | Where-Object { -not $_.withSkillPassed -and -not $_.withoutSkillPassed }).Count
    $scoreDeltas = @($pairRecords | ForEach-Object { [double]$_.scoreDelta })

    $withValidCount = [int]$withStatistics.validTrialCount
    $withoutValidCount = [int]$withoutStatistics.validTrialCount
    $withActivationCount = @($withSkill | Where-Object {
            $property = $_.PSObject.Properties['targetSkillLoaded']
            $null -ne $property -and [bool]$property.Value
        }).Count
    $withoutActivationCount = @($withoutSkill | Where-Object {
            $property = $_.PSObject.Properties['targetSkillLoaded']
            $null -ne $property -and [bool]$property.Value
        }).Count
    $routingRecords = @($withSkill | Where-Object {
            Test-BenchmarkTrialValidForModelComparison -Trial $_
        } | ForEach-Object {
            $routingProperty = $_.PSObject.Properties['routingExpectation']
            $expectedProperty = $_.PSObject.Properties['expectedSkill']
            if ($null -ne $routingProperty -and
                -not [string]::IsNullOrWhiteSpace([string]$routingProperty.Value) -and
                $null -ne $expectedProperty) {
                $loadedProperty = $_.PSObject.Properties['targetSkillLoaded']
                $loaded = $null -ne $loadedProperty -and [bool]$loadedProperty.Value
                $shouldLoad = $null -ne $expectedProperty.Value
                [pscustomobject][ordered]@{
                    caseId             = [string]$_.caseId
                    trialIndex         = [int]$_.trialIndex
                    routingExpectation = [string]$routingProperty.Value
                    expectedSkill      = $expectedProperty.Value
                    targetSkillLoaded  = $loaded
                    correct            = $loaded -eq $shouldLoad
                }
            }
        })
    $routingBuckets = @($routingRecords |
            Group-Object -Property routingExpectation |
            Sort-Object Name |
            ForEach-Object {
                $correctCount = @($_.Group | Where-Object { [bool]$_.correct }).Count
                [pscustomobject][ordered]@{
                    routingExpectation = [string]$_.Name
                    trialCount         = $_.Count
                    correctCount       = $correctCount
                    accuracy           = if ($_.Count -gt 0) {
                        [Math]::Round($correctCount / [double]$_.Count, 4)
                    }
                    else { $null }
                }
            })
    $routingCorrectCount = @($routingRecords | Where-Object { [bool]$_.correct }).Count

    return [pscustomobject][ordered]@{
        targetSkill = $TargetSkill
        withSkill = [pscustomobject][ordered]@{
            statistics           = $withStatistics
            passRateWilson95     = Get-BenchmarkWilsonInterval `
                -Successes ([int]$withStatistics.passedCount) `
                -Trials $withValidCount
            targetLoadedCount    = $withActivationCount
            targetLoadedRate     = if ($withSkill.Count -gt 0) {
                [Math]::Round($withActivationCount / [double]$withSkill.Count, 4)
            }
            else { $null }
        }
        withoutSkill = [pscustomobject][ordered]@{
            statistics           = $withoutStatistics
            passRateWilson95     = Get-BenchmarkWilsonInterval `
                -Successes ([int]$withoutStatistics.passedCount) `
                -Trials $withoutValidCount
            targetLoadedCount    = $withoutActivationCount
            targetLoadedRate     = if ($withoutSkill.Count -gt 0) {
                [Math]::Round($withoutActivationCount / [double]$withoutSkill.Count, 4)
            }
            else { $null }
        }
        lift = [pscustomobject][ordered]@{
            meanScore       = if ($null -ne $withStatistics.meanScore -and
                $null -ne $withoutStatistics.meanScore) {
                [Math]::Round(
                    [double]$withStatistics.meanScore - [double]$withoutStatistics.meanScore, 2)
            }
            else { $null }
            passRate        = if ($null -ne $withStatistics.passRate -and
                $null -ne $withoutStatistics.passRate) {
                [Math]::Round(
                    [double]$withStatistics.passRate - [double]$withoutStatistics.passRate, 4)
            }
            else { $null }
            pairedMeanScore = if ($scoreDeltas.Count -gt 0) {
                [Math]::Round([double](($scoreDeltas | Measure-Object -Average).Average), 2)
            }
            else { $null }
            meanDurationSeconds = if ($null -ne $withStatistics.meanDurationSeconds -and
                $null -ne $withoutStatistics.meanDurationSeconds) {
                [Math]::Round(
                    [double]$withStatistics.meanDurationSeconds -
                    [double]$withoutStatistics.meanDurationSeconds, 3)
            }
            else { $null }
            meanReasoningTokens = if ($null -ne $withStatistics.meanReasoningTokens -and
                $null -ne $withoutStatistics.meanReasoningTokens) {
                [Math]::Round(
                    [double]$withStatistics.meanReasoningTokens -
                    [double]$withoutStatistics.meanReasoningTokens, 2)
            }
            else { $null }
            meanCostUsd = if ($null -ne $withStatistics.meanCostUsd -and
                $null -ne $withoutStatistics.meanCostUsd) {
                [Math]::Round(
                    [double]$withStatistics.meanCostUsd -
                    [double]$withoutStatistics.meanCostUsd, 8)
            }
            else { $null }
        }
        routing = [pscustomobject][ordered]@{
            trialCount   = $routingRecords.Count
            correctCount = $routingCorrectCount
            accuracy     = if ($routingRecords.Count -gt 0) {
                [Math]::Round($routingCorrectCount / [double]$routingRecords.Count, 4)
            }
            else { $null }
            buckets      = $routingBuckets
            trials       = $routingRecords
        }
        pairedOutcomes = [pscustomobject][ordered]@{
            validPairCount       = $pairRecords.Count
            bothPass             = $bothPass
            skillOnlyPass        = $skillOnlyPass
            baselineOnlyPass     = $baselineOnlyPass
            neitherPass          = $neitherPass
            exactMcNemarPValue   = Get-BenchmarkExactMcNemarPValue `
                -SkillOnlyPassCount $skillOnlyPass `
                -BaselineOnlyPassCount $baselineOnlyPass
            pairs                = @($pairRecords)
        }
    }
}
