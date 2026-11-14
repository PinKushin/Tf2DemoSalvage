# 64 — The HUD is VGUI, and half of it is closed

**Question.** What does it take to draw TF2's HUD the way the game draws it, custom HUDs included?

The published SDK holds the controls (`vgui2/vgui_controls`: Panel, EditablePanel, BuildGroup, Label, TextImage,
ImagePanel, ScalableImagePanel) and the client's HUD classes. It does not hold the parts under them: the panel tree and
the scheme (`vgui2.dll`), the surface and the fonts (`vguimatsurface.dll`), or the localised strings. Those were read from
the shipped binaries, in Ghidra projects `tf2vgui2` and `tf2vguimatsurface`. The code remarks name each function by
address. What follows are the findings that someone would otherwise "fix".

## Engine behaviour worth keeping

- **Positions measure against the screen, not the parent**, unless the block sets `proportionalToParent`.
  `proportional_int` and `proportional_float` scale even on a panel that is not proportional, and the float passes
  through an int. *Read from published source.*
- **The first `GetCharABCwide` answer is GDI's own, and later answers are widened** by the font's effects. So the first
  string measured in a new font is measured narrower than every later one. *Disassembly.*
- **The blur's far-edge window never reaches the centre pixel.** *Disassembly.*
- **`CFPSPanel` sits at x = wide − 300, with no clamp.** On a narrow window it runs off the left edge. *Published source.*
- **A `Bitmap` with a rotation draws at the panel's origin, not at its position.** `Paint` (0x180002290) builds the
  rotated polygon from 0,0 and never adds `SetPos`. *Disassembly; no stock HUD file sets `rotation`.*
- **A localisation conditional's `!` is read in two places, and one of them never sees it.** `AddFile` (0x180008f80)
  accepts a conditional only as a token beginning `[$`. For a language name it reads `!` after the `$`. For anything else
  it passes the token to `EvaluateConditional` (0x18001e920), which reads `!` only straight after the `[`. A `[$` token
  never has one there, so `[$!X360]` names no platform the evaluator knows and comes out false. The shipped
  `tf_english.txt` has no conditionals at all. *Disassembly, and a count of the shipped file.*
- **A localisation file without the UTF-16 byte order mark is ignored**, with the message "Ignoring non-unicode close
  caption file", whatever the file is. A token added twice keeps the later value (`AddString`, 0x180009ed0).
  *Disassembly.*

## A wrong turn: an empty scheme is white

The first frame with the HUD root wired was solid white. The viewer builds VGUI before the install is open, so
`ClientScheme.res` was read as nothing. An empty scheme answers `Panel.BgColor` with the white fallback, and the
viewport painted it across the screen. It then kept it, because only a new screen size loads a scheme again. This is how
the engine behaves too: `Panel::ApplySchemeSettings` asks with a white default. So the fix is to build no VGUI until the
files exist, not to change the fallback. A `hud` probe — the production `VguiHud` on the install — drew no quads, which
ruled out the layout. One logged frame of the viewer showed a single fill the size of the window. *Measured, with a
control shot taken with the HUD switched off.*

## Whose health the HUD shows, and when it shows none

The first health number the viewer drew on f12 was "1". f12 is a SourceTV recording, and there the local player is the
SourceTV client itself: entity 1, team 1, `lifeState` 2, `m_iHealth` 1. The game does not draw that 1, because
`StartObserverMode` sets `m_Local.m_iHideHUD = HIDEHUD_HEALTH` (player.cpp:2298), and the demo carries it: 2058 on
every tick sampled. The HUD reads the player's own `m_iHealth`, not the player resource's (which the scoreboard reads).
`GetMaxBuffedHealth` is the resource's `m_iMaxBuffedHealth` — "actually m_iMaxHealthForBuffing, but we can't fix it now
because of demos" (c_tf_playerresource.h:81) — times 1.5, floored to a multiple of 5. On the 2013 POV specimen at tick
1000 the recorder is at 125 of 125 with `m_iHideHUD` 2050, and the viewer draws 125. *Measured on the corpus.*

## The clip arrives one too high, and scripts write keys bare

