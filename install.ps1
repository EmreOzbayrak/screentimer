param([string]$SourceDirectory = $PSScriptRoot, [switch]$EnableStartup)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceDirectory)
$sourceExe = Join-Path $source 'ScreenTime.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Build first and supply -SourceDirectory dist, or run install.ps1 from an extracted Windows release.' }
$destination = Join-Path $env:LOCALAPPDATA 'ScreenTime\app'
$installedExe = Join-Path $destination 'ScreenTime.exe'
# Ask the single-instance app to save and exit, without terminating other processes.
$stopper = Start-Process -FilePath $sourceExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
if (-not $stopper.WaitForExit(10000)) { throw 'Could not request ScreenTime to exit.' }
$deadline = [DateTime]::UtcNow.AddSeconds(15)
do {
    $running = @(Get-Process -Name ScreenTime -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId })
    if ($running.Count -eq 0) { break }
    Start-Sleep -Milliseconds 300
} while ([DateTime]::UtcNow -lt $deadline)
if ($running.Count -gt 0) { throw 'ScreenTime is still running. Quit from its tray menu, then install again.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('ScreenTime.exe', 'ScreenTime.exe.config')) {
    $from = Join-Path $source $name
    $to = Join-Path $destination $name
    if (-not (Test-Path -LiteralPath $from)) { throw "Missing release file: $name" }
    if (-not $from.Equals($to, [StringComparison]::OrdinalIgnoreCase)) { Copy-Item -LiteralPath $from -Destination $to -Force }
}
$programs = [Environment]::GetFolderPath('Programs')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $programs 'ScreenTime.lnk'))
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $destination
$shortcut.Description = 'ScreenTime tray activity and session history'
$shortcut.Save()
$arguments = @('--background')
if ($EnableStartup) { $arguments += '--enable-startup' }
Start-Process -FilePath $installedExe -ArgumentList $arguments -WindowStyle Hidden
Write-Output "Installed and started: $installedExe"
Write-Output 'Existing history is retained in the parent ScreenTime folder.'
