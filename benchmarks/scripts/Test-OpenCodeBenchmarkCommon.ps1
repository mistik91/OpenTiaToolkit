[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkCommonTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    if ((Get-OpenCodeModelProvider -Model 'openrouter/example/model') -cne 'openrouter') {
        throw 'Provider parsing did not preserve the OpenCode provider id.'
    }

    $listing = @(
        'openrouter/example/model',
        '{',
        '  "id": "example/model",',
        '  "providerID": "openrouter",',
        '  "status": "active",',
        '  "cost": {',
        '    "input": 0.1,',
        '    "output": 0.2',
        '  },',
        '  "capabilities": {',
        '    "toolcall": true',
        '  }',
        '}'
    )
    $model = ConvertFrom-OpenCodeModelListing -Lines $listing -Model 'openrouter/example/model'
    if ([string]$model.providerID -cne 'openrouter' -or -not [bool]$model.capabilities.toolcall) {
        throw 'Verbose model metadata parsing returned the wrong model.'
    }

    $statistics = Get-BenchmarkTrialStatistics -Trials @(
        [pscustomobject]@{
            caseId = 'ott-01'; score = 100; passed = $true; criticalFailure = $false
            timedOut = $false; exitCode = 0; cost = 0.01; durationSeconds = 10
            reasoningTokens = 100
        },
        [pscustomobject]@{
            caseId = 'ott-01'; score = 80; passed = $true; criticalFailure = $false
            timedOut = $false; exitCode = 0; cost = 0.02; durationSeconds = 20
            reasoningTokens = 200
        },
        [pscustomobject]@{
            caseId = 'ott-01'; score = 20; passed = $false; criticalFailure = $true
            timedOut = $false; exitCode = 0; cost = 0.03; durationSeconds = 90
            reasoningTokens = 300
        },
        [pscustomobject]@{
            caseId = 'ott-01'; score = 0; passed = $false; criticalFailure = $true
            timedOut = $false; exitCode = 0; cost = 0.04; durationSeconds = 120
            reasoningTokens = 400; validForModelComparison = $false
            executionFailureKind = 'provider-error'
        }
    )
    if ([int]$statistics.trialCount -ne 4 -or
        [int]$statistics.validTrialCount -ne 3 -or
        [int]$statistics.invalidTrialCount -ne 1 -or
        [int]$statistics.endpointInvalidCount -ne 1 -or
        [double]$statistics.passRate -ne 0.6667 -or
        [double]$statistics.criticalFailureRate -ne 0.3333 -or
        [double]$statistics.meanScore -ne 66.67 -or
        [double]$statistics.rawMeanScore -ne 50 -or
        [double]$statistics.p50DurationSeconds -ne 20 -or
        [double]$statistics.totalCostUsd -ne 0.10 -or
        [double]$statistics.costPerPassedTrialUsd -ne 0.05 -or
        [double]$statistics.meanReasoningTokens -ne 200) {
        throw 'Trial aggregation returned unexpected statistics.'
    }
    if ((Get-BenchmarkModelStatus -Trials @(
                [pscustomobject]@{
                    passed = $false; criticalFailure = $true; timedOut = $false; exitCode = 0
                    validForModelComparison = $false; executionFailureKind = 'provider-error'
                }) -ExpectedTrialCount 1) -ne 'endpoint-invalid') {
        throw 'Endpoint-invalid model status was not preserved.'
    }

    $rateLimitedTrial = [pscustomobject]@{
        passed = $false; criticalFailure = $false; timedOut = $false; exitCode = 0
        score = 0; cost = 0; durationSeconds = 1; reasoningTokens = 0
        validForModelComparison = $false
        executionFailure = [pscustomobject]@{
            kind = 'rate-limit'; statusCode = 429
            message = '1308 Usage limit reached. Resets at 2026-09-02T05:00:00Z'
        }
    }
    $rateLimitedStatistics = Get-BenchmarkTrialStatistics -Trials @($rateLimitedTrial)
    if (-not (Test-BenchmarkTrialRateLimited -Trial $rateLimitedTrial) -or
        [string](Get-BenchmarkRateLimitResetAtUtc -Trial $rateLimitedTrial) -cne '2026-09-02T05:00:00.0000000+00:00' -or
        [int]$rateLimitedStatistics.endpointInvalidCount -ne 1 -or
        (Get-BenchmarkModelStatus -Trials @($rateLimitedTrial) -ExpectedTrialCount 1) -ne 'endpoint-invalid') {
        throw 'Rate-limit detection, reset parsing, or endpoint-invalid aggregation failed.'
    }

    $legacyCompactStatistics = Get-BenchmarkTrialStatistics -Trials @(
        [pscustomobject]@{
            caseId = 'ott-01'; score = 100; passed = $true; criticalFailure = $false
            cost = 0; durationSeconds = 1
        }
    )
    if ([int]$legacyCompactStatistics.runtimeFailureCount -ne 0 -or
        (Get-BenchmarkModelStatus -Trials @(
                [pscustomobject]@{
                    passed = $true; criticalFailure = $false
                }) -ExpectedTrialCount 1) -ne 'qualified') {
        throw 'Legacy compact trial records without runtime fields were not treated as successful defaults.'
    }

    if ((Get-BenchmarkExpectedTrialCount -CaseIds 'ott-14' -TrialCount 3) -ne 3 -or
        (Get-BenchmarkExpectedTrialCount -CaseIds @('ott-01', 'ott-14') -TrialCount 3) -ne 6 -or
        (Get-BenchmarkExpectedTrialCount -CaseIds @('ott-01', 'ott-14') -TrialCount 3 -EvaluationArmCount 2) -ne 12) {
        throw 'Expected trial count did not preserve scalar, multi-case, and paired-arm campaign selections.'
    }

    $nonTargetSkillResult = [pscustomobject]@{
        skillUsage = [pscustomobject]@{
            calls = @(
                [pscustomobject]@{ name = 'opentia-toolkit-workflow'; status = 'completed' },
                [pscustomobject]@{ name = 'basic-tia-interaction'; status = 'completed' }
            )
        }
    }
    $noTargetCalls = @(Get-BenchmarkSkillCalls `
            -Result $nonTargetSkillResult `
            -SkillName 'hardware-configuration-workflow')
    $targetSkillResult = [pscustomobject]@{
        skillUsage = [pscustomobject]@{
            calls = @([pscustomobject]@{
                    name = 'hardware-configuration-workflow'; status = 'completed'
                })
        }
    }
    $targetCalls = @(Get-BenchmarkSkillCalls `
            -Result $targetSkillResult `
            -SkillName 'hardware-configuration-workflow')
    if (@($noTargetCalls).Count -ne 0 -or @($targetCalls).Count -ne 1 -or
        [string]$targetCalls[0].status -cne 'completed') {
        throw 'Skill-call selection did not preserve empty and matching call sets.'
    }

    $lift = Get-BenchmarkSkillLiftStatistics `
        -TargetSkill 'hardware-configuration-workflow' `
        -Trials @(
            [pscustomobject]@{
                caseId = 'ott-14'; trialIndex = 1; skillEvaluationArm = 'without-skill'
                score = 40; passed = $false; criticalFailure = $false; timedOut = $false
                exitCode = 0; cost = 0; durationSeconds = 10; reasoningTokens = 100
                validForModelComparison = $true; targetSkillLoaded = $false
            },
            [pscustomobject]@{
                caseId = 'ott-14'; trialIndex = 1; skillEvaluationArm = 'with-skill'
                score = 100; passed = $true; criticalFailure = $false; timedOut = $false
                exitCode = 0; cost = 0; durationSeconds = 12; reasoningTokens = 110
                validForModelComparison = $true; targetSkillLoaded = $true
                routingExpectation = 'explicit'; expectedSkill = 'hardware-configuration-workflow'
            },
            [pscustomobject]@{
                caseId = 'ott-01'; trialIndex = 1; skillEvaluationArm = 'without-skill'
                score = 100; passed = $true; criticalFailure = $false; timedOut = $false
                exitCode = 0; cost = 0; durationSeconds = 8; reasoningTokens = 80
                validForModelComparison = $true; targetSkillLoaded = $false
            },
            [pscustomobject]@{
                caseId = 'ott-01'; trialIndex = 1; skillEvaluationArm = 'with-skill'
                score = 100; passed = $true; criticalFailure = $false; timedOut = $false
                exitCode = 0; cost = 0; durationSeconds = 9; reasoningTokens = 85
                validForModelComparison = $true; targetSkillLoaded = $false
                routingExpectation = 'negative'; expectedSkill = $null
            }
        )
    if ([double]$lift.lift.meanScore -ne 30 -or
        [double]$lift.lift.passRate -ne 0.5 -or
        [double]$lift.lift.pairedMeanScore -ne 30 -or
        [int]$lift.pairedOutcomes.validPairCount -ne 2 -or
        [int]$lift.pairedOutcomes.skillOnlyPass -ne 1 -or
        [int]$lift.pairedOutcomes.bothPass -ne 1 -or
        [double]$lift.pairedOutcomes.exactMcNemarPValue -ne 1 -or
        [int]$lift.withSkill.targetLoadedCount -ne 1 -or
        [int]$lift.routing.trialCount -ne 2 -or
        [double]$lift.routing.accuracy -ne 1) {
        throw 'Paired skill-lift aggregation returned unexpected statistics.'
    }

    $caseArmAggregates = @(Get-BenchmarkCaseAggregates -Trials @(
            [pscustomobject]@{
                caseId = 'ott-14'; skillEvaluationArm = 'without-skill'; score = 40
                passed = $false; criticalFailure = $false; cost = 0; durationSeconds = 10
            },
            [pscustomobject]@{
                caseId = 'ott-14'; skillEvaluationArm = 'with-skill'; score = 100
                passed = $true; criticalFailure = $false; cost = 0; durationSeconds = 12
            }
        ))
    $sortedArms = @($caseArmAggregates | ForEach-Object {
            [string]$_.skillEvaluationArm
        } | Sort-Object)
    if ($caseArmAggregates.Count -ne 2 -or
        $sortedArms.Count -ne 2 -or
        $sortedArms -cnotcontains 'with-skill' -or
        $sortedArms -cnotcontains 'without-skill') {
        throw 'Case aggregation did not keep skill-evaluation arms separate.'
    }

    $fakePluginRoot = Join-Path $testRoot 'plugin'
    $fakeOpenCodeRoot = Join-Path $fakePluginRoot '.opencode'
    $fakeSkillsRoot = Join-Path $fakeOpenCodeRoot 'skills'
    New-Item -ItemType Directory -Path (Join-Path $fakeOpenCodeRoot 'plugins') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fakeOpenCodeRoot 'plugins\tool.js') -Value 'export {}'
    Set-Content -LiteralPath (Join-Path $fakePluginRoot 'AGENTS.md') -Value '# Agent notes'
    foreach ($skillName in @('kept-skill', 'hidden-skill')) {
        $skillRoot = Join-Path $fakeSkillsRoot $skillName
        New-Item -ItemType Directory -Path $skillRoot -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $skillRoot 'SKILL.md') -Value "# $skillName"
    }
    $fakeWorkspace = Join-Path $testRoot 'workspace'
    New-Item -ItemType Directory -Path $fakeWorkspace | Out-Null
    $copied = @(Copy-OpenCodeBenchmarkWorkspace `
            -PluginRoot $fakePluginRoot `
            -Workspace $fakeWorkspace `
            -VisibleSkills @('kept-skill'))
    if ($copied.Count -ne 1 -or $copied[0] -cne 'kept-skill' -or
        -not (Test-Path -LiteralPath (Join-Path $fakeWorkspace '.opencode\skills\kept-skill\SKILL.md')) -or
        (Test-Path -LiteralPath (Join-Path $fakeWorkspace '.opencode\skills\hidden-skill')) -or
        -not (Test-Path -LiteralPath (Join-Path $fakeWorkspace '.opencode\plugins\tool.js'))) {
        throw 'OpenCode workspace projection did not physically isolate visible skills.'
    }

    $sourceAuthPath = Join-Path $testRoot 'source-auth.json'
    $scopedAuthPath = Join-Path $testRoot 'isolated-data\opencode\auth.json'
    [ordered]@{
        openrouter = [ordered]@{ type = 'api'; key = 'test-openrouter-key' }
        opencode   = [ordered]@{ type = 'api'; key = 'test-opencode-key' }
        openai     = [ordered]@{
            type = 'oauth'; refresh = 'test-refresh-token'; access = 'test-access-token'
            expires = 4102444800000; accountId = 'test-account-id'
        }
    } |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $sourceAuthPath -Encoding UTF8

    [void](New-ProviderScopedOpenCodeAuthFile `
            -SourceAuthPath $sourceAuthPath `
            -Provider 'openrouter' `
            -DestinationPath $scopedAuthPath)
    $scopedAuth = Get-Content -LiteralPath $scopedAuthPath -Raw | ConvertFrom-Json
    if (@($scopedAuth.PSObject.Properties).Count -ne 1 -or
        $null -eq $scopedAuth.PSObject.Properties['openrouter'] -or
        [string]$scopedAuth.openrouter.key -cne 'test-openrouter-key') {
        throw 'Provider-scoped auth contained the wrong credentials.'
    }

    $scopedOauthPath = Join-Path $testRoot 'isolated-oauth-data\opencode\auth.json'
    [void](New-ProviderScopedOpenCodeAuthFile `
            -SourceAuthPath $sourceAuthPath `
            -Provider 'openai' `
            -DestinationPath $scopedOauthPath)
    $scopedOauth = Get-Content -LiteralPath $scopedOauthPath -Raw | ConvertFrom-Json
    if (@($scopedOauth.PSObject.Properties).Count -ne 1 -or
        $null -eq $scopedOauth.PSObject.Properties['openai'] -or
        [string]$scopedOauth.openai.type -cne 'oauth' -or
        [string]$scopedOauth.openai.access -cne 'test-access-token' -or
        $null -ne $scopedOauth.PSObject.Properties['openrouter']) {
        throw 'Provider-scoped OAuth contained the wrong credentials.'
    }

    Remove-BenchmarkSecretFile -Path $scopedAuthPath
    if (Test-Path -LiteralPath $scopedAuthPath) {
        throw 'Provider-scoped auth was not removed.'
    }
    Remove-BenchmarkSecretFile -Path $scopedOauthPath
    if (Test-Path -LiteralPath $scopedOauthPath) {
        throw 'Provider-scoped OAuth was not removed.'
    }

    Write-Host 'OpenCode benchmark common helper tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkCommonTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