The first ammo reading on the 2013 POV specimen was a 7-round scattergun. `m_iClip1` is
`SendPropIntWithMinusOneFlag`, which sends through `SendProxy_IntAddOne` (sendproxy.cpp:39) so that -1, "no clip", fits
an unsigned field; the client's `RecvProxy_IntSubOne` (recvproxy.cpp:26) takes the one back off. The wire said 7, the
game shows 6. *Read from published source, measured on the corpus.*

The same session found that `WeaponScript.Value` could not read the rocket launcher's clip at all: its script says
`clip_size 4` without quotes, which KeyValues accepts and a quoted-pair scan never matches. Every weapon script is now
read as KeyValues, and only the top block's keys answer — a scan would also have answered with a key from `SoundData`.

Max clip and max ammo go through the attribute system (`CALL_ATTRIB_HOOK_INT`), which reads the item attributes the
demo carries for each of the recorder's weapons and wearables (`m_hMyWeapons`, and `m_hMyWearables`, whose size is
stored under its `lengthproxy` path rather than a flat name). A weapon's hook skips the owner's other weapons —
"Don't allow weapons to provide to other weapons being carried by the same person" (attribute_manager.cpp:467).

**SourceTV is handed the owner-only table too — the prediction was wrong.** `SendProxy_SendLocalWeaponDataTable`
(basecombatweapon_shared.cpp:2739) sets its recipients to the owner alone, so the expectation, written into a marker
test for months, was that an STV recording would lack `m_iClip1` and a POV one of the same session would have it. On the
2011 koth_viaduct pair both carry it: the STV file holds the owner's clip on 40 of 51 sampled ticks, and SourceTV's own
spectator slot holds none. The recipient filter is applied per client by the closed engine, and the SourceTV client is
evidently not filtered by it. *Measured on the corpus* (`AudienceSplitCorpusTests`); the engine-side reason is
*interpolated* and unread. What an STV viewer's HUD shows for an in-eye target therefore has the data it needs.

## Three things the animation controller does that its code does not say

`AnimationController.cpp` is published, and three behaviours in it are not what they look like:

- **An animated int never starts where it was.** `GetValue` reads a non-built-in variable through `RequestInfo` and
  keeps only a float or a colour; an int or bool variable reports as `TYPE_INT`, so the animation starts from 0.
- **A duplicated event keeps its first definition,** under a comment that says "replacing the old one". The loop removes
  the new sequence — which changes nothing anyway, because every lookup already stops at the first of a name.
- **`Bias` never caches.** mathlib's `Bias` keeps `lastAmt` to skip a `log`, and never assigns it, so the exponent is
  recomputed every call — except for an amount of exactly -1, which matches the stale -1 and reuses the previous
  exponent.

The stock manifest's two scripts declare 311 events; 309 parse, and the two missing are `[$X360]` variants of
`WeaponUsesClips` and `WeaponDoesNotUseClips`. *Read from published source, counted on the shipped files.*

## Three round-timer and ammo details the port had from memory rather than the header

A parity audit (2026-10-04) re-read `tf_time_panel.h/.cpp` and `tf_hud_ammostatus.cpp` against the port and found three
divergences, each settled by the published source and fixed the same day (B466-B468). *Read from published source.*

- **Ten floating deltas, not two.** `NUM_TIMER_DELTA_ITEMS` is 10 (`tf_time_panel.h:57`) and `Paint` walks all ten
  (`tf_time_panel.cpp:843`). The port kept two, so a third `teamplay_timer_time_added` inside the 2-second
  `delta_lifetime` overwrote a delta still on screen. Capture and cart bonuses arrive in bursts, so this was reachable.
- **Setting the timer index is not a plain store.** `SetTimerIndex` clamps a negative index to 0 and calls
  `SetExtraTimePanels()` (`tf_time_panel.h:77`). The match status calls it when its timer goes invalid
  (`tf_hud_match_status.cpp:497`), and KOTH calls it when the entity index differs from the panel's (`tf_time_panel.cpp:1063`,
  `:1080`). The port assigned a property, so the setup, waiting, overtime and sudden-death labels waited for the next
  `teamplay_update_timer`. **This project's own test asserted that wait** (`…InSetupBeforeTheTimersUpdate_LeavesTheSetupLabelHidden`),
  with a comment explaining why the update was needed. The test was written from the port rather than from the header, so it
  agreed with whoever wrote the port. It now asserts the label shows on the think that points the panel at the timer.
