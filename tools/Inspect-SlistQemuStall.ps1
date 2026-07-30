[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int]$ObserveSeconds = 20,
    [ValidateSet('Normal', 'AboveNormal', 'High')]
    [string]$ProcessPriority = 'High'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$gate = [IO.Path]::GetFullPath($GateDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$esp = Join-Path $gate 'ESP'
$efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
$ovmf = 'C:\Program Files\qemu\share\edk2-x86_64-code.fd'
$varsTemplate = 'C:\Program Files\qemu\share\edk2-i386-vars.fd'
$qemu = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
if (-not (Test-Path -LiteralPath $efi)) { throw "EFI loader not found: $efi" }
if (-not (Test-Path -LiteralPath $qemu)) { throw "QEMU not found: $qemu" }
if (-not (Test-Path -LiteralPath $ovmf)) { throw "OVMF code not found: $ovmf" }
if (-not (Test-Path -LiteralPath $varsTemplate)) { throw "OVMF vars not found: $varsTemplate" }
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Preexisting QEMU process detected.' }

New-Item -ItemType Directory -Force -Path $output | Out-Null
$serial = Join-Path $output 'serial.log'
$stdout = Join-Path $output 'qemu.stdout.log'
$stderr = Join-Path $output 'qemu.stderr.log'
$monitorLog = Join-Path $output 'monitor.log'
$debugLog = Join-Path $output 'qemu.debug.log'
$events = Join-Path $output 'events.jsonl'
$vars = Join-Path $output 'ovmf-vars.fd'
Copy-Item -LiteralPath $varsTemplate -Destination $vars

function Write-Event([string]$name, [hashtable]$data = @{}) {
    $record = [ordered]@{ Utc = (Get-Date).ToUniversalTime().ToString('o'); Event = $name }
    foreach ($key in $data.Keys) { $record[$key] = $data[$key] }
    ($record | ConvertTo-Json -Compress -Depth 8) | Add-Content -LiteralPath $events -Encoding utf8
}

function Read-Serial {
    if (-not (Test-Path -LiteralPath $serial)) { return '' }
    $stream = [IO.File]::Open($serial, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.StreamReader($stream)
        try { return [string]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Send-Monitor([Net.Sockets.TcpClient]$client, [string]$command) {
    $stream = $client.GetStream()
    $bytes = [Text.Encoding]::ASCII.GetBytes($command + "`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    Start-Sleep -Milliseconds 300
    $result = New-Object Text.StringBuilder
    $buffer = New-Object byte[] 4096
    while ($stream.DataAvailable) {
        $read = $stream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        [void]$result.Append([Text.Encoding]::ASCII.GetString($buffer, 0, $read))
    }
    return $result.ToString()
}

$portListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$portListener.Start()
$port = ([Net.IPEndPoint]$portListener.LocalEndpoint).Port
$portListener.Stop()
$arguments = @(
    '-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M',
    '-drive', "if=pflash,format=raw,readonly=on,file=`"$ovmf`"",
    '-drive', "if=pflash,format=raw,file=`"$vars`"",
    '-drive', 'file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk',
    '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
    '-serial', "file:$serial", '-monitor', "tcp:127.0.0.1:$port,server,nowait",
    '-d', 'guest_errors,int', '-D', $debugLog,
    '-display', 'none', '-no-reboot', '-no-shutdown'
)
$process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
try { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::$ProcessPriority } catch { }
$startUtc = (Get-Date).ToUniversalTime()
Write-Event 'qemu-started' @{ Pid = $process.Id; StartUtc = $startUtc.ToString('o'); MonitorPort = $port; Arguments = ($arguments -join ' ') }

$monitor = $null
$connectedUtc = $null
$deadline = $startUtc.AddSeconds($ObserveSeconds)
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($null -eq $monitor) {
            try {
                $candidate = New-Object Net.Sockets.TcpClient
                $candidate.Connect('127.0.0.1', $port)
                $candidate.ReceiveTimeout = 1000
                $candidate.SendTimeout = 1000
                $monitor = $candidate
                $connectedUtc = (Get-Date).ToUniversalTime()
                Write-Event 'monitor-connected' @{ Utc = $connectedUtc.ToString('o') }
            } catch {
                if ($null -ne $candidate) { $candidate.Dispose() }
            }
        }
        if ($null -ne $monitor) {
            foreach ($command in @('info status', 'info cpus', 'info registers')) {
                try {
                    $response = Send-Monitor $monitor $command
                    Add-Content -LiteralPath $monitorLog -Value ("[$((Get-Date).ToUniversalTime().ToString('o'))] $command`r`n$response") -Encoding utf8
                    Write-Event 'monitor-query' @{ Command = $command; ResponseLength = $response.Length }
                } catch {
                    Write-Event 'monitor-query-failed' @{ Command = $command; Error = $_.Exception.Message }
                }
            }
        }
        $serialText = Read-Serial
        try { $cpuMs = $process.TotalProcessorTime.TotalMilliseconds } catch { $cpuMs = 0 }
        Write-Event 'sample' @{ Alive = (-not $process.HasExited); CpuMilliseconds = $cpuMs; SerialLength = $serialText.Length; LastMarker = if ($serialText.Length -gt 0) { ($serialText -split "`r?`n" | Where-Object { $_ -like 'GXOS_NET10:*' } | Select-Object -Last 1) } else { 'none' } }
        if ($process.HasExited) { break }
        Start-Sleep -Seconds 2
    }
} finally {
    if ($null -ne $monitor) { $monitor.Dispose() }
    try { $process.Refresh() } catch { }
    $alive = $false
    try { $alive = -not $process.HasExited } catch { }
    if ($alive) {
        Write-Event 'qemu-stop-requested' @{ Pid = $process.Id; Reason = 'diagnostic-window-complete' }
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    try { Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { }
    Write-Event 'finalized' @{ Pid = $process.Id; EndUtc = (Get-Date).ToUniversalTime().ToString('o'); SerialLength = (Read-Serial).Length; CleanupComplete = ($null -eq (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) }
}

Write-Output "SLIST_STALL_DIAGNOSTIC=$output"
