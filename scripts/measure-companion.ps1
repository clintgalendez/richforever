<#
.SYNOPSIS
  Samples the RichForever companion's CPU and memory use over a period.

.EXAMPLE
  ./scripts/measure-companion.ps1 -Seconds 120
  Run while RichForever is running in the state you want to measure (waiting for WoW, or in game).
#>
param(
    [int]$Seconds = 60,
    [int]$SampleSeconds = 5,
    [string]$ProcessName = "RichForever"
)
$ErrorActionPreference = "Stop"
$process = Get-Process -Name $ProcessName | Select-Object -First 1
$cores = [Environment]::ProcessorCount
$startCpu = $process.TotalProcessorTime
$startTime = Get-Date
$private = @(); $working = @()
$deadline = $startTime.AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $SampleSeconds
    $process.Refresh()
    $private += $process.PrivateMemorySize64 / 1MB
    $working += $process.WorkingSet64 / 1MB
}
$process.Refresh()
$elapsed = ((Get-Date) - $startTime).TotalMilliseconds
$cpu = ($process.TotalProcessorTime - $startCpu).TotalMilliseconds
[pscustomobject]@{
    Seconds            = [math]::Round($elapsed / 1000)
    CpuPercentAverage  = [math]::Round($cpu / $elapsed / $cores * 100, 3)
    CpuMilliseconds    = [math]::Round($cpu)
    PrivateMBAverage   = [math]::Round(($private | Measure-Object -Average).Average, 1)
    PrivateMBMax       = [math]::Round(($private | Measure-Object -Maximum).Maximum, 1)
    WorkingSetMBAverage = [math]::Round(($working | Measure-Object -Average).Average, 1)
    Threads            = $process.Threads.Count
    Handles            = $process.HandleCount
    Cores              = $cores
} | Format-List
