Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$deniedPrivatePaths = @(
    'report.md'
    'suggestions.md'
)

function Invoke-GitLines {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = @(& git -C $repoRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $detail = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "Git command failed: git -C `"$repoRoot`" $($Arguments -join ' ')$([Environment]::NewLine)$detail"
    }

    return @($output | ForEach-Object { $_.ToString() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

try {
    $inspectedRoot = (Invoke-GitLines -Arguments @('rev-parse', '--show-toplevel')) | Select-Object -First 1
    if ([System.IO.Path]::GetFullPath($inspectedRoot) -ne $repoRoot) {
        throw "Git resolved repository root '$inspectedRoot', expected '$repoRoot'."
    }

    $trackedIgnored = @(Invoke-GitLines -Arguments @('ls-files', '-ci', '--exclude-standard'))
    $trackedPrivate = @(Invoke-GitLines -Arguments (@('ls-files', '--') + $deniedPrivatePaths))

    $violations = @($trackedIgnored + $trackedPrivate | Sort-Object -Unique)
    if ($violations.Count -gt 0) {
        [Console]::Error.WriteLine("[repository-hygiene] status=failed`nTracked ignored or denied private paths:`n$($violations -join [Environment]::NewLine)`nRemove them from the index with 'git rm --cached -- <path>' and keep private reports local and ignored.")
        exit 1
    }

    Write-Output '[repository-hygiene] status=clean tracked_ignored=0 denied_private_tracked=0'
}
catch {
    Write-Error "[repository-hygiene] status=error $($_.Exception.Message)"
    exit 1
}
