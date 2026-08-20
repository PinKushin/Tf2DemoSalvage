<#
.SYNOPSIS
    Installs (or removes) the daily Windows scheduled task that checks Tf2DemoSalvage's
    measurement runs.

.DESCRIPTION
    **A Claude session cron is not a daily routine.** It lives in one session, dies with it, and
    expires after seven days — so a check that matters has to be owned by the operating system.
    This registers a real scheduled task, which survives reboots, sessions and this repository
    being closed for a month.

    The two are complementary and both are worth having:

      - THIS TASK is the one that keeps running. It writes build output to a log so a run that
        happened while nobody was watching can still be read afterwards.
      - A CronCreate job inside a live Claude session reports into the conversation, which is
        useful while working and worthless overnight.

    WHEN IT RUNS, and why 08:30 rather than the obvious morning slot.
    Tf2DemoSalvage's slots are 09:00 core, 13:00 cli, 15:00 content, 15:20 audio on mutation-box
    and 16:00 fuzz on fuzz-box, all box-local (America/New_York, which is also this machine's).
    08:30 sits after the last of the previous day's runs and before the first of today's, so every
    slot is reported exactly once, complete, and inside the 30-hour staleness threshold. Checking
    at 09:30 instead would catch that morning's core run mid-flight and report yesterday's, which
    is the same number arriving a day late.

    The minute is 08:29 rather than 08:30 deliberately: everything on a machine fires on the
    half hour.

.PARAMETER Remove
    Unregisters the task instead of installing it.

.EXAMPLE
    pwsh build/install-measurement-check-task.ps1

.EXAMPLE
    pwsh build/install-measurement-check-task.ps1 -Remove
#>
[CmdletBinding()]
param(
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

[string] $taskName = 'Tf2DemoSalvage measurement check'
[string] $repo = Split-Path -Parent $PSScriptRoot
[string] $script = Join-Path $repo 'build/check-measurements.ps1'
[string] $logDir = Join-Path $env:LOCALAPPDATA 'Tf2DemoSalvage'
[string] $log = Join-Path $logDir 'measurement-check.log'

if ($Remove) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Removed scheduled task '$taskName'." -ForegroundColor Yellow
    }
    else {
        Write-Host "No scheduled task named '$taskName'." -ForegroundColor DarkGray
    }

    exit 0
}

if (-not (Test-Path $script)) {
    throw "check-measurements.ps1 is not at $script - the task would register and fail silently."
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# pwsh, not powershell.exe: the check script uses PowerShell 7 syntax, and Windows PowerShell
# would fail at parse time — which the scheduler reports as a non-zero exit code and nothing else.
[string] $pwsh = (Get-Command pwsh -ErrorAction Stop).Source

# -NoProfile so a profile that writes to the console, prompts, or changes the working directory
# cannot affect a run nobody is watching. Output is appended with a separator, because the
# interesting question after a fortnight is "when did this stop working", and a log that is
# overwritten each day cannot answer it.
[string] $command =
    "& { " +
    "Write-Output ('=== ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')); " +
    "& '$script'; " +
    "} *>&1 | Add-Content -LiteralPath '$log'"

$action = New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -Command `"$command`"" -WorkingDirectory $repo

# 08:29 daily. See the .DESCRIPTION for why this time and not the morning slot.
$trigger = New-ScheduledTaskTrigger -Daily -At '08:29'

# StartWhenAvailable is the one that matters on a desktop: a machine asleep at 08:29 would
# otherwise simply skip the day, silently, which is the exact failure this check exists to catch.
# DontStopIfGoingOnBatteries for the same reason — a laptop on battery is still a laptop.
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) `
    -MultipleInstances IgnoreNew

# Interactive rather than a service account: the ssh keys and ~/.ssh/config that reach the boxes
# belong to this user, and a task running as SYSTEM would fail to resolve the host aliases.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'Checks that Tf2DemoSalvage''s mutation and fuzz slots on the Oracle measurement boxes actually ran, and what they scored. Appends to %LOCALAPPDATA%\Tf2DemoSalvage\measurement-check.log.' `
    -Force | Out-Null

Write-Host ''
Write-Host "Registered '$taskName' - daily at 08:29 local." -ForegroundColor Green
Write-Host "  log: $log" -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Run it once now to prove it works, rather than waiting until tomorrow:' -ForegroundColor Cyan
Write-Host "  Start-ScheduledTask -TaskName '$taskName'" -ForegroundColor Cyan
