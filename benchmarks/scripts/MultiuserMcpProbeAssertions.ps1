Set-StrictMode -Version Latest

function Assert-MultiuserMcpProbeResult {
    param([Parameter(Mandatory = $true)]$Result, [Parameter(Mandatory = $true)]$Expect)
    foreach ($expected in $Expect.PSObject.Properties) {
        $value = $Result
        foreach ($segment in $expected.Name.Split('.')) {
            if ($null -eq $value -or $null -eq $value.PSObject.Properties[$segment]) {
                throw "Missing required probe result path: $($expected.Name)"
            }
            $value = $value.PSObject.Properties[$segment].Value
        }
        # Preserve JSON types: the string "true" must not satisfy a boolean proof.
        $actualJson = ConvertTo-Json -InputObject $value -Depth 60 -Compress
        $expectedJson = ConvertTo-Json -InputObject $expected.Value -Depth 60 -Compress
        if ($actualJson -cne $expectedJson) { throw "Probe result does not match required value: $($expected.Name)" }
    }
}
