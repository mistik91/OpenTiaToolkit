[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$InstanceName = 'OTT35_Bench',
    [string]$ArticleNumber = '6ES7 511-1AL03-0AB0',
    [string]$ApiPath = 'C:\Program Files (x86)\Common Files\Siemens\PLCSIMADV\API\8.0\Siemens.Simatic.Simulation.Runtime.Api.x64.dll',
    [ValidateRange(60, 21600)]
    [int]$LifetimeSeconds = 14400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Use a 64-bit PowerShell process.' }
if ($InstanceName -cne 'OTT35_Bench') { throw 'This fixture helper is restricted to OTT35_Bench.' }
if ($ArticleNumber -cne '6ES7 511-1AL03-0AB0') { throw 'This fixture helper is restricted to the observed baseline CPU article.' }
$resolvedApi = (Resolve-Path -LiteralPath $ApiPath).Path
$assembly = [Reflection.Assembly]::LoadFrom($resolvedApi)
$managerType = $assembly.GetType('Siemens.Simatic.Simulation.Runtime.SimulationRuntimeManager', $true)
$manager = [Activator]::CreateInstance($managerType)
try {
    if (-not $managerType.GetProperty('IsInitialized').GetValue($manager) -or
        -not $managerType.GetProperty('IsRuntimeManagerAvailable').GetValue($manager)) {
        throw 'The installed PLCSIM Advanced Runtime Manager is unavailable.'
    }
    $registered = @($managerType.GetProperty('RegisteredInstanceInfo').GetValue($manager))
    if ($registered.Count -ne 0) {
        throw 'Refusing setup while any simulator is registered; inspect its ownership before continuing.'
    }
    if (-not $PSCmdlet.ShouldProcess($InstanceName, "Create and power on disposable PLCSIM CPU $ArticleNumber")) { return }
    $register = $managerType.GetMethod('RegisterInstance', [Type[]]@([string], [string]))
    $instance = $register.Invoke($manager, @($ArticleNumber, $InstanceName))
    $instanceType = $instance.GetType()
    $powerResult = $instanceType.GetMethod('PowerOn', [Type[]]@([uint32])).Invoke($instance, @([uint32]60000))
    [pscustomobject]@{
        ApiPath = $resolvedApi
        Id = $instanceType.GetProperty('ID').GetValue($instance)
        Name = $instanceType.GetProperty('Name').GetValue($instance)
        ArticleNumber = $instanceType.GetProperty('ArticleNumber').GetValue($instance)
        CpuType = [string]$instanceType.GetProperty('CPUType').GetValue($instance)
        OperatingState = [string]$instanceType.GetProperty('OperatingState').GetValue($instance)
        ControllerName = $instanceType.GetProperty('ControllerName').GetValue($instance)
        PowerResult = [string]$powerResult
    } | ConvertTo-Json
    if ([string]$instanceType.GetProperty('OperatingState').GetValue($instance) -notin @('Stop', 'Run')) {
        throw 'The new simulator is not powered. Retain and inspect this exact instance before retrying setup.'
    }
    # Runtime-manager shutdown releases instances registered by that manager.
    # Keep this owned process alive until its bounded lifetime or explicit teardown.
    Write-Host "Simulator owner PID $PID; keeping OTT35_Bench registered for at most $LifetimeSeconds seconds."
    $deadline = [DateTime]::UtcNow.AddSeconds($LifetimeSeconds)
    while ([DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
}
finally {
    $shutdown = $managerType.GetMethod('Shutdown', [Type[]]@())
    if ($null -ne $shutdown) { [void]$shutdown.Invoke($manager, @()) }
}
