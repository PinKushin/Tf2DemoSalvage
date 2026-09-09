#requires -Version 7
<#
.SYNOPSIS
    Captures one frame of a demo from REAL TF2, at a chosen tick, for golden comparison (B161).

.DESCRIPTION
    This is a TOOL, not a shipped feature - the owner's own framing. It lives beside the corpus
    scripts, is allowed to be slow and Windows-only, and nothing about it goes in the viewer.

    Why it exists: several documents already assume this instrument exists. `findings/13` and the
    roadmap's upsampling gate both say the project validates itself "against captures of the real
    game", and until now that meant somebody alt-tabbing. A blessed reference is what makes an
    open-ended visual claim assertable at all (docs/memory/a-picture-is-assertable.md).

    HOW THE PIECES FIT, all ordinary console commands:

      play the demo            +playdemo <name>
      seek to our tick         demo_gototick <tick>
      deterministic steps      host_framerate <n>   - a fixed step per frame, not wall clock
      write one frame          jpeg
      fixed geometry           -w / -h / -windowed / -novid

    `host_framerate` is the one that matters most. Without it the capture is at the mercy of frame
    timing, and the same command sequence lands on different ticks between runs - which would make
    every comparison flaky for a reason that has nothing to do with rendering.

    TWO CONSTRAINTS THE OWNER NAMED. `playdemo` resolves inside the game's own filesystem rather
    than taking an arbitrary path, so the demo is copied into `tf/` and removed afterwards. And
    there is no way to open a demo except by issuing the command - no file association, no browser;
    `demoui` controls a demo already running. A launch option IS a console command run at startup,
    so `+exec` of a generated config supplies the rest.

    THE TIMING IS THE PART TO GET RIGHT, NOT THE SYNTAX, and the first design got it wrong in a way
    worth recording. A `wait`-chain in the exec'd config looked robust - `wait` counts frames, and
    frames do not advance while the engine loads, so the chain should resume once playback is real.
    It never fired. **`playdemo` triggers a level load, and a level load FLUSHES the command
    buffer**, taking the pending chain with it. The config ran (`Playing demo from ...` is in the
    log, and only our config could have issued it); everything queued behind the load was gone.

    So commands are delivered from OUTSIDE instead. `-hijack` hands a command line to the instance
    that is already running, which is immune to the buffer being flushed because it re-enters the
    console after the load rather than before it. The script drives the game in steps, polling
    `tf/console.log` to know when each one has actually happened rather than sleeping and hoping.

    WHAT PROVIDES DETERMINISM HERE IS THE TICK, NOT THE FRAME RATE. `host_framerate` matters for a
    MOVIE, where every frame must be a fixed step; a single still seeked to an exact tick with
    `demo_gototick` is reproducible without it, and setting it during a long seek only slows the
    seek down.

.PARAMETER Demo
    The .dem to play. Copied into the game's own folder, and removed afterwards.

.PARAMETER Tick
    Which tick to capture. The demo's own numbering, as the probes report it.

.PARAMETER Out
    Where to write the .jpg.

.EXAMPLE
    pwsh tools/tf2-reference-capture.ps1 -Demo tools/corpus/local/x.dem -Tick 106270 -Out ref.jpg
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Demo,
    [Parameter(Mandatory)] [int]    $Tick,
    [Parameter(Mandatory)] [string] $Out,
    [int] $Width = 1280,
    [int] $Height = 720,

    # Generous, because a cold TF2 start plus a seek deep into a match is minutes, not seconds.
    [int] $TimeoutSeconds = 420,

    # The engine's fixed step. 60 is the viewer's own default and keeps the two comparable.
    [int] $FrameRate = 60,

    # How many ticks BEFORE the target to stop at, so playback runs through it slowly rather than
    # freezing on it. A rocket lives about 57 ticks, so 40 puts the capture inside its flight.
    [int] $Lead = 40
)

$ErrorActionPreference = 'Stop'

function Find-Tf2 {
    # The install the rest of this project already resolves through MapLocator; hardcoded here
    # only as a fallback, because a TOOL may be told where the game is.
    $candidates = @(
        'F:/SteamLibrary/steamapps/common/Team Fortress 2',
        'C:/Program Files (x86)/Steam/steamapps/common/Team Fortress 2'
    )

    foreach ($one in $candidates) {
        if (Test-Path (Join-Path $one 'tf_win64.exe')) { return $one }
    }

    throw 'No TF2 install found. Pass -Verbose and edit Find-Tf2 if the game is elsewhere.'
}

# **Refuse rather than fight a running game.** A second instance clobbers the first's config and
# the owner's session; the whole point of the machine lock is that one thing owns the desktop.
foreach ($name in @('hl2', 'tf_win64', 'tf')) {
    if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
        throw "TF2 is already running ($name). Close it first - this tool takes the desktop."
    }
}

$game = Find-Tf2
$tf = Join-Path $game 'tf'

if (-not (Test-Path $Demo)) { throw "No demo at $Demo" }

