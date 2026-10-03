# Tf2DemoSalvage 0.1.0-beta.3

## Changes since 0.1.0-beta.2

- **Overlay layers:** overlays draw in the layers the mapper gave them, one layer at a time, as the game does, so a
  marking placed on top stays on top (136 stock maps layer their overlays, e.g. cp_badlands, cp_dustbowl, cp_granary).

## Changes in 0.1.0-beta.2

- **POV demos:** the recorder's own body now moves the way the game predicts it. His recorded inputs are
  re-simulated between packets, covering items, surfaces, doors, buildings, water, taunts, karts, stuns,
  parachutes and grappling hooks. His legs turn smoothly every frame instead of stepping each tick.
- **Steam on any drive:** the viewer finds TF2 wherever Steam records it, not only under Program Files.
  `TF2_FOLDER` overrides every lookup.
- **File > Export assembly / Compile assembly**, the same text and byte-identical rebuild as the CLI.
- **Very large demos** decompile in under 100 MB of memory.
- `demo_*` settings in your config take effect without reopening the demo.

Tf2DemoSalvage is a public beta. Tf2DemoSalvage reads Team Fortress 2 `.dem` files from any period of TF2's
history, including demos the current game client can no longer play, because it decodes each demo
against the entity schema the demo carries rather than one hardcoded for a single era.

This is a **beta**: decoding is well tested; the viewer is usable and still visibly different from
the game in places listed under *Known gaps*.

## What is in the zip

| folder | program | what it does |
|---|---|---|
| `cli/` | `tf2demosalvage.exe` | Decompiles a demo to text and compiles the text back to a demo. |
| `viewer/` | `tf2demoview.exe` | Plays a demo back in 3D with the game's own maps, models, materials and sounds. |

### The command-line tool

```
tf2demosalvage <demo> -t [-e] -o out.txt     readable trace, message by message (-e expands entity snapshots)
tf2demosalvage <demo> -s                     summary
tf2demosalvage <demo> -j -o out.jsonl        JSON Lines
tf2demosalvage <demo> -a -o out.asm          assembly text, which compiles back:
tf2demosalvage out.asm -c -o rebuilt.dem
tf2demosalvage --help                        every option
```

The decoded form goes to standard output and stays pipeable; diagnostics go to standard error. Memory
does not grow with the demo's size: 44 to 51 MB for demos of 9 MB, 1.3 GB and 2 GB (measured 2026-10-01).

### The viewer

Open a demo from the window, or pass it on the command line. `tf2demoview --help` lists every flag and
environment variable. Your own TF2 config (`.cfg`, or a mastercomfig-style `.vpk`) works as-is for
key bindings; commands the viewer does not implement are ignored rather than rejected.

The File menu also has **Export assembly** (the open demo as text) and **Compile assembly** (text back
to a byte-identical demo), the same as the command-line tool's `-a` and `-c`.

## Reporting problems

The viewer writes one log per run to `%LOCALAPPDATA%\Tf2DemoSalvage` (`viewer-<date>-<time>-<id>.log`,
newest 50 kept). Send the newest one with the demo's name and what you did. The command-line tool
writes no log file; send its console output.

## What has been verified

Each claim below is a measurement, not an expectation.

- **Every protocol TF2 has shipped with that a demo could be found for: 11, 14, 15, 16 and 24.** The
  test suite carries one demo per era and point of view, recorded on a client of that period: 2007
  (build 3258, first-person and SourceTV), 2008 (build 3420, both), 2009 (build 3862, first-person),
  2011 (build 4604, both), 2013 (build 1729296, both) and a 2020 match. All decode and round-trip in
  every test run. Protocols 17 to 23 have no known surviving demo, so they are untested.
- **Both points of view.** First-person (POV) recordings and SourceTV recordings decode alike; most
  eras above are tested with a POV and a SourceTV recording of the same session.
