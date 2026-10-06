param([switch]$Relay, [int]$ExitCode = 0, [int]$DelayExitSeconds = 0, [string]$CompletionPath)
$ErrorActionPreference = 'Stop'
if ($Relay) {
    Add-Type -Path (Join-Path $PSScriptRoot '..\MultiuserStdioRelay.cs') | Out-Null
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $info.Arguments = '-NoProfile -NonInteractive -File "{0}" -ExitCode {1} -DelayExitSeconds {2} -CompletionPath "{3}"' -f $PSCommandPath, $ExitCode, $DelayExitSeconds, $CompletionPath
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $child = [Diagnostics.Process]::new(); $child.StartInfo = $info
    [void]$child.Start()
    $deadline = if ($DelayExitSeconds -gt 0) { 1 } else { 5 }
    try { exit [OpenTiaToolkit.Harness.MultiuserStdioRelay]::Forward($child, $deadline) } finally { $child.Dispose() }
}
[Console]::Error.WriteLine('stdio-fixture-stderr-only')
$inputStream = [Console]::OpenStandardInput()
$outputStream = [Console]::OpenStandardOutput()
$buffer = [byte[]]::new(8192)
while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
    $outputStream.Write($buffer, 0, $count); $outputStream.Flush()
}
if ($DelayExitSeconds -gt 0) { Start-Sleep -Seconds $DelayExitSeconds }
if (-not [string]::IsNullOrWhiteSpace($CompletionPath)) { [IO.File]::WriteAllText($CompletionPath, [string]$PID) }
exit $ExitCode
