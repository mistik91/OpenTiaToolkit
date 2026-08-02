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
