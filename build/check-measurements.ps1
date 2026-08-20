<#
.SYNOPSIS
    Tf2DemoSalvage: are OUR measurement runs still happening, and what did they score?

.DESCRIPTION
    **This reports Tf2DemoSalvage's runs and says so on every line.** That is the whole reason it
    exists separately from PokemonBattleJournal's build/check-measurement-boxes.ps1, which is a
    fine check of the BOXES and a misleading one about whose work is on them: it reports the single
    newest run directory, whichever project owns it, under a header that names neither. Three
    projects share these boxes, so on any given morning that line is a coin toss — and a
    Tf2DemoSalvage score appearing under a PokemonBattleJournal check reads as PBJ's number.

    The two are complementary rather than redundant. PBJ's answers "is the box alive"; this
    answers "did Tf2DemoSalvage's four slots run, and what came back".

    Runs are selected by the OWNERSHIP MARKER, never by a name glob. `~/measurements/` is shared
    and the directory names are `<stamp>-<sha>-<mode>`, so the obvious own-glob `*-fuzz` also
    matches a neighbour's `*-tcgdex-fuzz`, and the mirror-image exclusion misses our own older
    `-fuzz-container` runs. Every runner writes `.owner` beside its directory; that does not drift.

    What is checked, and why each one is silent by default:

      - RECENCY per mode. A cron job that fails the same way every night produces no output and
        no error — the log simply stops growing. Only an age check sees it.
      - The LOCK. `/tmp/measurement-box.lock` refuses rather than queues, so a job that starts
        inside another's window is skipped for that day and says so only in ~/cron-measure.log.
        A refused run is indistinguishable from a slow one until the age threshold trips, which
        is a day and a half later.
      - The SCORE. A run that completed and scored 0.00% is a broken measurement wearing a
        success, usually Stryker's MTP coverage-capture timeout.

.EXAMPLE
    pwsh build/check-measurements.ps1

.EXAMPLE
    pwsh build/check-measurements.ps1 -Review
    Adds the full crontab from each box and every Tf2DemoSalvage run of the last week, which is
    what a scheduling decision needs. A runtime that has grown into a neighbour's slot does not
    announce itself — it starts losing runs to a lock that refuses.
#>
[CmdletBinding()]
param(
    # Hours after which a daily run is called stale. 30 rather than 24 so a run that merely
    # started late, or a check run before that day's slot has come round, is not reported as a
    # failure. Anything past this has missed a whole scheduled slot.
    [int] $StaleHours = 30,

    # Adds the crontabs and a week of run history. For the weekly scheduling review.
    [switch] $Review
)

$ErrorActionPreference = 'Stop'

# The owner marker every Tf2DemoSalvage run writes. Must match RUN_OWNER in
# build/run-measurements.sh — if the two drift, this check reports NONE for everything, which
# at least fails loudly rather than reporting someone else's numbers as ours.
[string] $owner = 'tf2demosalvage'

# Booked slots, in box-local time. The boxes are set to America/New_York, so their crontabs ARE
# local and there is no conversion to do — a UTC crontab cannot express "9am local" across
# daylight saving, which is why the timezone was moved rather than the entries.
#
# **These strings are documentation, not the source of truth.** `crontab -l` on the box is, which
# is why -Review prints it rather than trusting this table.
[hashtable[]] $boxes = @(
    @{
        Alias = 'mutation-box'
        Slots = '9:00 core, 13:00 cli, 15:00 content, 15:20 audio - daily'
        Modes = @('core', 'cli', 'content', 'audio')
    },
    @{
        Alias = 'fuzz-box'
        Slots = '16:00 fuzz - daily'
        Modes = @('fuzz')
    }
)

Write-Host ''
Write-Host '================================================' -ForegroundColor Cyan
Write-Host ' Tf2DemoSalvage - measurement runs' -ForegroundColor Cyan
Write-Host " checked $(Get-Date -Format 'yyyy-MM-dd HH:mm') local" -ForegroundColor DarkGray
Write-Host '================================================' -ForegroundColor Cyan

[bool] $anyProblem = $false

# Separate from $anyProblem deliberately. A crash input is the box doing its job, not a fault in
# the run - but it must not vanish into a green summary either, because it is the only output
# here that asks for work.
[bool] $anyFinding = $false

