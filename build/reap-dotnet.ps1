<#
.SYNOPSIS
Ends the dotnet test and build processes this repository left orphaned, then shuts down the SDK's
build servers.

.DESCRIPTION
**Why this exists, measured 2026-09-12.** `build/gate.sh` already runs `dotnet build-server shutdown`
from a `trap ... EXIT`, and that covers exactly one of three ways processes get left behind:

  - A gate that COMPLETES: covered by the trap.
  - A gate that is KILLED (a background task stopped, a terminal closed): not covered. On Windows the
    shell is terminated outright, the trap never runs, and the `dotnet test` tree underneath is
    orphaned. Measured: a `testhost` survived a stopped gate and locked `Tf2DemoSalvage.Animation.dll`,
    failing the next build with MSB3021. `build-server shutdown` would not have helped either: it
    stops MSBuild's reusable nodes and the compiler server, never a test host.
  - A plain `dotnet build`/`test`/`run` outside the gate: no trap at all. Measured: a `VBCSCompiler`
    with no parent, ten minutes after a standalone build.

**So this is run at the START of a gate, not only at the end.** A killed run cannot clean up after
itself; the next run can.

**Orphans are found by parentage, never by name alone.** `pkill -f` is ruled out for the reason the
gate's own comment gives — it matches the shell running it — and a name match would also kill a gate
running concurrently in another terminal. A process is reaped only when it is orphaned: its parent is
gone, or the process holding its parent's id started AFTER it did, because Windows reuses ids and a
reused id is not a parent. A live run's processes all have living parents, so it is never touched.

What is reaped:

  - An orphaned `dotnet`, `testhost` or vstest process whose tree mentions this repository's path
    anywhere — the root or any descendant — stopped descendants first. The path test is on the whole
    tree because `dotnet test tests/X` carries only a relative path; its test host carries the full one.
  - An orphaned MSBuild worker node started with `/nodeReuse:false`. Such a node exists only for the
    build that launched it and should die with it, so an orphaned one is unambiguous whichever
    repository it came from.

Then `dotnet build-server shutdown`, for the nodes and compiler server that outlive their build by
design, which is the graceful way to end them.

.PARAMETER Repository
The repository whose orphans to reap. Defaults to the directory above this script.

.PARAMETER SkipBuildServers
Reap orphans only; leave `dotnet build-server shutdown` out.

.EXAMPLE
pwsh build/reap-dotnet.ps1

.EXAMPLE
pwsh build/reap-dotnet.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Repository = (Split-Path -Parent $PSScriptRoot),
    [switch] $SkipBuildServers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryPath = [IO.Path]::GetFullPath($Repository).TrimEnd('\', '/')

$processes = @(Get-CimInstance Win32_Process)

$byId = @{}
$children = @{}

foreach ($process in $processes) {
    $byId[[int]$process.ProcessId] = $process
}

foreach ($process in $processes) {
    $parentId = [int]$process.ParentProcessId

    if (-not $children.ContainsKey($parentId)) {
        $children[$parentId] = [System.Collections.Generic.List[object]]::new()
    }

    $children[$parentId].Add($process)
}

function Test-Orphaned([object] $process) {
    $parent = $byId[[int]$process.ParentProcessId]

    if ($null -eq $parent) {
        return $true
    }

    # A reused id: the process now holding it started after this one, so it cannot be the parent.
    return $parent.CreationDate -gt $process.CreationDate
}

function Get-Tree([object] $root) {
    $tree = [System.Collections.Generic.List[object]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    $pending.Push($root)

    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        $tree.Add($current)

        $below = $children[[int]$current.ProcessId]

        if ($null -ne $below) {
            foreach ($child in $below) {
                # A child that started before its "parent" is a reused id again, not a descendant.
                if ($child.CreationDate -ge $current.CreationDate) {
                    $pending.Push($child)
                }
            }
        }
    }

    # The comma keeps the list a list: PowerShell unrolls a collection on return, so a one-process
    # tree would otherwise arrive as the bare process and fail Stop-Tree's typed parameter.
    return , $tree
}

function Get-CommandLine([object] $process) {
    if ($null -eq $process.CommandLine) {
        return ''
    }

    return [string]$process.CommandLine
}

function Stop-Tree([System.Collections.Generic.List[object]] $tree, [string] $reason) {
    # Deepest first, so no process outlives the one that would otherwise be left to adopt it.
    for ($index = $tree.Count - 1; $index -ge 0; $index--) {
        $member = $tree[$index]

        if ([int]$member.ProcessId -eq $PID) {
            continue
        }

        $label = "$($member.Name) $($member.ProcessId)"

        if (-not $PSCmdlet.ShouldProcess($label, "Stop ($reason)")) {
            continue
        }

        try {
            Stop-Process -Id ([int]$member.ProcessId) -Force -ErrorAction Stop
            Write-Output "reaped $label ($reason)"
        }
        catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
            # Already gone between the census and now, which is the outcome wanted.
            Write-Verbose "$label had already exited: $($_.Exception.Message)"
        }
        catch {
            Write-Warning "could not stop ${label}: $($_.Exception.Message)"
        }
    }
}

$rootNames = @('dotnet.exe', 'testhost.exe', 'testhost.x86.exe')
$handled = [System.Collections.Generic.HashSet[int]]::new()
$reapedTrees = 0

foreach ($process in $processes) {
    if ($rootNames -notcontains $process.Name -or $handled.Contains([int]$process.ProcessId)) {
        continue
    }

    if (-not (Test-Orphaned $process)) {
        continue
    }

    $tree = Get-Tree $process

    foreach ($member in $tree) {
        [void]$handled.Add([int]$member.ProcessId)
    }

    $commandLine = Get-CommandLine $process

    $isOrphanedNonReuseNode =
        $commandLine.Contains('MSBuild.dll', [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains('/nodemode:', [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains('/nodeReuse:false', [StringComparison]::OrdinalIgnoreCase)

    if ($isOrphanedNonReuseNode) {
        Stop-Tree $tree 'orphaned MSBuild node without reuse'
        $reapedTrees++
        continue
    }

    $mentionsRepository = $false

    foreach ($member in $tree) {
        if ((Get-CommandLine $member).Contains($repositoryPath, [StringComparison]::OrdinalIgnoreCase)) {
            $mentionsRepository = $true
            break
        }
    }

    if ($mentionsRepository) {
        Stop-Tree $tree "orphaned tree under $repositoryPath"
        $reapedTrees++
    }
}

if ($reapedTrees -eq 0) {
    Write-Output 'reap-dotnet: no orphaned dotnet processes'
}

if (-not $SkipBuildServers -and $PSCmdlet.ShouldProcess('dotnet build servers', 'Shut down')) {
    & dotnet build-server shutdown *> $null

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "dotnet build-server shutdown exited $LASTEXITCODE"
    }
    else {
        Write-Output 'reap-dotnet: build servers shut down'
    }
}
