[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $casePath = Join-Path $testRoot 'case.json'
    $tracePath = Join-Path $testRoot 'events.jsonl'
    $metadataPath = Join-Path $testRoot 'metadata.json'
    $resultPath = Join-Path $testRoot 'result.json'
    $workspacePath = Join-Path $testRoot 'workspace'
    New-Item -ItemType Directory -Path $workspacePath | Out-Null

    $case = [ordered]@{
        schemaVersion = 1
        id            = 'ott-99'
        title         = 'Synthetic evaluator test'
        fixtureId     = 'synthetic'
        promptFile    = 'prompt.md'
        timeoutSeconds = 60
        passScore     = 100
        allowedSkills = @('synthetic')
        assertions    = @(
            [ordered]@{
                id       = 'run'
                type     = 'run'
                points   = 20
                critical = $true
            },
            [ordered]@{
                id          = 'tool'
                type        = 'tool'
                points      = 20
                namePattern = 'inspect_target$'
                minCount    = 1
                inputEquals = [ordered]@{ target = 'PLC_Main'; 'dry-run' = $true }
                outputPatterns = @('safe')
            },
            [ordered]@{
                id          = 'operation-set'
                type        = 'toolOperationSet'
                points      = 20
                critical    = $true
                namePattern = 'batch_target$'
                operationPath = 'operations'
                normalizeResultReferences = $true
                expectedOperations = @(
                    [ordered]@{ action = 'create'; match = [ordered]@{ name = 'A'; address = 1 }; resultId = 'expected-a' },
                    [ordered]@{ action = 'update'; match = [ordered]@{ name = 'B'; address = 2 }; targetRef = 'expected-a' }
                )
                minCount = 2
                maxCount = 2
            },
            [ordered]@{
                id          = 'forbidden'
                type        = 'forbiddenTool'
                points      = 20
                critical    = $true
                namePattern = 'delete_target$'
            },
            [ordered]@{
                id      = 'final'
                type    = 'finalRegex'
                points  = 20
                pattern = 'verified'
            },
            [ordered]@{
                id      = 'trace'
                type    = 'traceRegex'
                points  = 20
                pattern = 'PLC_Main'
            }
        )
    }
    $case | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $casePath -Encoding UTF8

    $events = @(
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'skill'
                state = [ordered]@{
                    status = 'completed'
                    input  = [ordered]@{ name = 'synthetic' }
                    output = 'synthetic skill content'
                }
            }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_apply-hardware-patch'
                state = [ordered]@{
                    status = 'error'
                    input  = [ordered]@{ operations = [ordered]@{ operations = @(
                                [ordered]@{ op = 'connect-node-to-subnet' },
                                [ordered]@{ op = 'connect-io-connector' }
                            ) } }
                    error = ([ordered]@{
                            success = $false
                            message = 'Hardware patch structural validation failed with 2 issue(s).'
                            data = [ordered]@{
                                ValidationBlocked = $true
                                RolledBack = $false
                                Operations = @()
                                ValidationIssues = @(
                                    [ordered]@{ OperationIndex = 0; Code = 'missing-required-field'; Field = 'subnetName' },
                                    [ordered]@{ OperationIndex = 1; Code = 'missing-required-field'; Field = 'ioSystemName' }
                                )
                            }
                        } | ConvertTo-Json -Depth 20 -Compress)
                }
            }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_apply-hardware-patch'
                state = [ordered]@{
                    status = 'error'
                    input  = [ordered]@{ operations = [ordered]@{ operations = @(
                                [ordered]@{ op = 'connect-node-to-subnet'; subnetName = 'CellPN' },
                                [ordered]@{ op = 'connect-io-connector'; ioSystemName = 'CellIO' }
                            ) } }
                    error = ([ordered]@{
                            success = $false
                            message = 'TIA placement probe rolled back.'
                            data = [ordered]@{
                                ValidationBlocked = $false
                                RolledBack = $true
                                Operations = @([ordered]@{ Index = 0; Success = $true })
                                ValidationIssues = @()
                            }
                        } | ConvertTo-Json -Depth 20 -Compress)
                }
            }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_inspect_target'
                state = [ordered]@{
                    status = 'completed'
                    input  = [ordered]@{ target = 'PLC_Main'; 'dry-run' = $true }
                    output = 'safe'
                }
            }
        },
        [ordered]@{
            type = 'text'
            part = [ordered]@{ text = 'Target verified' }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_batch_target'
                state = [ordered]@{
                    status = 'completed'
                    input  = [ordered]@{ operations = [ordered]@{ operations = @(
                                    [ordered]@{ action = 'update'; match = [ordered]@{ address = 2; name = 'B'; guard = 'kept' }; targetRef = 'actual-first' },
                                    [ordered]@{ action = 'create'; match = [ordered]@{ address = 1; name = 'A'; guard = 'kept' }; resultId = 'actual-first' }
                                ) } }
                    output = 'safe batch'
                }
            }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_batch_target'
                state = [ordered]@{
                    status = 'completed'
                    input  = [ordered]@{ operations = @(
                            [ordered]@{ action = 'create'; match = [ordered]@{ name = 'A'; address = 1; guard = 'kept' }; resultId = 'actual-second' },
                            [ordered]@{ action = 'update'; match = [ordered]@{ name = 'B'; address = 2; guard = 'kept' }; targetRef = 'actual-second' }
                        ) }
                    output = 'safe direct batch'
                }
            }
        },
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool  = 'opentia_batch_target'
                state = [ordered]@{
                    status = 'completed'
                    input  = [ordered]@{ operations = @(
                            [ordered]@{ action = 'create'; match = [ordered]@{ name = 'A'; address = 1 }; resultId = 'actual-broken' },
                            [ordered]@{ action = 'update'; match = [ordered]@{ name = 'B'; address = 2 }; targetRef = 'different-reference' }
                        ) }
                    output = 'unsafe broken reference chain'
                }
            }
        },
        [ordered]@{
            type = 'step_finish'
            part = [ordered]@{
                reason = 'stop'
                tokens = [ordered]@{
                    total = 12
                    input = 8
                    output = 4
                    reasoning = 0
                }
                cost = 0.01
            }
        }
    )
    $events |
        ForEach-Object { $_ | ConvertTo-Json -Depth 20 -Compress } |
        Set-Content -LiteralPath $tracePath -Encoding UTF8

    $metadata = [ordered]@{
        model           = 'synthetic/model'
        trialIndex      = 2
        reasoningEffort = 'provider-default'
        variant         = $null
        openCodeVersion = '1.18.10'
        gitCommit       = '0000000'
        gitBranch       = 'test'
        gitDirty        = $false
        gitStateSha256  = '0000000000000000000000000000000000000000000000000000000000000000'
        fixtureSha256   = $null
        startedAtUtc    = '2026-01-01T00:00:00.0000000Z'
        finishedAtUtc   = '2026-01-01T00:00:01.0000000Z'
        durationSeconds = 1
        exitCode        = 0
        timedOut        = $false
        workspacePath   = $workspacePath
        skillEvaluation = [ordered]@{
            arm           = 'with-skill'
            targetSkill   = 'synthetic'
            visibleSkills = @('synthetic')
        }
    }
    $metadata | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if (-not $result.passed -or [double]$result.score -ne 100) {
        throw "Expected a passing 100-point synthetic result, got passed=$($result.passed), score=$($result.score)."
    }
    if ([long]$result.usage.tokens.total -ne 12) {
        throw "Expected 12 total tokens, got $($result.usage.tokens.total)."
    }
    if ([int]$result.trialIndex -ne 2) {
        throw "Expected trialIndex 2, got $($result.trialIndex)."
    }
    if ([string]$result.skillEvaluation.arm -cne 'with-skill' -or
        [string]$result.skillEvaluation.targetSkill -cne 'synthetic' -or
        [int]$result.skillUsage.callCount -ne 1 -or
        @($result.skillUsage.loadedSkills).Count -ne 1 -or
        [string]$result.skillUsage.loadedSkills[0] -cne 'synthetic') {
        throw 'Skill-evaluation identity or skill-load telemetry was not preserved.'
    }
    if (-not [bool]$result.validForModelComparison -or $null -ne $result.executionFailure) {
        throw 'A completed trace was incorrectly marked invalid for model comparison.'
    }
    if ([int]$result.validatorInterventions.blockedCallCount -ne 1 -or
        [int]$result.validatorInterventions.rejectedBeforeTiaCount -ne 1 -or
        [int]$result.validatorInterventions.correctedOnNextPatchCount -ne 1 -or
        [int]$result.validatorInterventions.issueCount -ne 2 -or
        [string]$result.validatorInterventions.codes[0].code -ne 'missing-required-field' -or
        [int]$result.validatorInterventions.codes[0].count -ne 2 -or
        -not [bool]$result.validatorInterventions.calls[0].correctedOnNextPatch) {
        throw 'Validator-intervention telemetry did not summarize the synthetic blocked/corrected patch.'
    }

    $workspaceCandidatePath = Join-Path $workspacePath 'candidate.xml'
    $workspacePolicyEvents = @(
        [ordered]@{
            type = 'tool_use'
            part = [ordered]@{
                tool = 'opentia_validate-plc-code-artifact'
                state = [ordered]@{
                    status = 'error'
                    input = [ordered]@{ 'input-path' = $workspaceCandidatePath }
                    error = ([ordered]@{
                            success = $false
                            message = "MCP file policy rejected input-path '$workspaceCandidatePath'. Allowed roots: E:\Documents"
                            error = [ordered]@{ code = 'PolicyDenied'; message = 'denied' }
                        } | ConvertTo-Json -Depth 10 -Compress)
                }
            }
        },
        [ordered]@{
            type = 'step_finish'
            part = [ordered]@{
                reason = 'stop'
                tokens = [ordered]@{
                    total = 0; input = 0; output = 0; reasoning = 0
                }
                cost = 0
            }
        }
    )
    $workspacePolicyEvents | ForEach-Object {
        $_ | ConvertTo-Json -Depth 20 -Compress
    } | Set-Content -LiteralPath $tracePath -Encoding UTF8
    $harnessResultPath = Join-Path $testRoot 'harness-error-result.json'
    $harnessResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $harnessResultPath
    if ([bool]$harnessResult.validForModelComparison -or [bool]$harnessResult.passed -or
        [string]$harnessResult.executionFailure.kind -cne 'harness-error' -or
        [string]$harnessResult.executionFailure.message -notmatch 'rejected its own workspace path') {
        throw 'A policy rejection of the benchmark workspace was not classified as harness-invalid.'
    }

    $outsidePath = Join-Path $testRoot 'outside.xml'
    $workspacePolicyEvents[0].part.state.input.'input-path' = $outsidePath
    $outsideError = $workspacePolicyEvents[0].part.state.error | ConvertFrom-Json
    $outsideError.message = "MCP file policy rejected input-path '$outsidePath'. Allowed roots: $workspacePath"
    $workspacePolicyEvents[0].part.state.error = $outsideError | ConvertTo-Json -Depth 10 -Compress
    $workspacePolicyEvents | ForEach-Object {
        $_ | ConvertTo-Json -Depth 20 -Compress
    } | Set-Content -LiteralPath $tracePath -Encoding UTF8
    $outsideResultPath = Join-Path $testRoot 'outside-policy-result.json'
    $outsideResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $outsideResultPath
    if (-not [bool]$outsideResult.validForModelComparison -or
        $null -ne $outsideResult.executionFailure) {
        throw 'A model-selected policy rejection outside the benchmark workspace was incorrectly blamed on the harness.'
    }

    [ordered]@{
        type = 'error'
        error = [ordered]@{
            name = 'UnknownError'
            data = [ordered]@{ message = '"Streaming response failed: [503] The request queue is full."' }
        }
    } | ConvertTo-Json -Depth 20 -Compress |
        Set-Content -LiteralPath $tracePath -Encoding UTF8
    $providerResultPath = Join-Path $testRoot 'provider-error-result.json'
    $providerResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $providerResultPath
    if ([bool]$providerResult.validForModelComparison -or [bool]$providerResult.passed -or
        [string]$providerResult.executionFailure.kind -ne 'provider-error' -or
        [int]$providerResult.executionFailure.statusCode -ne 503) {
        throw 'A terminal provider 503 was not classified as endpoint-invalid.'
    }

    [ordered]@{
        type = 'error'
        error = [ordered]@{
            name = 'ProviderError'
            data = [ordered]@{
                statusCode = 429
                message = '1308 Usage limit reached. Resets at 2026-09-02T05:00:00Z'
            }
        }
    } | ConvertTo-Json -Depth 20 -Compress |
        Set-Content -LiteralPath $tracePath -Encoding UTF8
    $rateLimitResultPath = Join-Path $testRoot 'rate-limit-result.json'
    $rateLimitResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $rateLimitResultPath
    if ([bool]$rateLimitResult.validForModelComparison -or [bool]$rateLimitResult.passed -or
        [string]$rateLimitResult.executionFailure.kind -ne 'rate-limit' -or
        [int]$rateLimitResult.executionFailure.statusCode -ne 429) {
        throw 'A terminal provider 429 was not classified as a rate-limit pause signal.'
    }

    $metadata.timedOut = $true
    $metadata.finishedAtUtc = '2026-01-01T00:01:00.0000000Z'
    $metadata.durationSeconds = 60
    $metadata | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    Set-Content -LiteralPath $tracePath -Value '' -Encoding UTF8
    $timeoutResultPath = Join-Path $testRoot 'timeout-result.json'
    $timeoutResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $timeoutResultPath
    if (-not [bool]$timeoutResult.validForModelComparison -or [bool]$timeoutResult.passed -or
        $null -ne $timeoutResult.executionFailure) {
        throw 'A case timeout should remain a valid negative model outcome, not an endpoint-invalid trial.'
    }

    Write-Host 'Trace evaluator synthetic test passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
