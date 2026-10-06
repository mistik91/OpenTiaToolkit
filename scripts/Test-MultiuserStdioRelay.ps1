[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $PSScriptRoot 'test-assets\StdioRelayFixture.ps1'
$checks = 0
foreach ($scenario in @('empty', 'unicode', 'binary-large', 'interactive', 'child-failure', 'shutdown-pending')) {
    $payload = [byte[]]::new(0)
    switch ($scenario) {
        'unicode' {
            $unicodeText = 'Flow ' + [char]0x2014 + ' ' + [char]0x6D41 + [char]0x91CF + ' ' + [char]::ConvertFromUtf32(0x1F30A)
            $payload = [Text.Encoding]::UTF8.GetBytes("{`"text`":`"$unicodeText`"}`n")
        }
        'binary-large' { $payload = [byte[]](@(0..255) * 4096) }
        'child-failure' { $payload = [Text.Encoding]::UTF8.GetBytes("{`"id`":7}`n") }
        'interactive' { $payload = [Text.Encoding]::UTF8.GetBytes("{`"id`":1,`"method`":`"initialize`"}`n") }
    }
    $childExit = if ($scenario -eq 'child-failure') { 7 } else { 0 }
    $expectedExit = if ($scenario -eq 'shutdown-pending') { 124 } else { $childExit }
    $delayExit = if ($scenario -eq 'shutdown-pending') { 4 } else { 0 }
    $completionPath = Join-Path ([IO.Path]::GetTempPath()) ('stdio-relay-proof-' + [Guid]::NewGuid().ToString('N') + '.txt')
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $info.Arguments = '-NoProfile -NonInteractive -File "{0}" -Relay -ExitCode {1} -DelayExitSeconds {2} -CompletionPath "{3}"' -f $fixture, $childExit, $delayExit, $completionPath
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    [void]$process.Start()
    $bytes = [IO.MemoryStream]::new()
    $outputTask = if ($scenario -eq 'interactive') { $process.StandardOutput.ReadLineAsync() } else { $process.StandardOutput.BaseStream.CopyToAsync($bytes) }
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $process.StandardInput.BaseStream.Write($payload, 0, $payload.Length)
        $process.StandardInput.BaseStream.Flush()
        if ($scenario -eq 'interactive') {
            if (-not $outputTask.Wait(10000)) { throw 'Small protocol messages must round-trip while stdin remains open.' }
            $interactiveBytes = [Text.Encoding]::UTF8.GetBytes($outputTask.Result + "`n")
            $bytes.Write($interactiveBytes, 0, $interactiveBytes.Length)
        }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(15000)) { throw "Relay fixture timed out: $scenario" }
        [void]$outputTask.GetAwaiter().GetResult()
        $stderr = $errorTask.GetAwaiter().GetResult()
        if ($scenario -eq 'shutdown-pending') {
            $childMatch = [regex]::Match($stderr, 'Child PID (\d+) is still running')
            if (-not $childMatch.Success -or $stderr -notmatch 'No process was forcibly terminated') { throw 'Pending shutdown was misreported.' }
            $completionDeadline = [DateTime]::UtcNow.AddSeconds(10)
            while (-not (Test-Path -LiteralPath $completionPath) -and [DateTime]::UtcNow -lt $completionDeadline) { Start-Sleep -Milliseconds 100 }
            if (-not (Test-Path -LiteralPath $completionPath) -or (Get-Content -LiteralPath $completionPath -Raw) -cne $childMatch.Groups[1].Value) { throw 'Delayed fake child did not finish its own bounded timer; it must not be killed by the relay.' }
        } elseif ($stderr.Trim() -cne 'stdio-fixture-stderr-only') { throw "Stderr leaked into the protocol: $scenario" }
        if ($process.ExitCode -ne $expectedExit -or [Convert]::ToBase64String($bytes.ToArray()) -cne [Convert]::ToBase64String($payload)) { throw "Protocol/exit integrity failed: $scenario" }
        $checks++
    } finally {
        $bytes.Dispose(); $process.Dispose()
        if (Test-Path -LiteralPath $completionPath) { Remove-Item -LiteralPath $completionPath -Force }
    }
}
Write-Host "PASS: $checks byte-exact stdio relay scenarios (EOF, UTF-8, 1MiB binary, interactive round-trip, nonzero child exit, bounded non-destructive shutdown); no credentials or TIA used."
