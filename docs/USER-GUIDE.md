# Tf2DemoSalvage viewer: user guide

`tf2demoview` plays a Team Fortress 2 `.dem` file back in 3D, using the maps, models, materials and sounds
from your own TF2 install. It reads demos from any era, including ones the live game client can no longer
play. This guide covers the viewer. For the command-line tool (`tf2demosalvage`), see `README.md`.

The viewer is a beta. What is known to differ from the game is listed in `RELEASE-NOTES.md` under *Known gaps*.

## Requirements

- Windows 10 or 11, x64, with a Direct3D 11 GPU.
- 16 GB of RAM. The memory figures and their date are under *Requirements* and *Known gaps* in
  `RELEASE-NOTES.md`.
- Your own Team Fortress 2 install. No game files come with the viewer, and no demos.
- Nothing else to install. Unzip the release anywhere and run `viewer/tf2demoview.exe`.

## Open a demo

- **File picker:** File > Open demo (Ctrl+O). You can select several `.dem` files at once.
- **Command line:** `tf2demoview <demo.dem> [more.dem ...] [options]`. `tf2demoview --help` lists every
  option.
- **Library:** TODO(owner): no demo library is described in `ViewerHelp.cs`, `README.md` or
  `RELEASE-NOTES.md` on this branch. Say what it is, or remove this entry.

If the file is not a demo, `--shot` and `--measure` exit with code 1 and print the reason on standard error.

## Play, pause and seek

- **O** plays and pauses. **Home** returns to normal speed.
- `--autoplay` starts playing as soon as the demo loads.
- `--tick <n>` seeks to a tick before the first frame is drawn.
- TODO(owner): the on-screen seek controls and the keys or commands for faster and slower playback are not
  recorded in the sources for this guide. Name them here.

Seeking forward replays the skipped ticks, and a rewind restarts from the beginning, as the game does. A seek
also silences every sound except the loops. Both are listed in `RELEASE-NOTES.md` under *Known gaps*.

## Cameras

- **Space** switches camera mode.
- **Mouse1** and **Mouse2** select the next and previous player.
- **Ctrl+E** is first person. **Ctrl+B** is third person, the chase camera. **Ctrl+R** resets the camera.
- **W, A, S, D** fly the free camera. **`'`** moves it up and **`/`** moves it down. **Shift** slows it.
- **Tab** shows the scoreboard while held.
- Start options: `--first-person`, `--third-person`, `--spectate <who>` (a name, a Steam id, a user id or an
  entity index), `--look <x> <y>` and `--zoom <factor>` for the overhead camera.

**A point-of-view (POV) demo has no free camera and no third-person view.** You always see what the recorder
saw: first person while they were alive, and whatever they were watching while they were dead or spectating.
`--spectate` is ignored on a POV demo. The viewer does this on purpose, as TF2 does (`docs/DECISIONS.md` D128).
A SourceTV demo keeps the free camera.

## Use your own TF2 config

A real TF2 config works as is: `config.cfg`, `autoexec.cfg`, or a mastercomfig-style `.vpk`. Your key
bindings are used as they are. The viewer uses Source's own names for keys (`SPACE`, `CTRL`,
`MOUSE1`) and for actions (`+forward`, `+jump`, `+moveup`). It ignores every command it does not implement,
so a long config does no harm (`docs/DECISIONS.md` D69).

Without a config, the defaults apply. These are the ones listed above, plus **F5** screenshot, **F8** frame
rate, **F11** full screen and **Ctrl+O** open demo. The full table is `KeyBindings.Defaults` in
`managed/Tf2DemoSalvage.Presentation/ViewerAction.cs`.

TODO(owner): say where the viewer looks for a config, if that is not already obvious to a user.

You can set a cvar for one run only, as in Source: `tf2demoview <demo> +fps_max 0`. Anything in
`settings.cfg` works this way.

`--hud <folder or .vpk>` draws a HUD laid out as in `tf/custom`. Without it the viewer draws TF2's stock HUD.

## Take a screenshot

- Press **F5**. Files go to the folder named by `cl_screenshot_folder` in `settings.cfg`.
- `--shot <path>` saves one frame to `<path>` and exits. `--tick <n>` picks the moment.
- `--like` and the `.png.json` file written beside a screenshot: TODO(owner): neither is in `ViewerHelp.cs`
  on this branch, so this guide does not describe them. Add them once they land.

## Where logs and settings live

- **Settings:** `%LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg`, in TF2's own config syntax. Lines the viewer
  does not know are ignored.
- **Logs:** one per run in `%LOCALAPPDATA%\Tf2DemoSalvage`, named `viewer-<date>-<time>-<process id>.log`.
  The newest 50 are kept. The log is buffered, so a viewer that is still running may not have written
  everything yet.
- **Downloaded maps:** `%LOCALAPPDATA%\Tf2DemoSalvage\maps`.

To report a bug, send the newest log, the demo's file name (and the demo, if you can), and what you did and
saw.

## Known limits

The full list is `RELEASE-NOTES.md`, *Known gaps*. Read it before you report something as broken. Two limits
affect every user:

- The viewer needs a lot of memory. See *Requirements* in the release notes.
- Demos from a server left recording for hours cannot be opened in the viewer, though the command-line tool
  reads them.

## Troubleshooting

**The viewer asks for a folder at startup.** It could not find TF2. It looks where Steam records itself, then
reads Steam's library list for the library that holds TF2. Choose your `tf` folder. File > TF2 folder
changes it later. For one run only, start with `+cl_game_folder <path>`. The folder is yours to provide: the
viewer ships no game files.

**I cancelled the folder picker, and now it does not ask.** That is `cl_game_folder_ask 0`. The demo still
plays, without the game's maps and models. File > TF2 folder sets the folder.

**The map is missing.** If your install lacks the map, the viewer downloads it to
`%LOCALAPPDATA%\Tf2DemoSalvage\maps`. A downloaded map is cached by name, not by checksum. TODO(owner): D224
and B556, the status message for a demo whose map is missing, are not in `docs/DECISIONS.md` or
`docs/RISKS.md` on this branch. Add what the viewer shows and says, and note that it is unverified on every
path.

**A demo will not open.** A file that is not a demo exits with the reason on standard error when you use
`--shot` or `--measure`. Demos recorded in late June 2011 were malformed by the game itself and may not decode.
Send the log.