- **The ammo count hides for a Halloween ghost, an active minigame and the match summary** (`tf_hud_ammostatus.cpp:153-160`).
  `CTFHudPlayerStatus` beside it already tested all three, so the two sibling elements disagreed under the summary.
- **The crosshair makes the same two tests, first** (`tf_hud_crosshair.cpp:67`, `:70`), ahead of the ghost and taunt
  checks the port already had. Its own remarks listed them as not modelled; the inputs (`m_bShowMatchSummary`, the
  minigame logic's active minigame) were already decoded for the ammo count, so the gap was one condition (B500).
  *Read from published source.*
- **`FL_FROZEN` is not one bit across TF2's history.** The orangebox `const.h` has nine player flags and `FL_FROZEN` at
  `1<<5`; inserting `FL_ANIMDUCKING` at `1<<2` pushed it to `1<<6` and the list to eleven. The demo says which it was
  written with, because `m_fFlags` is sent in `PLAYER_FLAG_BITS`: 9 bits in the 2007-2009 specimens, 11 in 2011 and 2013,
  32 in 2026 (B500). *Measured with the `schema` probe, read from published source.* A constant `1<<6` reads a 2008
  player's `FL_ATCONTROLS` as frozen.
- **The Ambassador's crosshair rests at three quarters size**, not full: `RemapValClamped( t, 1.0, 0.5, 0.75, 2.5 )` of
  the time since the last shot, so it blooms to 2.5× right after firing and shrinks back over the next half second.
  Its timer is prediction-only, but it is set on the same call and curtime as the networked `m_flLastFireTime`, which
  is what a demo can carry (B500). *Read from published source.*
- **The competitive countdown hides the crosshair**: a `restart_timer_time` of 1 to 10 seconds hides it until that many seconds later, in ladder and casual
  matches but not MvM — `IsCompetitiveMode()` counts casual (B500). *Read from published source.*

## The spectator GUI: the tournament cards are a watcher's setting, not the demo's (B516)

The golden comparison (findings 76) showed TF2 drawing both teams' player cards over a SourceTV demo, and the viewer
drawing none. The first reading was "STV in tournament mode draws the tournament HUD". **That is half right.**

- **Two routes open the panel.** `specgui` is a viewport panel, not a `CHudElement`: `CBaseViewport` shows it at
  `game_newmap` when `engine->IsHLTV()` (baseviewport.cpp:706) — the demo's ServerInfo `is_hltv`, carried as read, not
  inferred from the absence of a recorded view — and for a player the server sends `ShowViewPortPanel( "specgui", ... )`
  as a `VGUIMenu`: on `StartObserverMode` (player.cpp:2294, closed on death and freeze cams by TF's
  `ModeWantsSpectatorGUI`), after the freeze cam (tf_player.cpp:13992), and closed on `StopObserverMode` (:2260). A POV
  demo carries those messages. `CSpectatorGUI::OnThink` hides it under the match summary. *Read from published source;
  the POV messages measured on rgl-pug-2026-08-10-pov.*
- **Which layout is the watcher's choice.** `GetResFile` (tf_spectatorgui.cpp:1037) loads `SpectatorTournament.res` only
  when `InTournamentGUI` (:1060): `mp_tournament`, NOT `IsCompetitiveMode()` (a matchmaking match), and
  `cl_use_tournament_specgui` or MvM. That ConVar is `FCVAR_ARCHIVE` with a default of **0**; the owner's `config.cfg` sets
  it to 1. A stock client draws `Spectator.res`'s black bars over the same demo. *Read from published source; the
  owner's value read from the running client.*
- **The cards come from the player resource**, not the players: `m_iHealth`, `m_iMaxHealth`, `m_flNextRespawnTime`
  (server time) and `m_iChargeLevel`, ordered by `g_ClassDefinesRemap` — the class menu's order, scout first — and placed
  down two columns by the `.res`'s `team1_player_*` / `team2_player_*`. A spectator's team1 is BLU.
- **A card filled for a player already dead is not greyed.** `Reset` sets `m_bPrevAlive = false`, and only a CHANGE of
  alive sets the portrait's draw colour (tf_playerpanel.cpp:168), so the grey appears only after the player has lived and
  died while the card watched. The conformance test first predicted grey; the source said otherwise, and the source is
  what is kept. *Read from published source.*
- **A paused frame never fills a new card.** Cards are made by one `Update`, take their scheme on that frame's layout, and
  are filled by a later `Update` throttled to `curtime + 0.1`. A `--shot` is a paused frame whose curtime never passes the
  throttle, so the first viewer shot drew grey cards with the `.res`'s placeholder portrait. A card made since the last
  paint is now filled once, after the layout that gives it its scheme, on the frame it is first painted. *Measured on the
  golden pair; the fill on that frame is interpolated — TF2 makes it on its next update, a tenth of a second later.*
  The first version laid the GUI out on every skipped frame of a seek instead, which paid a layout per skipped frame to
  reach the same picture; review moved it to the one frame that needs it.

Golden pair, f12 (`tf/f12.dem`), TF2 tick 60003, gummo in-eye, Garm3n in both: names, health, respawn seconds, charge,
portrait and team border agree on all twelve cards. `golden-compare --rect 236,595,1045,720` (the card strip, the world
between the two columns included): mean error **38.846 → 23.590** /255, bad pixels 56.2% → 27.6%, from the grey-card shot
to the fixed one against the same TF2 capture. What does not agree is B546.

## The stock spectator bar read "map: cp_bridge" (B733)

A stock-HUD shot of the f12 SourceTV demo (`+cl_use_tournament_specgui 0`) drew the spectator top bar with
**"map: cp_bridge"** in its corner, `%tip%` at the bottom left, "Press [ %changeteam% ] to Change Team" in the middle and
`#TF_Spectator_ClassOrTeamKey` beside the camera captions. Every one of those is the shipped `Spectator.res`'s own
`labelText` — the `MapLabel` placeholder is literally `"map: cp_bridge"`, in lower case, where the game's string is
`"Map: %s1"`. Nothing set them: `CTFSpectatorGUI::UpdateKeyLabels` (tf_spectatorgui.cpp:452) was never ported, and the
class comment had filed "the key-binding labels' text" as not modelled. *The placeholder read from the shipped `.res`
in `tf2_misc_dir.vpk`; the strings from `tf_english.txt` and `hl2/resource/valve_english.txt`.*

What the engine sets, every `Update` while the panel is up, outside the tournament layout:

- **The map** (:685-709): `Q_FileBase( engine->GetLevelName() )`, lower-cased, through `GetMapDisplayName`
  (tf_gamerules.cpp:18974) into `szMapName[32]`, then `#Spec_Map`. `GetMapDisplayName` is a table first — `s_ValveMaps`
  and `s_CommunityMaps`, so `cp_badlands` is "Badlands" — and otherwise a clean-up: a trailing `_final`/`_final1` cut, a
  `workshop/` prefix and `.ugc<digits>` dropped, the mode prefix stripped, underscores made spaces, upper-cased ("tf maps
  are LOUD"). So f12's `cp_process_f12` is **"Map: PROCESS F12"**. One quirk worth keeping: `koth_` and `pass_` are tested
  against the name *before* `workshop/` is dropped (:19063), so a workshop KOTH map keeps its prefix. The level is the
  demo's ServerInfo map, now carried on the timeline. *Read from published source; the map name measured on the corpus.*
- **The tip** (:470-492): on SourceTV `#Tip_HLTV`, "You are watching SourceTV.", into `%tip%`, refreshed every 10 s or
  on a change of desired class.
- **The class-or-team hint** (:494-540): `#TF_Spectator_AutoDirector` on SourceTV ("Press [ %strafe% ] for Auto
  Director"), else `ChangeTeam` for a spectator outside arena, else `ChangeClass` — key bindings replaced. The text is a
  function static, so a token not found keeps the last one.
- **The camera keys** (:557-683): `[%jump%]`, `[%attack%]`, `[%attack2%]`, bound; a player on a team has them hidden with
  their captions under `mp_forcecamera` 1/2 or `mp_fadetoblack` (cycle keys only under 2 or fade). All of it, the map
  included, is skipped under `cl_hud_minmode` — where the `.res` hides the labels itself with `visible_minmode 0`.

**A second bug, found by the first shot after the port, and a third by review.** The map and tip came out right, but
every key label read `[< NOT BOUND >]`. `UTIL_ReplaceKeyBindings` (cdll_util.cpp:917) strips the `+` before it asks
`Key_LookupBinding`, so it asks for `jump`, and the viewer's lookup only knew `+jump`. Fixing the `+` alone left the hint
reading "Press [ < NOT BOUND > ] for Auto Director": the lookup searched only the viewer's own actions, and `+strafe`,
`changeteam` and `changeclass` are commands it never acts on — so a bind for them, though the config made it, was thrown
away. TF2's `config_default.cfg` binds them to ALT, `.` and `,` (lines 19, 57, 58). A bind is a bind whether or not the
viewer acts on it (D69), so the lookup now walks every bind the configs left, key to command text, the stored `+`
ignored. *Interpolated:* `Key_LookupBinding` is in the closed engine — what is read is its caller and the shipped strings
that would otherwise print nothing; with two keys on one command the first bind answers, where the engine walks button
codes in order.

**Two review claims that did not hold.** "The SwitchCamMode branch never runs because the constructor nulls the label"
(:97): `ApplySchemeSettings` finds it again in the loaded `.res` (:217), so it runs whenever the layout has one.
"Use `System.Text.Ascii.ToUpper`": it stops at the first non-ASCII character, where `Q_strupr` passes it through — tried,
and the `CP_CAFé_final` case reddened. And one that did: the spectator caller hands `GetMapDisplayName` the level's file
base, folder already gone, so its `workshop/` branch never fires from here — a workshop map reads `Map: X.UGC123` in TF2
too, and the viewer now passes the base rather than formatting `maps/%s.bsp` and stripping it back each frame.

**Not ported, and why:** a point-of-view recorder's class tip (`g_TFTips.GetNextClassTip`, tf_tips.cpp:143) needs
`m_iDesiredPlayerClass`, which the HUD state does not carry, and with no class chosen it is a `RandomInt` pick with a
hours-played-weighted abuse-report tip (:99-138) no viewer can reproduce; so a POV demo's tip label still shows `%tip%`.
The training branch needs `m_bIsInTraining`, not carried. B733.

## B546: of the three parts left over, one was already drawn, one was a missing override, one a missing reparent

B546 named three differences on the f12 golden pair: no control-point row, no number in the target ID's health
("150 : GUMMO" in TF2, ": GUMMO" here), and every card's health white where TF2 drew the overhealed ones blue. A
`--shot` taken on 2026-11-13 (f12, tick 60003, `--spectate gummo`, Garm3n, `cl_use_tournament_specgui 1`) settled
what each still was, before any code was touched:

- **The overheal colour was already right.** SILVER 235 and TOMI67 163 are blue, and KOEL 36 sits on the red
  `HealthBGLow` box. The cause B546 interpolated — a paused frame never running `HudHealthBonusPulse` — does not hold:
  the card's `SetHealth` starts the sequence (tf_hud_playerstatus.cpp:776-783), and its `Accel 0.0 0.0` colour landed
  on the shot. *Interpolated:* it lands on the next `UpdateAnimations` after the card is filled. One card did differ
  between two shots of the same frame: VITINHO's 150 was white in the first and blue in the second, so which frame of
  the paused shot fills a card still moves where in the pulse it is caught. The white box TF2 drew on one card is `HealthBGPlus`'s alpha flash
  (Garm3n's `HudAnimations_custom.txt`, `Linear 0.0 0.2` up then down, looping every 0.4 s): a moment in a loop, which a
  capture at another instant need not catch. *Measured on the shot; read from the HUD's own files.*
- **The control-point row was already drawn** once B709 ported `CTFHudObjectiveStatus` — Garm3n's `bottombarblank`
  sits at `ypos 480`, zero tall, so nothing covered it. **But on the stock HUD the bar covered it.** `CTFSpectatorGUI::
  ShowPanel` (tf_spectatorgui.cpp:791) takes the objective status in as its own child when the panel shows
  (`pStatus->SetParent( this ); pStatus->SetProportional( true );`, :809-813) and gives it back to the viewport when it
  hides (:869-872). Inside the spectator GUI the status is the newest child, so at an equal z it paints after the bar;
  left on the viewport, the spectator GUI — made after it by `CreateDefaultPanels` — paints its 70-tall brown
  `bottombarblank` over the icons. Ported as `TfSpectatorGui.ShowPanel`. *Read from published source; held by a
  corpus assertion that the icons paint after the bar on cp_process_f12; seen on a stock-HUD `--shot` of the same frame
  with `cl_use_tournament_specgui 0`, where the five icons sit on the brown bar.*
- **The target ID's health number was a missing override.** `CTargetID::DrawHealthIcon` (tf_hud_target_id.cpp:205)
  answers false for a player unless `tf_hud_target_id_disable_floating_health` is set — the floating icon above the
  player draws the health instead — and `UpdateID` ends `m_pTargetHealth->SetVisible( DrawHealthIcon() )` (:1009). But
  `CSpectatorTargetID` and `CSecondaryTargetID` both override it as `{ return true; }` (tf_hud_target_id.h:118, :138).
  The port had made it a plain method, so the spectator's health panel — whose Garm3n label `%Health%` is the "150" —
  was hidden for every player, and so was the medic's heal-target panel. It is virtual now, and both overrides return
  true. The other readers of it follow: the floating icon is not made for those two (:478), and their layout counts the
  panel's width. *Read from published source.*

**Review, 2026-11-13: what the reparent touches besides the paint order.**

- **A HUD element is registered once.** `CHud::AddHudElement` runs from the element's constructor
  (`DECLARE_HUDELEMENT`); a reparent registers nothing. The viewport here registered an element on every
  `OnChildAdded`, so each hide — the status coming back to the viewport — added it to the think list again, and a POV
  demo whose recorder died N times asked its `ShouldDraw` N+1 times a frame. `HudViewport` now registers a panel once.
  *Read from published source; held by `Think_AnElementReparentedAwayAndBackThreeTimes_IsAskedOnce`.*
- **The build groups are the engine's, leak included.** `EditablePanel::OnChildAdded` (EditablePanel.cpp:100) calls
  `SetBuildGroup`, which is `//TODO: remove from old group` then `PanelAdded` (Panel.cpp:3841), and `PanelAdded`
  skips a panel it already holds (BuildGroup.cpp:855). So after one show the status sits in both the viewport's group
  and `specgui`'s, for good: a later `Spectator.res` would restyle it by name if it had a `HudObjectiveStatus` block
  (neither the stock file nor Garm3n's has one), and `HudLayout.res` still reaches it while it is inside `specgui`.
  The port does the same and was left so. *Read from published source.*
- **A scheme reload while shown sizes it against the spectator GUI's old size, in the engine too.**
  `CBaseViewport::ReloadScheme` (baseviewport.cpp:714) applies `HudLayout.res` — `"wide" "f0"` for the status — and
  only then `InvalidateLayout( true, true )`, so the status takes the width of a parent that has not yet been resized.
  `CTFSpectatorGUI::ApplySchemeSettings` (tf_spectatorgui.cpp:209) ends `SetVisible( bVisible )`, not `ShowPanel`, so
  nothing reparents or resizes it again until the next hide and show. `VguiHud`'s reload keeps the same order, so it
  keeps the same staleness; in the viewer that is a window resize while the spectator GUI is up. *Read from published
  source; not measured.*
- **`SetProportional( true )` in `ShowPanel` is redundant**, there and here: `Panel::SetParent` already copies the new
  parent's flag (Panel.cpp:1462), and `specgui` is proportional. The line is kept to mirror :812; the test makes the
  status non-proportional first, so its assertion holds the reparent.

**Not ported, found on the way:** `CTFPlayerPanel::Reset` calls `m_pHealthIcon->Reset()` (tf_playerpanel.cpp:66), which
clears the health panel's animation state (tf_hud_playerstatus.cpp:702-709); here a card handed to a new player keeps
it, so a second overhealed player on the same card does not restart the pulse the first one began. The colour is the
same; only the loop's phase differs. Nor are the Engineer and Spy building panels that `ShowPanel` moves under the top
bar (tf_spectatorgui.cpp:815-829, :874-882), which this viewer does not draw.

## Custom HUDs are ordinary `.res` files

The owner's custom HUD draws its crosshair as a `CExLabel` created from `ControlName` in `hudlayout.res`, with a font
from its own `CustomFontFiles`. Nothing about it is special-cased. It is the build group making a control it was told to
make. That is why the control factory has to know TF's client classes, not just `vgui_controls`. *Measured: `hud custom`
lists the five crosshair labels.*