- **A census of 459 distinct real-world demos** (2026-09-30): the entity stage failed on none. At
  that time 214 passed every stage; every failure class it found has been fixed since (pause
  messages, demos cut off mid-command, Steam Voice audio, two 2010 SourceTV demos, the 2007 SourceTV
  schema cut, oversized files in the CLI). **The census has not been re-run since those fixes**, so
  the post-fix pass count is not yet measured.
- **Text round-trips to the identical file.** A demo decompiled to assembly and compiled back
  reproduces the original bytes, held by the test suite for every era above.
- **Voice from every era:** Speex (2007 to 2011), Steam Voice / SILK (2011 to 2016), CELT
  (2016 to about 2018) and Opus (since).

## Known gaps

Things a user is likely to notice. Each has an entry in `docs/RISKS.md` in the repository.

- **Very large demos cannot be opened in the viewer.** A demo left recording on an idle server for
  hours (the 1.3 GB and 2 GB specimens found so far) works in the command-line tool, but the
  viewer's timeline needs 40 to 84 times the file's size in memory (B449, B439).
- **The viewer needs a lot of memory.** A typical modern match uses about 4 GB once loaded (B433,
  B407); see *Requirements*.
- **A truncated 2007 SourceTV schema is only completed for the one build known to truncate it**
  (build 3258). Another launch-era SourceTV demo with a cut schema would be refused with a message
  saying so (B24).
- **No footsteps or landing sounds.** The game predicts these on the client and never records them
  in the demo (B172).
- **Cosmetics are not drawn in first person** (B186).
- **Switching demos without restarting the viewer gets slower** (B148).
- **The 3D skybox is drawn without its scale transform** (B152).
- **Demos recorded in late June 2011 were malformed by the game itself** and may not decode (B144).
- **The first-person camera's movement differs slightly from the game's** in velocity and feet yaw
  (B450).

## Requirements

- **Windows 10 or 11, x64**, with a Direct3D 11 GPU.
- **8 GB of RAM minimum, 16 GB recommended.** The viewer holds about 4 GB with a full match loaded (B433).
- **Nothing else to install.** Both programs carry their own copy of .NET, so no runtime download is
  needed; unzip and run.
- **Your own Team Fortress 2 install**, for the viewer. Nothing from the game is included — no maps,
  models, materials or sounds — and no demos. The viewer reads them from your install (found through the Steam folder Steam records in the registry, then its library list, so Steam on another drive works; setting `TF2_FOLDER` to a `tf` folder overrides that for maps, models, materials, sounds and configs alike) and, if it
  cannot find one, says so and plays the demo without the game's maps and models. The command-line tool needs only the demo.

## Licences

- **Tf2DemoSalvage** — MIT, `LICENSE.txt`.
- **Speex 1.2.1** (`viewer/speex.dll`) — Xiph.Org BSD-style, `licenses/SPEEX-COPYING.txt`.
- **CELT 0.11.3** (`viewer/celt.dll`) — Xiph.Org BSD-style, `licenses/CELT-COPYING.txt`.
- **SILK SDK 1.0.9** (`viewer/silk.dll`) — Skype Limited BSD-style, `licenses/SILK-LICENSE.txt`.
  Tf2DemoSalvage did not write it and does not claim to; it is included under that licence, which
  grants no patent rights.
- **libopus 1.6.1** (`viewer/runtimes/win-x64/native/opus.dll`) — Xiph.Org BSD-style,
  `licenses/OPUS-COPYING.txt`.
- **OpenAL Soft 1.23.1** (`viewer/soft_oal.dll`, from the `Silk.NET.OpenAL.Soft.Native` package) —
  LGPL 2.0 or later, `licenses/OPENAL-SOFT-COPYING.txt`; dynamically loaded and replaceable.
- **.NET runtime** (bundled in `viewer/` and `cli/`) — MIT, Microsoft.
- Other third-party packages in `viewer/` and `cli/` (Silk.NET, NLayer,
  Microsoft.Extensions.Logging) are under their own licences, published with each package on NuGet.
