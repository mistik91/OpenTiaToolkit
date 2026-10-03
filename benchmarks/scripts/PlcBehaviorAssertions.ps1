Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

function Assert-BenchmarkTrue {
    param([object]$Value, [string]$Label)
    if ($Value -isnot [bool] -or -not $Value) { throw "$Label is not proven true." }
}

function Assert-BenchmarkCleanCompile {
    param([object]$Compile, [string]$Label)
    if ($null -eq $Compile) { throw "$Label is missing." }
    Assert-BenchmarkTrue -Value $Compile.Success -Label $Label
    if ($Compile.ErrorCount -ne 0 -or $Compile.WarningCount -ne 0) { throw "$Label is not clean." }
}

function Convert-BenchmarkTimestamp {
    param([object]$Value)
    # PowerShell 7 deserializes ISO timestamps to DateTime; Windows PowerShell
    # keeps strings. Never stringify DateTime through the current culture.
    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) { return [DateTimeOffset]$Value }
    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Test-BenchmarkObservedValue {
    param([object]$Observation, [object]$Expected)
    $types = @{
        Bool=[bool]; Int8=[sbyte]; UInt8=[byte]; Int16=[int16]; UInt16=[uint16]
        Int32=[int32]; UInt32=[uint32]; Int64=[int64]; UInt64=[uint64]
        Float=[single]; Double=[double]; Char=[char]; WChar=[char]; String=[string]; WString=[string]
    }
    if (-not $types.ContainsKey([string]$Expected.type)) { throw 'Unknown primitive observation type.' }
    $type = $types[[string]$Expected.type]
    $actual = [Convert]::ChangeType($Observation.Value, $type, [Globalization.CultureInfo]::InvariantCulture)
    $wanted = [Convert]::ChangeType($Expected.expected, $type, [Globalization.CultureInfo]::InvariantCulture)
    if ($type -in @([single], [double]) -and ([double]::IsNaN($actual) -or [double]::IsInfinity($actual))) {
        throw 'Non-finite runtime observation.'
    }
    $comparison = if ($type -eq [string]) { [string]::CompareOrdinal($actual, $wanted) } else { $actual.CompareTo($wanted) }
    $equal = $comparison -eq 0
    if ($type -notin @([bool], [string], [char]) -and $null -ne $Expected.PSObject.Properties['tolerance']) {
        $equal = [Math]::Abs([decimal]$actual - [decimal]$wanted) -le [decimal]$Expected.tolerance
    }
    switch ([string]$Expected.operator) {
        'Equal' { return $equal }
        'NotEqual' { return -not $equal }
        'GreaterThan' { return $comparison -gt 0 }
        'GreaterThanOrEqual' { return $comparison -ge 0 }
        'LessThan' { return $comparison -lt 0 }
        'LessThanOrEqual' { return $comparison -le 0 }
        default { throw 'Unknown observation operator.' }
    }
}

