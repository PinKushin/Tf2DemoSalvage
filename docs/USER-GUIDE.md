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

**Cheat cvars need `sv_cheats 1`, as in TF2.** A cvar TF2 marks as a cheat (`cl_drawhud`, `mat_fullbright`,
`r_screenfademinsize` and others) is refused from a config, the console or the command line while `sv_cheats` is `0`, and
the log shows TF2's own line: *Can't use cheat cvar … in multiplayer, unless the server has sv_cheats set to 1.* Set
`sv_cheats 1` first, for example `tf2demoview <demo> +sv_cheats 1 +cl_drawhud 0`. Setting `sv_cheats` back to `0` returns
every cheat cvar to its default. The viewer's own keys (`CTRL+0`, `CTRL+1`, `CTRL+2`) and its menus still work without it.

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

`cl_threaded_bone_setup` poses the skeletons the frame is about to draw on the worker threads before the draw, like TF2's
setting of that name. The default is `1`, which is this viewer's choice: TF2 ships it at `0`. The picture is the same
either way. To turn it off, put `cl_threaded_bone_setup 0` in `settings.cfg`, or for one run use
`tf2demoview <demo> +cl_threaded_bone_setup 0`. In a team fight on the author's recording the pose step got cheaper by
0.3 to 0.7 ms a frame and the frame rate rose 3 to 15% (`docs/measurements/2026-11-06-detail-sprites-visible-leaves.md`).

`r_shadowrendertotexture` draws the shaped shadows of players, buildings, projectiles and dropped items on the map's
floors, walls and displacement terrain, as TF2 does with the same setting. The default is `1`. The map's `shadow_control`
entity sets the direction, color and length, as in TF2. `r_shadowrendertotexture 0` does not turn the shadows off: as in
TF2, every shadow becomes the simple round blob, cast straight down. `r_shadows 0` turns every shadow off, blob and shape
alike, as in TF2; the default is `1`. Put it in `settings.cfg`, or for one run use
`tf2demoview <demo> +r_shadowrendertotexture 0`. They cost about 0.7 ms a frame in a busy fight. Shadows also fall on
doors, platforms and other moving brush pieces; props such as crates and barrels take none, as in TF2.

Past a limit, or when the shadow texture has no room left, a shadow becomes TF2's soft round blob instead of
disappearing. `r_shadowmaxrendered` sets that limit per view; the default is `32`, as in TF2. `cl_blobbyshadows 1` makes
players cast the blob from their collision box, straight down; the default is `0`. Both are read from your TF2 config,
or from `settings.cfg`, or for one run from the command line (`tf2demoview <demo> +r_shadowmaxrendered 160`).

`mat_phong` and `mat_bumpmap` turn off two of TF2's model and surface effects, as TF2's settings of those names do. The
default for both is `1`. `mat_phong 0` removes the shiny highlight and rim light from players, weapons and props.
`mat_bumpmap 0` stops drawing bump maps (the fine surface detail on walls, floors and models) and also removes the
highlight and rim light from models; TF2's own `low.cfg` sets it. Some models then show their reflections differently,
as they do in TF2. Both are read from your TF2 config, or from `settings.cfg`, or for one run from the command line
(`tf2demoview <demo> +mat_bumpmap 0`).

`mat_specular 0`, also the **Reflections** item in the menu, turns off the cubemap reflections the way TF2's setting of
that name does: on walls, floors and models that have a base texture. As in TF2, some reflections stay: 686 of
TF2's cosmetic and weapon materials (counted on the November 2026 game files) keep theirs while `mat_phong` is on, and so do water, glass and eyes and the few
reflective surfaces with no base texture. The default is `1`.

Dead bodies and gibs cast shadows. An entity the server marks as casting or receiving no shadow (`EF_NOSHADOW`,
`EF_NORECEIVESHADOW`) does neither. Shadows fade with an entity's fade or cloak, sentries and other animated props are
redrawn as they animate, and the Halloween bosses switch between the blob and a full shadow by distance, as in TF2.
Shadows are worked out separately for the main view, the monitor (camera screen), the 3D skybox and water reflections, as
in TF2.

Each of those views also draws its own models, particles, sprites, ropes, detail props and translucent surfaces, and a map
with several active camera screens draws every one. Entities fade by their size on screen as well as by distance, as in
TF2; `r_screenfademinsize` and `r_screenfademaxsize` are read from your config, and as cheat cvars they need `sv_cheats 1`
(above). A texture or material that cannot be found draws as TF2's magenta-and-black error texture.

`demo_retimeview` smooths the first-person camera of a point-of-view demo recorded over a jittery connection. TF2 spreads
the camera's position along the time each packet reached the recording client, which is uneven on such a connection; with
this setting the viewer spreads the position along the server's own tick instead. The view angles are left as TF2 plays
them. The default is `1`. For TF2's exact camera, put `demo_retimeview 0` in `settings.cfg`, or for one run use
`tf2demoview <demo> +demo_retimeview 0`. On one stuttering demo, frames more than 50% off their neighbors fell from 20.7%
to 3.2% (`docs/measurements/2026-10-27-retime-view-by-server-tick.md`). It has costs and limits, listed in
`RELEASE-NOTES.md` under *Known gaps*. A SourceTV demo is unchanged by it.

Opening a map takes about half a second longer than in the last release, because the viewer now builds every static
prop's lighting at load, as TF2 does, so that none is built while the demo plays
(`docs/measurements/2026-10-27-static-prop-handle-at-load.md`).

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
- Some options of TF2's highest-quality setups are missing: edge smoothing (`mat_antialias`),
  the flashlight's shadows, water quality options and a few effect options (B701 to B705). They are planned for after the viewer is faster.
- With `demo_retimeview` on (the default), a point-of-view camera can stall or snap more often in places than TF2's
  own, in exchange for the smoother pace. `demo_retimeview 0` gives TF2's camera.

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