$demoName = 'tf2ref_' + [System.IO.Path]::GetFileNameWithoutExtension($Demo)
$demoCopy = Join-Path $tf "$demoName.dem"
$cfgPath = Join-Path $tf 'cfg/tf2ref.cfg'
$shots = Join-Path $tf 'screenshots'
$shotName = 'tf2ref'

Write-Host "TF2:  $game"
Write-Host "demo: $Demo -> $demoCopy"
Write-Host "tick: $Tick, $Width x $Height, host_framerate $FrameRate"

# What is already there, so the NEW file is identified by difference rather than by name - the
# engine appends a number when a name is taken, and assuming the name would pick up a stale shot.
$before = @()
if (Test-Path $shots) {
    $before = Get-ChildItem $shots -Filter '*.jpg' | Select-Object -ExpandProperty Name
}

Copy-Item $Demo $demoCopy -Force

# **Only what survives the level load goes here**: cvars and the `playdemo` itself. Everything that
# has to happen AFTER playback begins is delivered by -hijack below, because the load flushes the
# command buffer and takes any queued chain with it.
$config = @"
// Generated by tools/tf2-reference-capture.ps1 (B161). Deleted when it finishes.
jpeg_quality 100
cl_showfps 0
net_graph 0
con_enable 1
engine_no_focus_sleep 0
playdemo $demoName
"@

Set-Content -Path $cfgPath -Value $config -Encoding ascii

$arguments = @(
    '-game', 'tf',
    '-novid', '-windowed', '-noborder',
    '-w', $Width, '-h', $Height,
    '-condebug',                       # writes tf/console.log, which is how a failure is read
    '+exec', 'tf2ref'
)

$log = Join-Path $tf 'console.log'
if (Test-Path $log) { Remove-Item $log -Force }

$process = Start-Process -FilePath (Join-Path $game 'tf_win64.exe') `
    -ArgumentList $arguments -PassThru

Write-Host "launched pid $($process.Id); waiting up to $TimeoutSeconds s"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

# Brings the game's window to the front, so the engine actually draws the frame being captured.
function Show-Window {
    if (-not ('Tf2Ref.Windows' -as [type])) {
        Add-Type -Namespace Tf2Ref -Name Windows -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr handle);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindow(System.IntPtr handle, int how);
'@
    }

    $process.Refresh()
    $handle = $process.MainWindowHandle

    if ($handle -eq [System.IntPtr]::Zero) {
        Write-Warning 'The game has no window handle yet; the screenshot may be empty.'
        return
    }

    [Tf2Ref.Windows]::ShowWindow($handle, 9) | Out-Null   # SW_RESTORE
    [Tf2Ref.Windows]::SetForegroundWindow($handle) | Out-Null

    Write-Host '  brought the game to the front'
}

# Sends one console command to the instance already running.
function Send-Command {
    param([string] $Command)

    Write-Host "  -> $Command"

    Start-Process -FilePath (Join-Path $game 'tf_win64.exe') `
        -ArgumentList @('-game', 'tf', '-hijack', "+$Command") -Wait -ErrorAction SilentlyContinue
}

# Waits for a line to appear in the game's own log, which is how each step is KNOWN to have
# happened rather than assumed - the fault this project keeps meeting is a sleep standing in for a
# condition (docs/memory/instrument-bugs-outnumber-decoder-bugs.md).
function Wait-ForLog {
    param([string] $Pattern, [datetime] $Until, [string] $What)

    while ((Get-Date) -lt $Until) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern $Pattern -Quiet)) {
            Write-Host "  $What"
            return $true
        }

        if ($process.HasExited) { return $false }

        Start-Sleep -Seconds 3
    }

    return $false
}

$found = $null

