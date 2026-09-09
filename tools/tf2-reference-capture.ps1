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
    [int] $Lead = 40,

    # How fast playback runs through the target. A twentieth turns 40 ticks into about twelve
    # seconds of real time, which is a window to poll rather than an instant to hit.
    [double] $Timescale = 0.05,

    # Stop on the tick and WAIT, instead of trying to take the picture. Everything this tool does
    # automatically works except the shutter; with this switch a person presses F5 and gets the
    # frame in seconds, which is worth more than a tool that is nearly finished.
    [switch] $HoldForManualShot,

    [int] $HoldSeconds = 300
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
# **Unfiltered, because the engine chooses the extension.** This listed only `*.jpg` while the
# search for a new file listed everything, so a `.tga` left behind by an earlier run was invisible
# to the "before" set and then found as "new" — and four consecutive runs reported success while
# handing back the same stale menu capture. The instrument was answering about itself
# (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
$before = @()
if (Test-Path $shots) {
    $before = Get-ChildItem $shots | Select-Object -ExpandProperty Name
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

# Copies the game window's pixels straight off the screen.
#
# **This is the shutter, and it deliberately does not go through the engine.** `jpeg` and
# `screenshot` are accepted without error while a demo is loaded and write nothing anywhere - not
# to `tf/screenshots`, not as `.tga`, not into Steam's own store, all of which were checked. The
# window is showing the frame regardless, so it is read from there.
function Save-Window {
    param([string] $Path)

    Add-Type -AssemblyName System.Drawing

    if (-not ('Tf2Ref.Rect' -as [type])) {
        Add-Type -Namespace Tf2Ref -Name Rect -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Box { public int Left, Top, Right, Bottom; }

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetWindowRect(System.IntPtr handle, out Box box);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr GetForegroundWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr handle, System.IntPtr dc, uint flags);
'@
    }

    $process.Refresh()
    $handle = $process.MainWindowHandle

    if ($handle -eq [System.IntPtr]::Zero) {
        Write-Warning 'The game has no window to read.'
        return $false
    }

    # **Refuse rather than capture whatever is on top.** A screenshot of the wrong window is a wrong
    # answer that looks like a right one, which is the failure this whole exercise keeps meeting.
    if ([Tf2Ref.Rect]::GetForegroundWindow() -ne $handle) {
        Write-Warning 'The game is not the foreground window; the capture would be of something else.'
        return $false
    }

    $box = New-Object Tf2Ref.Rect+Box
    [Tf2Ref.Rect]::GetWindowRect($handle, [ref] $box) | Out-Null

    $wide = $box.Right - $box.Left
    $tall = $box.Bottom - $box.Top

    if ($wide -le 0 -or $tall -le 0) {
        Write-Warning "The game window has no size ($wide x $tall)."
        return $false
    }

    $bitmap = New-Object System.Drawing.Bitmap $wide, $tall
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)

    # **`PrintWindow` with `PW_RENDERFULLCONTENT`, not `CopyFromScreen`.** BitBlt off the screen DC
    # returns black for a hardware-composited Direct3D swapchain, which is what forty consecutive
    # black captures were - taken while the demo was demonstrably PLAYING, so it was never about the
    # pause. `PW_RENDERFULLCONTENT` (2) asks the compositor for the window's real contents and is
    # the documented way to capture a D3D window.
    $dc = $canvas.GetHdc()
    $printed = [Tf2Ref.Rect]::PrintWindow($handle, $dc, 2)
    $canvas.ReleaseHdc($dc)

    if (-not $printed) {
        # The older route, kept as a fallback rather than removed: it works for an ordinary window
        # and costs nothing to try when the modern one refuses.
        $canvas.CopyFromScreen($box.Left, $box.Top, 0, 0, $bitmap.Size)
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Jpeg)

    $canvas.Dispose()
    $bitmap.Dispose()

    Write-Host "  captured the window, $wide x $tall (PrintWindow: $printed)"

    return $true
}

