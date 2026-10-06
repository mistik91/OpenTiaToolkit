[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$SolutionPath = '.\OpenTiaToolkit.sln',
    [string]$SigningCertificateThumbprint = $env:OPEN_TIA_SIGNING_CERT_THUMBPRINT,
    [switch]$SignBinaries,
    [switch]$RequireSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$payloadRoot = (Resolve-Path -LiteralPath $InputRoot).Path
$metadataRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $metadataRoot | Out-Null

$payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object {
    -not $_.FullName.StartsWith($metadataRoot, [StringComparison]::OrdinalIgnoreCase)
})

if ($SignBinaries) {
    $signable = @($payloadFiles | Where-Object {
        $_.Name -like 'OpenTiaToolkit*.exe' -or $_.Name -like 'OpenTiaToolkit*.dll'
    })
    if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        if ($RequireSigning) { throw 'Release signing is required but OPEN_TIA_SIGNING_CERT_THUMBPRINT is not set.' }
        Write-Warning 'No signing certificate thumbprint was supplied; binaries remain unsigned.'
    }
    else {
        $certificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $SigningCertificateThumbprint) -ErrorAction SilentlyContinue
        if (-not $certificate) {
            $certificate = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $SigningCertificateThumbprint) -ErrorAction SilentlyContinue
        }
        if (-not $certificate) { throw "Signing certificate '$SigningCertificateThumbprint' was not found." }
        foreach ($file in $signable) {
            $signature = Set-AuthenticodeSignature -LiteralPath $file.FullName -Certificate $certificate -HashAlgorithm SHA256
            if ($signature.Status -ne 'Valid') { throw "Failed to sign '$($file.FullName)': $($signature.StatusMessage)" }
        }
        $payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object {
            -not $_.FullName.StartsWith($metadataRoot, [StringComparison]::OrdinalIgnoreCase)
        })
    }
}

if ($RequireSigning) {
    foreach ($file in @($payloadFiles | Where-Object { $_.Name -like 'OpenTiaToolkit*.exe' })) {
        if ((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid') {
            throw "Required Authenticode signature is missing from '$($file.FullName)'."
        }
    }
}

$checksums = foreach ($file in $payloadFiles | Sort-Object FullName) {
    $relative = $file.FullName.Substring($payloadRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
$checksums | Set-Content -LiteralPath (Join-Path $metadataRoot 'SHA256SUMS.txt') -Encoding UTF8

$solution = [System.IO.Path]::GetFullPath($SolutionPath)
$packageJson = & dotnet list $solution package --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'Failed to obtain dependency inventory for the SBOM.' }
$inventory = ($packageJson -join [Environment]::NewLine) | ConvertFrom-Json
$packages = @{}
foreach ($project in $inventory.projects) {
    foreach ($framework in $project.frameworks) {
        $topLevel = if ($framework.PSObject.Properties['topLevelPackages']) { @($framework.topLevelPackages) } else { @() }
        $transitive = if ($framework.PSObject.Properties['transitivePackages']) { @($framework.transitivePackages) } else { @() }
        foreach ($package in @($topLevel) + @($transitive)) {
            if ($null -eq $package -or [string]::IsNullOrWhiteSpace($package.id)) { continue }
            $key = "$($package.id)@$($package.resolvedVersion)"
            $packages[$key] = [ordered]@{
                SPDXID = 'SPDXRef-Package-' + ($key -replace '[^A-Za-z0-9.-]', '-')
                name = $package.id
                versionInfo = $package.resolvedVersion
                downloadLocation = 'https://api.nuget.org/v3/index.json'
                filesAnalyzed = $false
                licenseConcluded = 'NOASSERTION'
                licenseDeclared = 'NOASSERTION'
            }
        }
    }
}
$commit = (& git -C (Split-Path -Parent $solution) rev-parse HEAD 2>$null)
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = 'OpenTiaToolkit'
    documentNamespace = 'https://github.com/mistik91/OpenTiaToolkit/sbom/' + [Guid]::NewGuid().ToString('N')
    creationInfo = [ordered]@{ created = [DateTime]::UtcNow.ToString('o'); creators = @('Tool: OpenTiaToolkit Generate-ReleaseArtifacts.ps1') }
    packages = @($packages.Values)
}
$sbom | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $metadataRoot 'sbom.spdx.json') -Encoding UTF8

$provenance = [ordered]@{
    _type = 'https://in-toto.io/Statement/v1'
    predicateType = 'https://slsa.dev/provenance/v1'
    subject = @($payloadFiles | ForEach-Object {
        [ordered]@{ name = $_.FullName.Substring($payloadRoot.Length).TrimStart('\', '/').Replace('\', '/'); digest = [ordered]@{ sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
    })
    predicate = [ordered]@{
        buildDefinition = [ordered]@{ buildType = 'https://github.com/mistik91/OpenTiaToolkit/windows-release@v1'; externalParameters = [ordered]@{ configuration = 'Release'; commit = $commit } }
        runDetails = [ordered]@{ builder = [ordered]@{ id = 'OpenTiaToolkit/Generate-ReleaseArtifacts.ps1' }; metadata = [ordered]@{ invocationId = [Guid]::NewGuid().ToString('N'); startedOn = [DateTime]::UtcNow.ToString('o') } }
    }
}
$provenance | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $metadataRoot 'provenance.intoto.json') -Encoding UTF8

[pscustomobject]@{ Files = $payloadFiles.Count; Packages = $packages.Count; OutputRoot = $metadataRoot }
