# What TF2 gives you when it plays an STV demo

**The spec, owner-stated: whatever TF2 offers natively when watching a SourceTV demo, this should
offer too.** Not the graphics menu — that is `13-settings-parity.md` — but the playback and
spectator controls, which are the actual product.

This list is short because it is *documented*: the Valve Developer Community wiki covers the demo UI
and the spectator convars, so it should be read rather than recalled before any of it is built.

## Transport

| TF2 | what it does |
|---|---|
| `demoui` | the panel: play, pause, step a frame, scrub a timeline |
| `demo_timescale` | slow motion and fast forward |
| `demo_pause` / `demo_resume` | stop and start |
| `demo_gototick` | jump to a tick |
| `demo_togglepause` | the key people actually use |

Ours has play and a timeline already. Speed, single-step and go-to-tick are missing.

## Cameras — the part that forces a correct renderer

| TF2 | what it does |
|---|---|
| first person, `spec_mode 4` | through a player's eyes |
| chase / third person, `spec_mode 5` | behind and above a player |
| free roam, `spec_mode 6` | fly the map with no attachment |
| `spec_next` / `spec_prev`, or clicking a player | change who is watched |
| `spec_player <name>` | pick one directly |
| `spec_autodirector` | let the director choose the shot |

Every one of these except the transport is a camera **inside** the map, which is why the shader
parity work in `12-shader-parity.md` is not optional decoration. A top-down view forgives `$detail`
and `$bumpmap`; standing in a corridor does not.

## Overlay

| TF2 | what it does |
|---|---|
| scoreboard | score, class, ping |
| killfeed | who killed whom, with what |
| **chat** | `SayText2` and `TextMsg`, both already decoded here |
| `cl_drawhud` | turn the lot off, which is what frag-movie recording wants |

Chat is the one the owner named as missing from the first pass of this list, and it is nearly free:
the messages already come out of the reader, so it is a display question rather than a parsing one.

**Voice** belongs here too, and this project is further along with it than with anything else on the
page. TF2 shows a speaker badge beside whoever is talking; the badge is the easy half. The voice
data itself is already decoded here — the packets are unpacked and run through libopus — so the
viewer can do what the game does and also play it back, which no amount of watching a demo in TF2
gives you if you were not in that lobby.

Both are viewing rather than options: nothing to configure, just a question of what is drawn over
the picture.

## What this project can do that TF2 cannot

Worth stating, because it is the reason for building rather than patching the game: several players'
points of view at once, as a grid — the owner's "six on one screen like a security feed, twelve
maybe, click a feed to take that player's POV". TF2 renders one camera. Nothing about a demo file
prevents rendering several, and the camera being a matrix in a constant buffer is what makes that
cheap.

## Deliberately excluded

`cl_interp`, `cl_interp_ratio`, `cl_updaterate`, `cl_cmdrate` — interpolation and network timing
shaped the recording when it was made and are baked into the demo. Re-applying them at playback
would invent a second, different game. Also the DirectX level: this is DX11.