# Presses one key, as a person would.
#
# **This is the route the engine actually honours.** `jpeg` and `screenshot` delivered over
# `-hijack` are accepted and write nothing while a demo is loaded; the same command reached through
# its BINDING works, because it runs on the engine's own input path rather than being injected into
# the console from another process. `keybd_event` synthesises at the driver level, which is what
# makes a game accept it where a posted window message is ignored.
function Send-Key {
    param([byte] $Code)

    if (-not ('Tf2Ref.Keys' -as [type])) {
        Add-Type -Namespace Tf2Ref -Name Keys -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern void keybd_event(byte code, byte scan, uint flags, System.IntPtr extra);
'@
    }

    [Tf2Ref.Keys]::keybd_event($Code, 0, 0, [System.IntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [Tf2Ref.Keys]::keybd_event($Code, 0, 2, [System.IntPtr]::Zero)   # KEYEVENTF_KEYUP
}

# Which key the OWNER has bound to a screenshot, read from the game rather than assumed.
#
# **Nothing is rebound.** TF2 writes `cfg/config.cfg` on shutdown - the log said
# `Host_WriteConfiguration: Wrote cfg/config.cfg` on an earlier run - so a `bind` issued here would
# outlive the capture and change what the owner's own F5 does. Asking is free; editing is not.
function Find-ScreenshotKey {
    foreach ($command in @('jpeg', 'screenshot')) {
        Send-Command "key_findbinding $command"

        # **The console log is BUFFERED**, so a single read two seconds later finds nothing and the
        # miss reads as "nothing is bound" - which is what it did. Poll instead; this project has
        # met the same buffering with the viewer's own `--measure` output.
        $line = $null
        $until = (Get-Date).AddSeconds(25)

        while ((Get-Date) -lt $until -and -not $line) {
            Start-Sleep -Seconds 3

            if (-not (Test-Path $log)) { continue }

            # The engine answers with both sides QUOTED - `"F5" = "screenshot"` - which an unquoted
            # pattern misses entirely. **And it is NOT anchored to the start of a line**, because
            # the console writes without a trailing newline often enough that the answer lands
            # concatenated onto whatever preceded it: `TF2REF_HIJACK_REACHED_THE_GAME "F5" =
            # "screenshot"` is a real line from this log.
            $line = Select-String -Path $log -Pattern "`"([^`"]+)`"\s*=\s*`"$command`"" |
                Select-Object -Last 1
        }

        if ($line) {
            $key = $line.Matches[0].Groups[1].Value
            Write-Host "  '$command' is bound to $key"
            return $key
        }
    }

    return $null
}

# Whether a capture has anything in it at all.
#
# **A black frame is the failure this tool keeps producing**, so it is detected rather than handed
# back as a reference. Sampling a grid is enough: a rendered TF2 frame is never uniformly black,
# and a non-presenting swapchain is exactly that.
function Test-Brightness {
    param([string] $Path)

    Add-Type -AssemblyName System.Drawing

    $bitmap = [System.Drawing.Bitmap]::FromFile($Path)

    try {
        $total = 0
        $counted = 0

        for ($y = 4; $y -lt $bitmap.Height; $y += 32) {
            for ($x = 4; $x -lt $bitmap.Width; $x += 32) {
                $pixel = $bitmap.GetPixel($x, $y)
                $total += $pixel.R + $pixel.G + $pixel.B
                $counted++
            }
        }

        $mean = if ($counted -gt 0) { $total / (3 * $counted) } else { 0 }

        return $mean -gt 6
    }
    finally {
        $bitmap.Dispose()
    }
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
    # **Never pause, at any point.** A paused engine stops presenting, and then NOTHING can read the
    # frame — `jpeg` writes no file, and reading the window off the screen gives 1280x720 of pure
    # black. Pausing was how the seek was made exact; it is also why every capture failed.
    #
    # So playback is slowed instead. `demo_timescale` is set BEFORE the seek, and the seek lands
    # short of the target, so the engine arrives already rendering and crawls through the tick over
    # seconds of real time. That gives a window wide enough to poll rather than an instant to hit.
    # **Force windowed at RUNTIME, because the launch option loses to the saved config.** TF2 stores
    # the video mode in `config.cfg` and restores it, so `-windowed` can be overridden by whatever
    # the owner last played at. Exclusive fullscreen is not redirected by the compositor, which is
    # why `PrintWindow` returned success and a black bitmap — this project already knows that
    # hazard from its own UI suite (`docs/memory/the-viewer-suite-wants-the-gpu.md`).
    Send-Command "mat_setvideomode $Width $Height 1"

    Start-Sleep -Seconds 5

    # **Pause exactly ON the tick, and hold there.** Playing slowly through it does not survive the
    # wait for the seek to settle: `demo_gototick` lands 40 ticks from the end of this demo and
    # playback then runs on at full speed, so the capture came back as the main menu.
    #
    # The earlier reason for avoiding the pause — "a paused engine does not render" — was inferred
    # from black window captures, and those turned out to be the compositor refusing to redirect a
    # Direct3D swapchain rather than anything about the pause. A paused demo draws its frame; only
    # reading it from outside was ever the problem.
    # **Never pause. The pause is a one-way door.** `demo_pauseatservertick` lands on the tick
    # exactly and the engine says so — but a paused demo renders no frames, a screenshot needs a
    # rendered frame, and NEITHER `demo_resume` NOR `demo_togglepause` brings playback back: the
    # log stops growing and stays stopped, which is the control that settles it.
    #
    # So the demo is slowed first and then seeked, and it arrives already running. The target tick
    # is reached seconds later in real time, which is a window wide enough to press the screenshot
    # key across rather than an instant to hit.
    Send-Command "demo_timescale $Timescale"

    $from = [Math]::Max(0, $Tick - $Lead)

    Send-Command "demo_gototick $from"

    # **The engine ANNOUNCES its arrival, so wait for that and nothing else.** TF2 answers
    # `demo_gototick` by restarting and fast-forwarding rather than jumping, so a deep seek is
    # minutes of work — and the first version treated "the log stopped growing" as arrival. It fired
    # mid-seek, the three commands after it were sent into a busy engine and lost, and the pause
    # message then turned up as the LAST line of the log, after the screenshot had already been
    # asked for. Synchronise on the condition, never on the clock
    # (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
    # The engine announces its own arrival, so wait for that and nothing else.
    # **No pause means no arrival line, so the seek is waited out by the log settling.** The engine
    # replays kill feed and chat as it fast-forwards, so a growing log IS the seek; a still one is
    # arrival. Getting this wrong now only costs a few early keypresses, because the loop below
    # keeps pressing for as long as the target is in view.
    $quiet = 0
    $last = -1

    while ((Get-Date) -lt $deadline -and $quiet -lt 4 -and -not $process.HasExited) {
        Start-Sleep -Seconds 4

        $size = (Get-Item $log -ErrorAction SilentlyContinue).Length

        if ($size -eq $last) { $quiet++ } else { $quiet = 0 }

        $last = $size
    }

    Write-Host "  seek settled; playing through tick $Tick at $Timescale speed"

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

    # The engine only draws when its window is in front, and only a drawn frame can be read.
    Show-Window

    Start-Sleep -Seconds 2

    if ($HoldForManualShot) {
        Send-Command 'demo_timescale 0'
        Send-Command "demo_pauseatservertick $Tick"

        Write-Host ''
        Write-Host "TF2 is at tick $Tick. Press F5 in the game window to take the screenshot."
        Write-Host "It lands in $shots (and in Steam's own store)."
        Write-Host "Holding for $HoldSeconds seconds, then closing the game."
        Write-Host ''

        $until = (Get-Date).AddSeconds($HoldSeconds)

        while ((Get-Date) -lt $until -and -not $process.HasExited) {
            Start-Sleep -Seconds 5

            $new = @()

            if (Test-Path $shots) {
                $new = Get-ChildItem $shots -Filter '*.*' |
                    Where-Object { $before -notcontains $_.Name }
            }

            if ($new) {
                $found = $new | Sort-Object LastWriteTime | Select-Object -Last 1
                Copy-Item $found.FullName $Out -Force
                Write-Host "  got it: $($found.Name)"
                break
            }
        }
    }
    elseif ($key = Find-ScreenshotKey) {
        # A handful of presses across the slow playback, for the same reason the captures were
        # polled: the target tick is a window in real time, not an instant.
        $codes = @{
            'F1' = 0x70; 'F2' = 0x71; 'F3' = 0x72; 'F4' = 0x73; 'F5' = 0x74; 'F6' = 0x75
            'F7' = 0x76; 'F8' = 0x77; 'F9' = 0x78; 'F10' = 0x79; 'F11' = 0x7A; 'F12' = 0x7B
        }

        if (-not $codes.ContainsKey($key.ToUpperInvariant())) {
            Write-Warning "The screenshot key '$key' is not one this tool can press."
        }
        else {
            $code = [byte] $codes[$key.ToUpperInvariant()]

            for ($press = 0; $press -lt 40 -and -not $found; $press++) {
                Show-Window
                Start-Sleep -Milliseconds 400

                # **A synthesised key goes to whatever has focus, not to a chosen window.** If the
                # game is not in front, F5 lands in the terminal that launched it and nothing
                # happens — which is indistinguishable from the engine refusing, and would have
                # been blamed on the engine.
                $process.Refresh()

                if ([Tf2Ref.Rect]::GetForegroundWindow() -ne $process.MainWindowHandle) {
                    if ($press -eq 0) {
                        Write-Warning 'The game is NOT the foreground window; F5 is going elsewhere.'
                    }

                    Start-Sleep -Milliseconds 500
                    continue
                }

                Send-Key -Code $code

                Start-Sleep -Seconds 2

                $new = @()

                if (Test-Path $shots) {
                    $new = Get-ChildItem $shots |
                        Where-Object { $before -notcontains $_.Name }
                }

                if ($new) {
                    $found = $new | Sort-Object LastWriteTime | Select-Object -Last 1

                    # **Wait for the engine to finish writing it.** Copying the moment the file
                    # appears takes a partial one — a 15 KB fragment that no decoder will open,
                    # which reads as "the capture failed" when it very nearly succeeded.
                    $settled = -1

                    for ($wait = 0; $wait -lt 20; $wait++) {
                        Start-Sleep -Milliseconds 500

                        $now = (Get-Item $found.FullName -ErrorAction SilentlyContinue).Length

                        if ($now -eq $settled -and $now -gt 0) { break }

                        $settled = $now
                    }

                    Copy-Item $found.FullName $Out -Force
                    Write-Host "  pressed $key and got $($found.Name), $settled bytes"

                    # Removed, so it cannot be found again by a later run and reported as fresh.
                    Remove-Item $found.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        }

        if (-not $found) {
            Write-Warning "Pressed $key eight times and no file appeared."
        }
    }
    else {

    # **Poll for a frame with something in it.** A black capture means the engine did not present,
    # which is a real answer rather than something to retry blindly - so they are counted and
    # reported rather than silently swallowed.
    $black = 0

    for ($attempt = 0; $attempt -lt 40 -and -not $found; $attempt++) {
        if (Save-Window -Path $Out) {
            if (Test-Brightness -Path $Out) {
                $found = Get-Item $Out
                break
            }

            $black++
        }

        Start-Sleep -Milliseconds 700
    }

        if (-not $found) {
            Write-Warning "$black captures were black - the engine never presented a frame. Try -HoldForManualShot."
        }
    }
}

# **Killed rather than left**, because a tool that takes the desktop must give it back even when it
# failed - and a half-finished demo playback holds the machine lock for as long as it is alive.
# **Every instance, not just the handle this script holds.** `-hijack` starts a second
# `tf_win64.exe` per command, and killing only the launcher left a game running after each run - so
# the NEXT run's hijacks went to a stale instance sitting at the main menu while the fresh one
# played the demo. That is very likely why capture after capture came back as the menu: the
# commands and the screenshots were going to two different games.
Write-Host 'stopping TF2'

foreach ($name in @('tf_win64', 'hl2', 'tf')) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); $_.WaitForExit(20000) | Out-Null } catch { }
    }
}

if ($found) {
    Write-Host "wrote $Out"
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