function Test-BenchmarkPlcBehaviorEvidence {
    param([object[]]$ToolEvents, [object]$Metadata, [string]$WorkspaceRoot, [object]$Assertion)

    $vectorPath = Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath $Assertion.relativePath -WorkspaceInput
    if ([string]$Assertion.vectorSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        (Get-FileHash -LiteralPath $vectorPath -Algorithm SHA256).Hash -ine [string]$Assertion.vectorSha256) {
        throw 'Acceptance vector is missing, modified, or not bound to an exact SHA-256.'
    }
    $vector = Get-Content -LiteralPath $vectorPath -Raw | ConvertFrom-Json
    $candidates = @($ToolEvents | Where-Object {
            (([string]$_.part.tool -replace '-', '_') -match '^opentia_run_plc_code_behavior_probe$')
        })
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($event in $candidates) {
        try {
            if ([string]$event.part.state.status -cne 'completed') { throw 'Tool did not complete.' }
            $input = $event.part.state.input
            Assert-BenchmarkTrue -Value $input.confirm -Label 'Explicit probe confirmation'
            $actualVectorPath = [IO.Path]::GetFullPath([string]$input.'vector-path')
            if ($actualVectorPath -ine $vectorPath) { throw 'Probe used another vector path.' }
            $envelope = $event.part.state.output
            if ($envelope -is [string]) { $envelope = $envelope | ConvertFrom-Json }
            Assert-BenchmarkTrue -Value $envelope.success -Label 'Command success'
            $result = $envelope.data
            if ($result.ReusedEvidence -isnot [bool] -or $result.ReusedEvidence) { throw 'Evidence is missing freshness or was reused.' }
            $evidence = $result.Evidence
            if ($evidence.EvidenceKind -cne 'Behavior' -or $evidence.Status -cne 'Verified' -or
                [string]$evidence.EvidenceId -notmatch '^[a-f0-9]{32}$' -or
                [string]$evidence.TransactionId -notmatch '^[a-f0-9]{32}$') { throw 'Not an exact verified behavior evidence record.' }
            $started = Convert-BenchmarkTimestamp $evidence.StartedAtUtc
            $completed = Convert-BenchmarkTimestamp $evidence.CompletedAtUtc
            if ($started -lt (Convert-BenchmarkTimestamp $Metadata.startedAtUtc) -or
                $completed -gt (Convert-BenchmarkTimestamp $Metadata.finishedAtUtc) -or $completed -lt $started) {
                throw 'Evidence falls outside this trial.'
            }
            Assert-BenchmarkTrue -Value $evidence.CleanupProven -Label 'Offline cleanup'
            $behavior = $evidence.Behavior
            Assert-BenchmarkTrue -Value $behavior.SimulatorCleanupProven -Label 'Simulator cleanup'
            if ($behavior.VectorHash -ine [string]$Assertion.vectorSha256 -or
                $behavior.VectorId -cne [string]$vector.vectorId -or
                $behavior.PlcName -cne [string]$input.'plc-name' -or
                [string]$behavior.SimulatorFingerprint -notmatch '^[a-f0-9]{64}$') { throw 'Behavior/vector/PLC identity does not match.' }
            foreach ($compileProperty in @('BaselineCompilation', 'ProbeCompilation', 'CleanupCompilation')) {
                Assert-BenchmarkCleanCompile -Compile $evidence.$compileProperty -Label $compileProperty
            }
            $declared = @($evidence.DeclaredObjects)
            $hashes = @($evidence.CanonicalResultHashes)
            if ($declared.Count -lt 2 -or $hashes.Count -ne $declared.Count -or
                @($hashes | Where-Object { [string]$_ -notmatch '^[a-f0-9]{64}$' }).Count -gt 0 -or
                @($declared | Where-Object { $_.ObjectType -ceq 'GlobalDB' }).Count -lt 1 -or
                @($declared | Where-Object { $_.ObjectType -ceq 'OB' }).Count -lt 1) { throw 'Canonical generated-object coverage is incomplete.' }
            $identities = @($declared | ForEach-Object { '{0}|{1}' -f $_.ObjectType, $_.Name })
            if (@($identities | Select-Object -Unique).Count -ne $declared.Count) { throw 'Duplicate declared objects.' }
            if ($null -ne $Assertion.PSObject.Properties['requiredObjectTypes']) {
                foreach ($requiredType in $Assertion.requiredObjectTypes) {
                    if (@($declared | Where-Object { $_.ObjectType -ceq $requiredType }).Count -eq 0) { throw "Required object type $requiredType is missing." }
                }
            }
            foreach ($comparisonProperty in @('BaselineCompare', 'ProbeCompare', 'CleanupCompare')) {
                $comparison = $behavior.$comparisonProperty
                if ($null -eq $comparison) { throw "$comparisonProperty is missing." }
                Assert-BenchmarkTrue -Value $comparison.Success -Label $comparisonProperty
                Assert-BenchmarkTrue -Value $comparison.ComparedToOnline -Label $comparisonProperty
                if ($comparison.DifferenceCount -ne 0 -or @($comparison.Entries | Where-Object { $_.HasDifference }).Count -gt 0) {
                    throw "$comparisonProperty contains offline/online differences."
                }
            }
            foreach ($downloadProperty in @('ProbeDownload', 'CleanupDownload')) {
                $download = $behavior.$downloadProperty
                Assert-BenchmarkTrue -Value $download.Success -Label $downloadProperty
                Assert-BenchmarkTrue -Value $download.Applied -Label $downloadProperty
                Assert-BenchmarkTrue -Value $download.ToSimulator -Label $downloadProperty
                if ($download.DryRun -isnot [bool] -or $download.DryRun) { throw "$downloadProperty was not a real simulator download." }
            }
            $apply = $result.ApplyResult
            Assert-BenchmarkTrue -Value $apply.Verified -Label 'Transaction apply verification'
            if ($apply.TransactionId -cne $evidence.TransactionId -or $apply.Transaction.State -cne 'Succeeded') { throw 'Apply transaction identity/state mismatch.' }
            Assert-BenchmarkCleanCompile -Compile $apply.Transaction.BaselineCompilation -Label 'Transaction baseline compilation'
            Assert-BenchmarkCleanCompile -Compile $apply.Transaction.FinalCompilation -Label 'Transaction final compilation'
            $cleanup = $result.CleanupResult
            Assert-BenchmarkTrue -Value $cleanup.RolledBackAndVerified -Label 'Transaction rollback verification'
            if ($cleanup.TransactionId -cne $evidence.TransactionId -or $cleanup.RecoveryRequired -isnot [bool] -or $cleanup.RecoveryRequired) {
                throw 'Cleanup transaction mismatch or recovery required.'
            }
            $execution = $behavior.Execution
            Assert-BenchmarkTrue -Value $execution.Passed -Label 'Behavior execution'
            Assert-BenchmarkTrue -Value $execution.ResetApplied -Label 'Reset writes'
            Assert-BenchmarkTrue -Value $execution.CleanupApplied -Label 'Cleanup writes'
            $steps = @($execution.Steps)
            if ($steps.Count -ne @($vector.steps).Count) { throw 'Vector step coverage differs.' }
            $assertionCount = 0
            for ($stepIndex = 0; $stepIndex -lt $steps.Count; $stepIndex++) {
                $step = $steps[$stepIndex]
                $expectedStep = $vector.steps[$stepIndex]
                Assert-BenchmarkTrue -Value $step.Passed -Label "Step $stepIndex"
                if ($step.Name -cne $expectedStep.name -or
                    @($step.WrittenTags).Count -ne @($expectedStep.writes).Count -or
                    (@($step.WrittenTags) -join '|') -cne (@($expectedStep.writes | ForEach-Object { $_.tag }) -join '|')) {
                    throw 'Step names or applied writes differ from the frozen vector.'
                }
                $observedAssertions = @($step.Assertions)
                if ($observedAssertions.Count -ne @($expectedStep.assertions).Count) { throw 'Assertion coverage differs.' }
                for ($index = 0; $index -lt $observedAssertions.Count; $index++) {
                    $observed = $observedAssertions[$index]
                    $expected = $expectedStep.assertions[$index]
                    Assert-BenchmarkTrue -Value $observed.Passed -Label 'Assertion'
                    $expectedText = [Convert]::ToString($expected.expected, [Globalization.CultureInfo]::InvariantCulture)
                    if ($observed.Tag -cne $expected.tag -or $observed.Operator -cne $expected.operator -or $observed.Expected -cne $expectedText) {
                        throw 'Assertion identity/expected value differs.'
                    }
                    $observations = @($observed.Observations)
                    if ($observations.Count -eq 0 -or
                        @($observations | Where-Object { $_.Tag -cne $expected.tag -or $_.Type -cne $expected.type }).Count -gt 0) {
                        throw 'Typed runtime observations are missing or inconsistent.'
                    }
                    Assert-BenchmarkTrue -Value $observations[-1].Matched -Label 'Final runtime observation'
                    if (-not (Test-BenchmarkObservedValue -Observation $observations[-1] -Expected $expected)) {
                        throw 'Final typed value does not satisfy its assertion.'
                    }
                    if ($null -ne $expected.PSObject.Properties['stableMilliseconds'] -and
                        $observed.ElapsedMilliseconds -lt [int]$expected.stableMilliseconds) { throw 'Stability duration is not proven.' }
                    $assertionCount++
                }
            }
            return "Fresh verified behavior evidence=$($evidence.EvidenceId); steps=$($steps.Count); assertions=$assertionCount; both cleanups proven."
        }
        catch { $failures.Add($_.Exception.Message) }
    }
    throw "No single complete authentic probe matched the frozen vector. Calls=$($candidates.Count); $($failures -join '; ')"
}
