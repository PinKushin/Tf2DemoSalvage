# Recording checklist for period clients

What to do while recording a demo on an old TF2 build, so that every era specimen exercises the
same things and a difference between two of them is an **era difference rather than a difference
in what happened that day**.

The era demos so far were recorded ad hoc. That is why `svc_VoiceData` had no coverage until the
2007 session, why chat has never been captured below protocol 24 except once, and why the voice
client-to-player mapping is still unresolved — the one recording that carries voice has a single
speaker in it. None of those are hard to capture; they were simply not on a list.

**Run the whole list on every build.** Two to four minutes is enough (see the corpus manifest on
why era specimens stay short), and a build that refuses a step is itself a finding.

---

## Before you start

```
tv_enable 1          // before the map loads, or SourceTV will not attach
map <mapname>
```

Record **both** points of view of the same session where possible:

```
record pov<year>     // POV: carries dem_usercmd and dem_consolecmd
tv_record stv<year>  // SourceTV: carries neither, and is what a real match archive looks like
```

**A step the build rejects is data, not a failure.** An unrecognised console command prints
`Unknown command: <name>` and that line travels into the demo as a `TextMsg` — which is how we
know the 2007 client has no `cl_crosshairscale`. Note what it refused rather than skipping it
silently.

---

## Solo — everything below works with one person on a listen server

| # | Do this | What it exercises |
|---|---|---|
| 1 | `say hello from <year>` | `SayText2` → chat decoding, per era |
| 2 | `say_team team line` | the chat message's destination/kind field |
| 3 | `name Пётр大将` then `say hi` | UTF-8 through `userinfo`, chat, and the header |
| 4 | Hold the mic key and talk for ~10s | `svc_VoiceData`, `svc_VoiceInit` |
| 5 | `name something_else` mid-demo | `svc_UpdateStringTable` on `userinfo` — the mid-game roster path (B22), and `TextMsg #TF_Name_Change` |
| 6 | Change class at least three times | different entity classes, different flattened property sets |
| 7 | Fire every weapon you have at a wall | `svc_TempEntities`, `svc_Sounds`, `svc_BspDecal` (decals) |
| 8 | Blow yourself up (`explode`) | `player_death`, `player_hurt` with attacker == victim |
| 9 | `kill` | `player_death` without damage |
| 10 | Take fall damage, then pick up health and ammo | `player_hurt`, `ItemPickup` |
| 11 | Taunt, and let it run to completion | `PlayerTauntSoundLoopStart` / `End` |
| 12 | As Engineer, build and then destroy each building | `BuiltObject`, object entities entering and being deleted |
| 13 | Capture a control point | `teamplay_point_captured` and friends |
| 14 | `mp_restartgame 1` and let the round restart | round events, `TextMsg` announcements, a second full snapshot |
| 15 | Break a breakable prop if the map has one | `BreakModel` / `CheapBreakModel` |
| 16 | Die and spectate — cycle with `spec_next` | `svc_SetView`, spectator entity state |
| 17 | `pause`, wait, `unpause` | `svc_SetPause` |
| 18 | Type a few unknown commands on purpose | `TextMsg` bodies, and it dates the build's cvar set |

## Needs a second player — the open questions live here

These cannot be done alone, and each one is currently blocking something specific.

| # | Do this | Why it matters |
|---|---|---|
| 19 | **Two people talking, one after the other** | The only way to settle how `svc_VoiceData`'s client slot maps to a player. One speaker cannot distinguish "client == entity" from "client + 1 == entity"; the 2007 demo has one speaker and contradicts the widely repeated +1 rule |
| 20 | One player joins **after** the recording starts | The mid-game join path (B22) against a real update rather than a name change |
| 21 | One player disconnects, another takes the slot | Slot reuse — whether a re-used entity index reports a new serial number |
| 22 | Kill each other several times | `player_death` with distinct attacker and victim, which is what makes a kill attributable |
| 23 | Medic übers the other player | condition properties, which are where a viewer reads game state from |
| 24 | Both talk **at the same time** | Interleaved voice packets from two clients |

**Bots are not a substitute before ~2010.** TF2's own bots (`tf_bot_add`) arrived years after
launch, so for the 2007, 2008 and 2009 builds a second player means a second machine or a second
Steam session on the LAN. Where bots do exist, note it — a bot's `userinfo` record sets the fake
player flag, which is its own thing worth having.

---

## After recording

Check what actually landed rather than assuming, since several of these produce nothing visible
in game:

```bash
tf2demosalvage <demo> --summary        # chat lines, players, event counts
tf2demosalvage <demo> --trace -o t.txt # every message in stream order
```

Then grep the trace for the ones with no in-game feedback — `svc_voicedata`, `svc_bspdecal`,
`svc_setpause`. A step that produced no message is worth knowing about immediately, while the
build is still set up.
