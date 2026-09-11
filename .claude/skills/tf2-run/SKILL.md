---
name: tf2-run
description: Launch tf2demoview to show the owner the app running (motion, not just a still). Use when asked to run, start, launch, or open the viewer for the owner to watch, as distinct from a headless --shot capture. Project-scoped: the launcher the bundled `run` skill should defer to for this repo.
---

# Launching the viewer for the owner to watch

**Build first, always** (`--no-build` is banned project-wide). The viewer takes the desktop, so it
goes through `run-exclusive.ps1`, and it is a GUI app: launched bare with `&`, PowerShell returns
immediately without waiting for it to close (`docs/memory/a-gui-exe-does-not-hold-the-lock.md`).

## The one mistake already made here

**`Start-Process -FilePath <script>.ps1 ...` does NOT run the script — it opens `.ps1` in whatever
Windows associates with that extension, which on this machine is Notepad.** `-FilePath` on
`Start-Process` launches through the shell's file association, not through PowerShell. Never do this:

```powershell
# WRONG — opens run-exclusive.ps1 as a text file
Start-Process -FilePath "run-exclusive.ps1" -ArgumentList @(...)
```

## The correct invocation

Build, then invoke `run-exclusive.ps1` itself as a PowerShell command (via `&`), with the exe and its
arguments as its own arguments — never wrap it in `Start-Process`:

```bash
cd Tf2DemoSalvage
MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D/Tf2DemoSalvage.Viewer3D.csproj -c Release
```

```powershell
pwsh -NoProfile -Command "& 'C:\Users\pinku\source\repos\PinKushin\run-exclusive.ps1' 'C:\Users\pinku\source\repos\PinKushin\Tf2DemoSalvage\managed\Tf2DemoSalvage.Viewer3D\bin\Release\net10.0-windows\tf2demoview.exe' '<demo path>' --autoplay --first-person"
```

Run this **in the background** (the Bash tool's `run_in_background: true`, or PowerShell's own
backgrounding) — it blocks until the window closes, and the point is for the owner to watch it while
the session stays usable. Never pass `--shot`: that is the headless single-frame path
(`docs/memory/take-your-own-screenshot.md`), the opposite of what "run it for me" is asking for.

## Picking the demo

**Default to the f12 demo** unless the owner names a different one —
`docs/memory/the-f12-demo-is-the-parity-reference.md`. Announce the demo by name in the reply.
`tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem` is a point-of-view recording, so
`--first-person` opens on the recorder's own camera; add `--autoplay` so it starts moving without a
keypress. A SourceTV demo has no recorded view and opens on the free camera instead — mention that if
one is used.

## What to say afterward

The window takes 20-30 seconds to load before it starts drawing. Tell the owner it is loading and
about how long. This is a UI claim: only the owner's own look settles whether it's right — don't
declare it correct yourself.
