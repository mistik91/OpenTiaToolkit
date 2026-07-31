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
    $keyProperty = $credential.PSObject.Properties['key']
    if ($null -eq $typeProperty -or [string]$typeProperty.Value -ne 'api' -or
        $null -eq $keyProperty -or [string]::IsNullOrWhiteSpace([string]$keyProperty.Value)) {
        throw "OpenCode credential for provider '$Provider' is not a supported API-key credential."
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