foreach ($box in $boxes) {
    [string] $alias = $box.Alias
    [string[]] $modes = $box.Modes

    Write-Host ''
    Write-Host "=== $alias  [Tf2DemoSalvage]" -ForegroundColor Cyan
    Write-Host "    booked: $($box.Slots) (box local time)" -ForegroundColor DarkGray

    # One ssh per box, so a box that is gone fails attributably to that box rather than as a
    # parse error further down. BatchMode so a missing key fails now instead of hanging on a
    # prompt this script cannot answer.
    #
    # No parentheses and no backslashes in the remote script: ssh joins its arguments and the
    # remote shell re-parses the result, and both have broken this before.
    [string] $remote = @"
echo "tz:    `$(timedatectl show -p Timezone --value) - now `$(date '+%a %H:%M %Z')"
echo "disk:  `$(df -h / | awk 'NR==2 {print `$4" free of "`$2}')"
echo "cron:  `$(crontab -l 2>/dev/null | grep -c 'tf2-measurements') Tf2DemoSalvage entries of `$(crontab -l 2>/dev/null | grep -c -- '-measurements') total"
for mode in $($modes -join ' '); do
  newest=''
  for d in `$(ls -1dt ~/measurements/*/ 2>/dev/null); do
    [ -f "`${d}.owner" ] || continue
    [ "`$(cat "`${d}.owner" 2>/dev/null)" = "$owner" ] || continue
    case "`$(basename "`$d")" in
      *-tf2-`$mode) newest="`$d"; break ;;
    esac
  done
  if [ -z "`$newest" ]; then
    echo "MODE `$mode NONE - no run owned by $owner has ever completed"
  else
    age=`$(( ( `$(date +%s) - `$(stat -c %Y "`$newest") ) / 3600 ))
    if [ "`$mode" = fuzz ]; then
      # A fuzz run has no score. What it has is COVERAGE REACHED and whether it found
      # anything, and the second is the one that must never be inferred from silence:
      # libFuzzer's -artifact_prefix writes nothing through libfuzzer-dotnet, so a managed
      # exception can report a defect and lose the input. The harness saves the bytes itself.
      #
      # Targets come from the run's OWN log filenames rather than from a glob over
      # ~/findings-*, for the reason the shared box teaches everywhere else: a name glob
      # picks up a neighbour's directory the moment one of their targets is called
      # something of ours. The run names its own targets and cannot drift from them.
      #
      # maxdepth 1, because a triaged crash is moved into triaged/ once it has become a
      # regression fixture in the repository. Counting those makes the check nag daily about
      # work already finished, which is how a real finding gets scrolled past.
      targets=''
      crashes=0
      for log in "`$newest"/fuzz-*.log; do
        [ -f "`$log" ] || continue
        t=`$(basename "`$log" .log | sed 's/^fuzz-//')
        targets="`$targets `$t"
        n=`$(find "`$HOME/findings-`$t" -maxdepth 1 -name 'crash-*.bin' 2>/dev/null | wc -l)
        crashes=`$(( crashes + n ))
      done
      count=`$(echo `$targets | wc -w)
      echo "MODE `$mode `$age `$(basename "`$newest") | `$count targets, `$crashes untriaged crash inputs"
    else
      score=`$(grep -hoE 'final mutation score[^%]*%' "`$newest"/*.log 2>/dev/null | tail -1)
      [ -n "`$score" ] || score='NO SCORE LINE - the run did not finish scoring'
      echo "MODE `$mode `$age `$(basename "`$newest") | `$score"
    fi
  fi
done
if [ -f ~/cron-measure.log ]; then
  refused=`$(grep 'another measurement run holds' ~/cron-measure.log 2>/dev/null | grep -c tf2 | head -1)
  refused=`${refused:-0}
  if [ "`$refused" -gt 0 ]; then
    echo "REFUSED `$refused Tf2DemoSalvage runs were refused by the lock"
    grep 'another measurement run holds' ~/cron-measure.log | grep tf2 | tail -2 | sed 's/^/         /'
  fi
else
  echo "REFUSED-UNKNOWN no ~/cron-measure.log - cron has never produced output"
fi
"@

    [string[]] $out = & ssh -o BatchMode=yes -o ConnectTimeout=15 $alias $remote 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Unreachable is the loud case, not a warning. Oracle reclaims Always Free compute that
        # stays idle, and a reclaimed instance looks exactly like this: the box does not break,
        # it ceases to exist.
        Write-Host "    UNREACHABLE (ssh exit $LASTEXITCODE)" -ForegroundColor Red
        Write-Host "    $($out -join "`n    ")" -ForegroundColor DarkRed
        Write-Host '    Check the box still EXISTS in the Oracle console before debugging ssh.' -ForegroundColor Red
        $anyProblem = $true
        continue
    }

    foreach ($line in $out) {
        if ($line -match '^MODE\s+(\S+)\s+NONE\s+(.*)$') {
            Write-Host "    $($Matches[1].PadRight(8)) NONE - $($Matches[2])" -ForegroundColor Red
            $anyProblem = $true
            continue
        }

        if ($line -match '^MODE\s+(\S+)\s+(\d+)\s+(\S+)\s*\|\s*(.*)$') {
            [string] $mode = $Matches[1]
            [int] $age = [int] $Matches[2]
            [string] $run = $Matches[3]
            [string] $result = $Matches[4].Trim()

            [string] $label = "    $($mode.PadRight(8))"

            if ($age -gt $StaleHours) {
                Write-Host "$label STALE $age h - $run" -ForegroundColor Red
                Write-Host '             a job that fails the same way every night is silent by design;' -ForegroundColor Red
                Write-Host '             read ~/cron-measure.log on the box.' -ForegroundColor Red
                $anyProblem = $true
            }
            elseif ($result -match 'NO SCORE LINE') {
                # The directory exists, so the run started and something wrote to it; the score
                # line is what says it got to the end. Missing means it died partway, and a
                # partial mutation run is not a smaller measurement — it is no measurement.
                Write-Host "$label $age h  $result" -ForegroundColor Red
                Write-Host "             $run" -ForegroundColor DarkGray
                $anyProblem = $true
            }
            elseif ($result -match '([1-9]\d*) untriaged crash inputs') {
                $script:anyFinding = $true
                # Not a failure of the run - a FINDING, and the point of the box. Called out in
                # its own colour so it cannot be read as routine green output.
                Write-Host "$label $age h  $result" -ForegroundColor Yellow
                Write-Host '             a crash input is a regression fixture. Replay it.' -ForegroundColor Yellow
                Write-Host "             $run" -ForegroundColor DarkGray
            }
            elseif ($result -match '0\.00\s*%') {
                # A completed run scoring zero is a broken measurement wearing a success. The
                # usual cause is Stryker's MTP runner blowing its 180-second JSON-RPC limit,
                # which reports coverage of ZERO and then scores ~100% off timeouts - or 0.00%
                # under vstest, where the symptom inverts.
                Write-Host "$label $age h  $result" -ForegroundColor Red
                Write-Host '             0.00% is not a measurement. Check the coverage-capture line.' -ForegroundColor Red
                $anyProblem = $true
            }
            else {
                Write-Host "$label $age h  $result" -ForegroundColor Green
                Write-Host "             $run" -ForegroundColor DarkGray
            }
            continue
        }

        if ($line -match '^REFUSED\b') {
            # A refused run produced nothing at all, which the age check cannot tell from a job
            # that has simply not come round yet. The lock refuses rather than queues, so a
            # collision loses that day's run silently.
            Write-Host "    $($line -replace '^REFUSED\s*', 'REFUSED: ')" -ForegroundColor Red
            $anyProblem = $true
            continue
        }

        if ($line -match '^REFUSED-UNKNOWN') {
            Write-Host "    $($line -replace '^REFUSED-UNKNOWN\s*', 'NO LOG: ')" -ForegroundColor Red
            $anyProblem = $true
            continue
        }

        Write-Host "    $line"
    }
}

Write-Host ''
if ($anyProblem) {
    Write-Host 'Tf2DemoSalvage: something above needs attention.' -ForegroundColor Red
}
elseif ($anyFinding) {
    Write-Host 'Tf2DemoSalvage: slots all ran, and the fuzzer has untriaged crash inputs waiting.' -ForegroundColor Yellow
}
else {
    Write-Host 'Tf2DemoSalvage: all booked slots ran and scored.' -ForegroundColor Green
}

if (-not $Review) {
    exit 0
}

foreach ($box in $boxes) {
    [string] $alias = $box.Alias

    Write-Host ''
    Write-Host "=== WEEKLY REVIEW: $alias  [Tf2DemoSalvage]" -ForegroundColor Yellow

    # Wall time comes from the directory mtime minus the stamp in its own name: the name is
    # stamped at START with date -u and the directory is last written at the END, so the
    # difference is the real duration including the build and the git work. Stryker's own
    # reported time excludes both, and it is the real one that has to fit the slot.
    [string] $remote = @"
echo '--- full crontab (the only authority on what is booked) ---'
crontab -l 2>/dev/null | grep -v '^#' | grep . || echo 'NO CRONTAB'
echo ''
echo '--- Tf2DemoSalvage runs, last 7 days ---'
for d in `$(ls -1dt ~/measurements/*/ 2>/dev/null); do
  [ -f "`${d}.owner" ] || continue
  [ "`$(cat "`${d}.owner" 2>/dev/null)" = "$owner" ] || continue
  age=`$(( ( `$(date +%s) - `$(stat -c %Y "`$d") ) / 86400 ))
  [ "`$age" -gt 7 ] && continue
  name=`$(basename "`$d")
  started=`$(date -u -d "`$(echo "`$name" | cut -c1-8) `$(echo "`$name" | cut -c10-15 | sed 's/../&:/g;s/:`$//')" +%s 2>/dev/null)
  ended=`$(stat -c %Y "`$d")
  if [ -n "`$started" ]; then mins=`$(( (ended - started) / 60 )); else mins='?'; fi
  printf '%-46s %5s min  %s\n' "`$name" "`$mins" "`$(grep -hoE 'final mutation score[^%]*%' "`$d"/*.log 2>/dev/null | tail -1)"
done
"@

    [string[]] $out = & ssh -o BatchMode=yes -o ConnectTimeout=15 $alias $remote 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    UNREACHABLE (ssh exit $LASTEXITCODE)" -ForegroundColor Red
        continue
    }

    foreach ($line in $out) { Write-Host "    $line" }
}
