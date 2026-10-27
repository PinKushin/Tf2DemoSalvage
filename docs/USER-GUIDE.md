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
- **Playlist:** the **Open folder** button chooses a folder of demos, and the list at the right of the window
  shows every `.dem` in it, with subfolders, sorted by folder and then name. Nothing is copied or imported: a demo
  stays where you keep it. You can open several folders at once. Double-click a demo, or press Enter on it, to
  load it. Type in the search box above the list to narrow it by demo name or folder. A folder that cannot be
  read is skipped and written to the log. Folders named like the game's asset folders (`materials`, `models`,
  `sound`, `maps`, `cfg`, `custom`, `download`, `replay` and similar) are not searched, so you can point it at
  a TF2 install without it walking the game's files.

If the file is not a demo, `--shot` and `--measure` exit with code 1 and print the reason on standard error.

## Play, pause and seek

- **O** plays and pauses. **Home** returns to normal speed.
- `--autoplay` starts playing as soon as the demo loads.
- `--tick <n>` seeks to a tick before the first frame is drawn.
- **The playback bar** along the bottom of the window has, left to right: jump to start (`|<`), slower (`<<`),
  Play, faster (`>>`), jump to end (`>|`), a speed slider, a scrub bar and a tick readout. Drag the scrub bar to
  seek. `<<` steps the speed down, through one quarter speed and into reverse. `>>` steps it up to a maximum of
  eight times. The speed slider sets any speed in between: right of center plays forward, left plays backward,
  and the center is the slowest speed in either direction.
- There is no default key for faster, slower, single-step or jump to start or end. Use the buttons. While the
  scrub bar or the speed slider has focus, it takes the arrow keys, **Home**, **End**, **Page Up** and
  **Page Down** for itself, so **Home** moves that slider instead of resetting the speed.

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

Without a config, the defaults apply. A bind in your config replaces the default for that action. A key you
`unbind` keeps its default. The defaults are:

| Key | Action (Source's name) |
|---|---|
| `SPACE` | `+jump`: switch camera mode |
| `MOUSE1`, `MOUSE2` | `+attack`, `+attack2`: next and previous player |
| `CTRL+e`, `CTRL+b` | `firstperson`, `thirdperson` |
| `CTRL+r` | `resetcamera` |
| `w`, `s`, `a`, `d` | `+forward`, `+back`, `+moveleft`, `+moveright` |
| `'`, `/` | `+moveup`, `+movedown` |
| `SHIFT` | `+speed`: slows the free camera |
| `TAB` | `+showscores` |
| `o` | `demo_togglepause`: play and pause |
| `HOME` | `resetspeed`: normal speed, forward |
| `CTRL+o` | `opendemo` |
| `F11` | `togglefullscreen` |
| `F5` | `screenshot` |
| `F8` | `cl_showfps` |
| `CTRL+p` | `cl_showpos` |
| `F9` | `mat_surfacecolours` |
| `F3`, `F4` | `mat_normalmaps`, `mat_bumpbasis` |
| `CTRL+w`, `CTRL+f`, `CTRL+x` | `mat_wireframe`, `mat_drawflat`, `mat_luxels` |
| `CTRL+l`, `CTRL+t` | `mat_leafvis`, `mat_showlowresimage` |
| `CTRL+0`, `CTRL+1`, `CTRL+2` | `mat_fullbright 0`, `mat_fullbright 1`, `mat_fullbright 2` |

**Where the viewer looks for your config.** It reads `cfg/config_default.cfg`, then `cfg/config.cfg`, then
`cfg/autoexec.cfg`, in that order, so each overrides the one before it. It looks in your TF2 `tf` folder,
including a `.vpk` under `tf/custom` that holds those files, as mastercomfig ships. It also looks in a `custom`
folder beside `tf2demoview.exe`, which is searched first. There you can use the same `cfg/` layout, or drop a
`config.cfg` or `autoexec.cfg` straight into `custom`. A file that is missing is normal. If none is found, the
built-in bindings apply. The log lists the files read and each bind it applied.

A command the viewer does not implement is ignored, and the key stays free for whatever else uses it. A key you
bind to a command the viewer does not implement loses its default action.

You can set a cvar for one run only, as in Source: `tf2demoview <demo> +fps_max 0`. Anything in
`settings.cfg` works this way.

`mat_queue_mode` picks which thread draws each frame, as TF2's own setting of that name does. The default, `-1`, is
TF2's: on a machine with two or more physical CPU cores the viewer draws on a second thread (mode `2`), and on one core
it draws on the main thread (mode `0`). On the author's recording the second thread made frames about 14% faster. To go
back to drawing on the main thread, put `mat_queue_mode 0` in `settings.cfg`, or for one run use
`tf2demoview <demo> +mat_queue_mode 0`. Screenshots wait for the second thread to finish its frame.

`r_threaded_particles` steps the particle effects on several threads (your physical cores minus one), as TF2's setting of
that name does. The default is `1` and the picture is the same either way. To use one thread, put
`r_threaded_particles 0` in `settings.cfg`, or for one run use `tf2demoview <demo> +r_threaded_particles 0`. The particle
step got cheaper by 0.07 to 0.09 ms a frame on the author's recording; a faster whole frame is not established
(`docs/measurements/2026-10-26-threaded-particles-and-sun.md`).

`cl_threaded_bone_setup` poses the skeletons the previous frame used on the worker threads before the frame, as TF2's
setting of that name does. The default is `0`, as in TF2, and the picture is the same either way. To try it, put
`cl_threaded_bone_setup 1` in `settings.cfg`, or for one run use `tf2demoview <demo> +cl_threaded_bone_setup 1`. On the
author's recording the pose step got cheaper by 0.13 to 0.17 ms a frame; a faster whole frame is not established
(`docs/measurements/2026-10-27-bone-clock-and-threaded-setup.md`).

`--hud <folder or .vpk>` draws a HUD laid out as in `tf/custom`. Without it the viewer draws TF2's stock HUD.

## Take a screenshot

- Press **F5**. Files go to the folder named by `cl_screenshot_folder` in `settings.cfg`.
- `--shot <path>` saves one frame to `<path>` and exits. `--tick <n>` picks the moment.
- **The companion file.** `--shot out.png` also writes `out.png.json` beside the picture. It records the demo's
  file name and sha256, the map, the tick, the camera, the view size, the settings that differ from their
  defaults, the build and the command line (file names only), so you can rebuild the capture later. The
  screenshot key writes a companion only in a Debug build, so a release build's key writes just the picture.
  Deleting a screenshot from the viewer's tidy-up deletes its companion too.
- **Reproduce a TF2 capture.** `--like <shot.png.json>` takes the tick, camera, view size and console variables
  from the companion file of a TF2-side capture, and its demo too, from the file's folder, when you name none.
  A `--tick` or `+cvar` you pass still wins; the camera and view-size environment variables do not. If the demo
  you open is not the one the file names (different sha256), the viewer warns and still shoots. A file with a
  bad camera, or one whose demo is a path rather than a file name, is refused with the reason.

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
`%LOCALAPPDATA%\Tf2DemoSalvage\maps`. A downloaded map is cached by name, not by checksum. If the map cannot be
had, the demo still opens, the world is not drawn, and the status bar says why (`Map <name> is not installed`,
or the reason the download failed). TF2 itself refuses such a demo. The status-bar message is unverified on
every route that can end without a map (B556, `RELEASE-NOTES.md` *Known gaps*), so a silent bar does not prove
the map loaded. If the viewer found no TF2 install at all, the status bar says that instead:
maps and models cannot be loaded, the demo still plays, and a map in the viewer's own maps folder is still used.
If the map file is there but cannot be read, the status bar says `Map <name> could not be read` and why.

**A demo will not open.** A file that is not a demo exits with the reason on standard error when you use
`--shot` or `--measure`. Demos recorded in late June 2011 were malformed by the game itself and may not decode.
Send the log.
