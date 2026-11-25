# 114. The spectator's keys are exact bindings, not commands (B844)

How TF2 turns a key into a spectator camera action, read from the Source SDK 2013 client. Every claim here is **read**
from published source unless marked otherwise; nothing was measured in TF2.

## The wrong model this replaced

The viewer treated `+attack`, `+attack2` and `+jump` as the spectator's commands: any route that ran `+attack` (a key,
an alias, a longer binding, the launch line) cycled the camera. That is not where TF2 decides it.

## What the client does

`ClientModeShared::HandleSpectatorKeyInput` (`clientmode_shared.cpp:739-772`) is asked about every key event before the
engine adds the key's binding to the command buffer. It compares the key's **whole binding string** with `Q_strcmp`,
case-sensitive, and only on the press (`down`):

- exactly `+attack`: `engine->ClientCmd( "spec_next" )`, and the key is swallowed (`return 0`);
- exactly `+attack2`: `spec_prev`, swallowed;
- exactly `+jump`: `spec_mode`, swallowed;
- exactly `+duck`: the spectator menu panel (`:742-746`), swallowed (not in the viewer: B850);
- exactly `+strafe`: `HLTVCamera()->SetAutoDirector( true )` (`:762-769`), which is `spec_autodirector.SetValue( 1 )`
  (`hltvcamera.cpp:868-871`), swallowed.

So a swallowed key's binding never runs: no button is set, and an alias named `+attack` is never consulted. Any other
route to `+attack` (an alias whose body runs it, a binding with more after it, `+ATTACK`, the launch line) is the
ordinary button command from `in_main.cpp`, which sets `IN_ATTACK` and moves no camera.

`spec_next`, `spec_prev` and `spec_mode` are ConCommands of their own (`SpectatorGUI.cpp:786, 807, 828`). `spec_mode`
with exactly one argument (`ArgC() == 2`) sets that mode rather than cycling (`:847-851`).

TF2 overrides the function: `ClientModeTFNormal::HandleSpectatorKeyInput` (`tf/clientmode_tf.cpp:1589-1618`) takes only
an exact `+jump` while the local player is coaching, and passes every other key on (B857).

## What the owner decided above that

D266: a config that wraps `+attack` in an alias does what TF2 does with it, sets the button and moves no camera. The
owner's words are in `docs/DECISIONS.md` D266.

## What is still open

The release of a swallowed key (B854: whether `Key_Event` adds its `-` text is unread), the PVS lock (B853), the
auto-director left on by a cycle (B855), `spec_mode <n>` (B851) and the coaching override (B857).
