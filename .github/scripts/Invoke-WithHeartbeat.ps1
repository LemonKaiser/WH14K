param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(Mandatory = $true)]
    [string[]]$ArgumentList,

    [string]$Activity = "Command",

    [ValidateRange(5, 3600)]
    [int]$HeartbeatSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-ProcessArgument
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    if ($Argument -notmatch '[\s"]')
    {
        return $Argument
    }

    $escaped = $Argument -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

$processStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processStartInfo.FileName = $FilePath
$processStartInfo.WorkingDirectory = (Get-Location).Path
$processStartInfo.UseShellExecute = $false
$processStartInfo.RedirectStandardOutput = $true
$processStartInfo.RedirectStandardError = $true
$processStartInfo.CreateNoWindow = $true
$processStartInfo.Arguments = ($ArgumentList | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' '

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $processStartInfo

$stdoutQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$stderrQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()

$process.add_OutputDataReceived({
    if ($null -ne $_.Data)
    {
        $stdoutQueue.Enqueue($_.Data)
    }
})

$process.add_ErrorDataReceived({
    if ($null -ne $_.Data)
    {
        $stderrQueue.Enqueue($_.Data)
    }
})

function Flush-QueuedLines
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Concurrent.ConcurrentQueue[string]]$Queue,

        [switch]$IsError
    )

    $line = $null
    $flushed = $false

    while ($Queue.TryDequeue([ref]$line))
    {
        if ($IsError)
        {
            Write-Host $line
        }
        else
        {
            Write-Host $line
        }

        $flushed = $true
    }

    return $flushed
}

Write-Host "Starting $Activity..."

if (-not $process.Start())
{
    throw "Failed to start process '$FilePath'."
}

$process.BeginOutputReadLine()
$process.BeginErrorReadLine()

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$lastHeartbeatAt = [TimeSpan]::Zero

while (-not $process.HasExited)
{
    $hadOutput = $false

    if (Flush-QueuedLines -Queue $stdoutQueue)
    {
        $hadOutput = $true
    }

    if (Flush-QueuedLines -Queue $stderrQueue -IsError)
    {
        $hadOutput = $true
    }

    if (-not $hadOutput -and ($stopwatch.Elapsed - $lastHeartbeatAt).TotalSeconds -ge $HeartbeatSeconds)
    {
        Write-Host ("[{0}] still running after {1:hh\:mm\:ss}..." -f $Activity, $stopwatch.Elapsed)
        $lastHeartbeatAt = $stopwatch.Elapsed
    }

    Start-Sleep -Milliseconds 500
    $process.Refresh()
}

$process.WaitForExit()

$flushPending = $true
while ($flushPending)
{
    $flushPending = $false

    if (Flush-QueuedLines -Queue $stdoutQueue)
    {
        $flushPending = $true
    }

    if (Flush-QueuedLines -Queue $stderrQueue -IsError)
    {
        $flushPending = $true
    }
}

$stopwatch.Stop()

if ($process.ExitCode -eq 0)
{
    Write-Host ("{0} finished successfully in {1:hh\:mm\:ss}." -f $Activity, $stopwatch.Elapsed)
}
else
{
    Write-Host ("{0} failed with exit code {1} after {2:hh\:mm\:ss}." -f $Activity, $process.ExitCode, $stopwatch.Elapsed)
}

exit $process.ExitCode