if (Wait-ForLog -Pattern 'Playing demo from' -Until $deadline -What 'demo is playing') {
    # A few seconds of real playback before seeking: the engine has loaded, but the first frames
    # are still resolving models and materials.
    Start-Sleep -Seconds 8

    # **Pause AT the tick, then seek to just before it.** Seeking straight to the tick and then
    # screenshotting is a race the engine can win: the first attempt targeted tick 106270 of a
    # 106,313-tick demo - 43 ticks, two thirds of a second, from the end - so playback ran off the
    # end and returned to the main menu before the screenshot command arrived. The capture was of
    # the menu, which is a wrong answer that looks like a tooling failure.
    #
    # Paused, the engine cannot overrun however long the round trip takes, and the frame is exactly
    # the requested tick rather than that tick plus however many the seek overshot.
    # **Arrive just BEFORE the tick and play through it slowly, rather than stopping on it.**
    # `demo_pauseatservertick` lands exactly — the log says so — but a paused engine renders no
    # frames, and a screenshot is taken on the next frame rendered. `demo_resume` did not bring it
    # back: nothing appeared in the log after it and no image was ever written. So the pause is used
    # only as the ARRIVAL SIGNAL, and playback then continues at a tenth speed, which gives seconds
    # of real time around the target instead of a frozen instant.
    $stopAt = [Math]::Max(0, $Tick - $Lead)

    Send-Command "demo_pauseatservertick $stopAt"

    $from = [Math]::Max(0, $Tick - 900)

    Send-Command "demo_gototick $from"

    # **The engine ANNOUNCES its arrival, so wait for that and nothing else.** TF2 answers
    # `demo_gototick` by restarting and fast-forwarding rather than jumping, so a deep seek is
    # minutes of work — and the first version treated "the log stopped growing" as arrival. It fired
    # mid-seek, the three commands after it were sent into a busy engine and lost, and the pause
    # message then turned up as the LAST line of the log, after the screenshot had already been
    # asked for. Synchronise on the condition, never on the clock
    # (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
    if (-not (Wait-ForLog -Pattern "Demo paused at server tick $stopAt" -Until $deadline `
            -What "arrived at tick $stopAt, $Lead before the target")) {
        throw "The demo never reached tick $stopAt. Check it is inside the demo's own length."
    }

    # **Paused is exact but does not render, so it cannot screenshot.** `demo_pauseatservertick`
    # lands the engine on the requested tick — the log says `Demo paused at server tick 106270` —
    # and `jpeg` then produces nothing at all, because the screenshot is taken on the next frame the
    # engine actually advances and a paused demo advances none.
    #
    # So: arrive paused for exactness, then resume at 1% speed. The frame is within a tick or two of
    # the target instead of exactly on it, and playback cannot reach the end of the demo during the
    # round trip — which is what the first attempt failed on.
    # **A control on the instrument before believing its absence.** `echo` is the cheapest command
    # that must appear in the log; if the marker is missing then the hijack is not landing and the
    # screenshot commands were never the problem
    # (`docs/memory/an-empty-search-needs-a-control.md`).
    Send-Command 'echo TF2REF_HIJACK_REACHED_THE_GAME'

    if (-not (Wait-ForLog -Pattern 'TF2REF_HIJACK_REACHED_THE_GAME' `
            -Until (Get-Date).AddSeconds(30) -What 'hijack still reaches the game')) {
        throw 'The hijack no longer reaches the running game after the seek. The commands before it did land - the demo paused on the requested tick - so this is about WHEN, not about the mechanism.'
    }

    # **Disarm the pause before resuming, or it fires again immediately.**
    # `demo_pauseatservertick` stays set, so a resumed demo advances one tick, meets the same
    # condition and pauses again — the engine never renders a frame, and `jpeg` has nothing to
    # capture however many times it is asked. This is the guard-that-is-the-mechanism shape:
    # the thing that made the seek exact is the thing preventing the picture
    # (`docs/memory/a-guard-you-remove-may-be-the-mechanism.md`).
    Send-Command 'demo_pauseatservertick 0'
    Send-Command 'demo_timescale 0.1'
    Send-Command 'demo_resume'
    Send-Command 'demo_togglepause'

    # **A screenshot needs a rendered frame, and an unfocused TF2 renders none.** Every command was
    # accepted silently — no unknown-command error anywhere in the log, and the `echo` control came
    # back — while `jpeg` still wrote nothing. The engine sleeps instead of drawing when its window
    # is not in front, so there was no back buffer to capture. `engine_no_focus_sleep 0` in the
    # config is half of the answer and this is the other half.
    Show-Window

    Start-Sleep -Seconds 5

    # **Several shots as it passes, not one.** At a tenth speed the target tick is seconds of real
    # time away rather than an instant, and asking repeatedly costs nothing while a single ask
    # depends on the round trip landing inside a window this script cannot see.
    for ($shot = 0; $shot -lt 12 -and -not $found; $shot++) {
        Send-Command "jpeg $shotName$shot"

        Start-Sleep -Seconds 2

        if (Test-Path $shots) {
            $new = Get-ChildItem $shots -Filter '*.jpg' |
                Where-Object { $before -notcontains $_.Name }

            if ($new) {
                $found = $new | Sort-Object LastWriteTime | Select-Object -Last 1
            }
        }
    }

    Send-Command 'echo TF2REF_ASKED_FOR_THE_SCREENSHOT'
}

# **Killed rather than left**, because a tool that takes the desktop must give it back even when it
# failed - and a half-finished demo playback holds the machine lock for as long as it is alive.
if (-not $process.HasExited) {
    Write-Host 'stopping TF2'
    $process.Kill()
    $process.WaitForExit(30000) | Out-Null
}

if ($found) {
    Copy-Item $found.FullName $Out -Force
    Write-Host "wrote $Out from $($found.Name)"
    Remove-Item $found.FullName -Force
}

# Clean up what was put in the game's folder, whether or not it worked.
Remove-Item $demoCopy -Force -ErrorAction SilentlyContinue
Remove-Item $cfgPath -Force -ErrorAction SilentlyContinue

if (-not $found) {
    if (Test-Path $log) {
        Write-Host '--- last 40 lines of tf/console.log ---'
        Get-Content $log -Tail 40
    }

    throw 'No screenshot was produced. The console log above is the evidence - check first that `-hijack` reached the running instance, which shows as the commands appearing in that log.'
}
