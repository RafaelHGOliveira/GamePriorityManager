# Launches the built GPM.App (unelevated) and reports whether it came up cleanly:
# window title, responding, and any .NET Runtime crash logged since launch.
# Leaves the app open for the user. Build first: dotnet build GamePriorityManager.sln -c Release
param([string]$Configuration = 'Release', [int]$TimeoutSeconds = 15)

$exe = Join-Path $PSScriptRoot "..\src\GPM.App\bin\$Configuration\net10.0-windows\GPM.App.exe" | Resolve-Path -ErrorAction Stop

# An elevated instance (an Apply worker, or one run as admin) can't be stopped from here; leave it.
foreach ($old in Get-Process GPM.App -ErrorAction SilentlyContinue) {
    try { Stop-Process -Id $old.Id -Force -ErrorAction Stop }
    catch { "skipped elevated GPM.App (pid $($old.Id))" }
}

$since = Get-Date
$p = Start-Process $exe -PassThru
$deadline = $since.AddSeconds($TimeoutSeconds)
do { Start-Sleep -Milliseconds 500; $p.Refresh() } until ($p.HasExited -or $p.MainWindowHandle -ne 0 -or (Get-Date) -gt $deadline)

$crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; StartTime = $since } -ErrorAction SilentlyContinue |
    Where-Object Message -match 'GPM'
if ($p.HasExited) { "FAIL: exited with code $($p.ExitCode)" }
elseif ($p.MainWindowHandle -eq 0) { "FAIL: no window after $TimeoutSeconds s (pid $($p.Id))" }
else { "OK: pid $($p.Id), title '$($p.MainWindowTitle)', responding $($p.Responding)" }
$crashes | ForEach-Object { "--- .NET Runtime $($_.TimeCreated)"; $_.Message }
if ($p.HasExited -or $p.MainWindowHandle -eq 0 -or $crashes) { exit 1 }
