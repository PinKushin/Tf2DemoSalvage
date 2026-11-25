# 114. The spectator's keys are exact bindings, not commands (B844)

How TF2 turns a key into a spectator camera action. Evidence classes: **read** is the Source SDK 2013 client source;
**SDK contract** is what the SDK's own comments say the closed engine does with a return value, not read in the engine;
**interpolated** is flagged where it appears. Nothing here was measured in TF2.

## The wrong model this replaced

The viewer treated `+attack`, `+attack2` and `+jump` as the spectator's commands: any route that ran `+attack` (a key,
an alias, a longer binding, the launch line) cycled the camera. That is not where TF2 decides it.

## What the client does

`ClientModeShared::KeyInput` (`clientmode_shared.cpp:662-734`) is the client's say over a key event (**read**). With
the console visible it lets every key through untouched (`return 1`, `:664-665`); a chat bind starts message mode and is
swallowed (`return 0`, `:668-697`); with voting compiled in (`#ifdef VOTING_ENABLED`, `:700`) a key the vote HUD takes
is swallowed (`:704-707`). Only then is `HandleSpectatorKeyInput` asked, and only when there is a local player whose
observer mode is past the death cam (`:714-716`). That gate is what the viewer's `Spectating` stands in for (an open demo;
B845 is where it diverges). Before any of it, `CInput::KeyEvent` swallows `MOUSE_LEFT` and `MOUSE_RIGHT` while the camera
intercepts the mouse (`in_main.cpp:562-567`), which the viewer has no state for.

`HandleSpectatorKeyInput` (`:739-772`, **read**) compares the key's **whole binding string** with `Q_strcmp`,
case-sensitive, and only on the press (`down`):

- exactly `+attack`: `engine->ClientCmd( "spec_next" )`, returning 0;
- exactly `+attack2`: `spec_prev`, returning 0;
- exactly `+jump`: `spec_mode`, returning 0;
- exactly `+duck`: the spectator menu panel (`:742-746`), returning 0 (not in the viewer: B850);
- exactly `+strafe`: `HLTVCamera()->SetAutoDirector( true )` (`:762-769`), which is `spec_autodirector.SetValue( 1 )`
  (`hltvcamera.cpp:868-871`), returning 0.

Returning 0 swallows the key: the engine does not process it (**SDK contract**: `clientmode_shared.cpp:660`, "Return 1 if the
engine is allowed to handle it", and `in_main.cpp:557`, "Return 1 to allow engine to process the key"). So the binding's text
is not run, no button is set and an alias named `+attack` is not consulted. `Key_Event` itself (`engine.dll` build 1007
FUN_1801b6090) was not read for this path; whether it still adds a swallowed key's release text is B854.

Any other route to `+attack` (an alias whose body runs it, a binding with more after it, `+ATTACK`, the launch line) is
the button command from `in_main.cpp`, which sets `IN_ATTACK` (**read**). For the HLTV camera that moves nothing: it
reads `IN_SPEED` and no other button (`hltvcamera.cpp:387`, **read**). That every other route reaches the button command
rather than something else, through the engine's command dispatch and `stuffcmds`, is **interpolated** from the SDK.

`spec_next`, `spec_prev` and `spec_mode` are ConCommands of their own (`SpectatorGUI.cpp:786, 807, 828`, **read**). Each
returns unless the local player `IsObserver()` (`:790, 811, 832`); outside SourceTV each forwards to the server, which
returns during demo playback (`ForwardSpecCmdToServer`, `:766-769`); on SourceTV, `spec_next` and `spec_prev` do nothing
while the PVS is locked (`:796, 817`), and `spec_mode` swaps eye and chase while it is (`ToggleChaseAsFirstPerson`,
`:837-841`); unlocked, with exactly one argument (`ArgC() == 2`) it sets that mode, and otherwise cycles (`:843-851`). Those gates are B845, B851 and B853.

TF2 overrides the function: `ClientModeTFNormal::HandleSpectatorKeyInput` (`tf/clientmode_tf.cpp:1589-1618`, **read**)
takes only an exact `+jump` while the local player is coaching, and passes every other key on (B857).

## What the owner decided above that

D266: a config that wraps `+attack` in an alias does what TF2 does with it, sets the button and moves no camera. The
owner's words are in `docs/DECISIONS.md` D266.

## What is still open

The gates on the commands and the POV demo (B845), `spec_mode <n>` (B851), the PVS lock (B853), the release of a
swallowed key (B854), the auto-director left on by a cycle (B855), the coaching override (B857), and the visible-console
gate (B859).
