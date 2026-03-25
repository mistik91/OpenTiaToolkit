[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [string]$PortalVersion = '21.0',

    [switch]$SkipVersionedWhitelist
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FileHashBase64 {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [Convert]::ToBase64String($sha.ComputeHash($stream))
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileTimestampUtc {
    param([string]$Path)

    return (Get-Item -LiteralPath $Path).LastWriteTimeUtc.ToString('yyyy/MM/dd HH:mm:ss.fff')
}

function Get-NextEntryName {
    param([string]$ParentRegistryPath)

    if (-not (Test-Path -LiteralPath $ParentRegistryPath)) {
        return 'Entry'
    }

    $existing = Get-ChildItem -LiteralPath $ParentRegistryPath -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty PSChildName

    if ($existing -notcontains 'Entry') {
        return 'Entry'
    }

    $index = 1
    while ($existing -contains "Entry ($index)") {
        $index++
    }

    return "Entry ($index)"
}

function Find-MatchingEntry {
    param(
        [string]$ParentRegistryPath,
        [string]$ExecutablePath,
        [string]$DateModified,
        [string]$FileHash
    )

    if (-not (Test-Path -LiteralPath $ParentRegistryPath)) {
        return $null
    }

    foreach ($entry in Get-ChildItem -LiteralPath $ParentRegistryPath -ErrorAction SilentlyContinue) {
        $props = Get-ItemProperty -LiteralPath $entry.PSPath -ErrorAction SilentlyContinue
        if ($null -eq $props) {
            continue
        }

        if ($props.Path -eq $ExecutablePath -and
            $props.DateModified -eq $DateModified -and
            $props.FileHash -eq $FileHash) {
            return $entry.PSChildName
        }
    }

    return $null
}

function Ensure-RegistryEntry {
    param(
        [string]$ParentRegistryPath,
        [string]$ExecutablePath,
        [string]$DateModified,
        [string]$FileHash
    )

    New-Item -Path $ParentRegistryPath -Force | Out-Null

    $existing = Find-MatchingEntry `
        -ParentRegistryPath $ParentRegistryPath `
        -ExecutablePath $ExecutablePath `
        -DateModified $DateModified `
        -FileHash $FileHash

    if ($existing) {
        return [pscustomobject]@{
            Created   = $false
            EntryName = $existing
            Path      = (Join-Path $ParentRegistryPath $existing)
        }
    }

    $entryName = Get-NextEntryName -ParentRegistryPath $ParentRegistryPath
    $entryPath = Join-Path $ParentRegistryPath $entryName

    New-Item -Path $entryPath -Force | Out-Null
    New-ItemProperty -LiteralPath $entryPath -Name Path -Value $ExecutablePath -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $entryPath -Name DateModified -Value $DateModified -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $entryPath -Name FileHash -Value $FileHash -PropertyType String -Force | Out-Null

    return [pscustomobject]@{
        Created   = $true
        EntryName = $entryName
        Path      = $entryPath
    }
}

$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

if (-not (Test-Path -LiteralPath $resolvedExecutablePath -PathType Leaf)) {
    throw "Executable not found: $ExecutablePath"
}

if (-not (Test-IsAdministrator)) {
    throw 'Administrator rights are required to write Siemens Openness allowlist entries under HKLM.'
}

$fileName = Split-Path -Leaf $resolvedExecutablePath
$dateModified = Get-FileTimestampUtc -Path $resolvedExecutablePath
$fileHash = Get-FileHashBase64 -Path $resolvedExecutablePath

$targets = @(
    @{
        Name = 'AllowList'
        Path = "HKLM:\SOFTWARE\Siemens\Automation\Openness\AllowList\$fileName"
    }
)

if (-not $SkipVersionedWhitelist) {
    $targets += @{
        Name = 'VersionedWhitelist'
        Path = "HKLM:\SOFTWARE\Siemens\Automation\Openness\$PortalVersion\Whitelist\$fileName"
    }
}

$results = @()

foreach ($target in $targets) {
    if (-not $PSCmdlet.ShouldProcess($target.Path, "Register $fileName for TIA Portal Openness")) {
        continue
    }

    $result = Ensure-RegistryEntry `
        -ParentRegistryPath $target.Path `
        -ExecutablePath $resolvedExecutablePath `
        -DateModified $dateModified `
        -FileHash $fileHash

    $results += [pscustomobject]@{
        Target    = $target.Name
        Registry  = $target.Path
        EntryName = $result.EntryName
        Created   = $result.Created
        Path      = $resolvedExecutablePath
        DateUtc   = $dateModified
        FileHash  = $fileHash
    }
}

$results
