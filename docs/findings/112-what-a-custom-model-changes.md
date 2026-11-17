# 112. What a custom model changes, and the icons over a head (B765)

`DT_TFPlayerClassShared` sends five fields beside `m_iszCustomModel`: an offset, a rotation, whether it rotates,
whether the rotation is set, and whether its player can see it (`tf_playerclass_shared.cpp:29-40`). Reading the engine
for each turned up three things about Valve's code that were not obvious from the fields. Evidence class: read from
published source throughout; nothing here is measured on a demo, because the corpus holds no custom model.

## A custom model's angles are not the feet's

`CTFPlayerAnimState::Update` returns early for a custom model without the class's animations
(`tf_playeranimstate.cpp:340-366`). It writes `m_angRender` from the set rotation, or from the eye yaw, or not at all
when the model does not rotate, then clears the animation state and returns, so the feet are never turned. Two
consequences that a first version missed, both caught in review:

- **`m_angRender` is per-client, per-frame state**, zeroed when the anim state is made (`multiplayer_animstate.cpp:56`).
  A held angle is the last one *drawn*, not the last tick's, so the viewer's per-frame feet object carries it.
- **The class's own path writes only the yaw** (`:1765`). A rotation's pitch and roll therefore outlive the custom
  branch on a player who goes back to the class's animations. It looks like a bug, but it is what the code does.

## A body nobody draws still carries its cosmetics

`ShouldDraw` hides a local player whose custom model is not visible to himself (`c_tf_player.cpp:8008-8012`), just above
the sniper zoom's test. The zoom also hides his cosmetics, because `C_TFWearable::ShouldDraw` repeats the zoom test
(`tf_item_wearable.cpp:338-340`). It does not repeat the custom-model test.

**The first version treated the two hides as one** and dropped the cosmetics with the body. What a cosmetic asks of its
wearer is `follow->DrawModel( 0 )` (`c_baseanimating.cpp:3236-3247`), which sets up the follow's bones and never asks
the follow's `ShouldDraw`. `C_TFPlayer::DrawModel` does not ask it either (`c_tf_player.cpp:6929-6994`). So TF2 draws a
hat on a body it hides. The viewer had no route for that: its bone-merge gate accepted only a wearer drawn in the same
pass. It now brings the hidden wearer up to date ahead of his pass, the way B729 does for attachments.

## The overhead icons are one map, and one of them never shows from a demo

The nemesis icon turned out to be one entry in `m_mapOverheadEffects`. The map also holds the Halloween IT icon, the
dueling icon, the Mannpower rune icon and `mark_for_death`. Their order is the `CUtlMap`'s node order, with a freed
node reused first. `UpdateOverheadEffects` spaces them 24 units apart across the line from the local player's eyes
(`c_tf_player.cpp:11616-11656`). So the nemesis icon cannot be placed without the others, and none was drawn.

- The IT, dueling and nemesis toggles sit inside `if ( !IsHLTV() )` (`:4557`). **A SourceTV demo never shows them.**
  The mark and the rune are toggled in `ClientThink` and show in either kind of demo.
- **The dueling icon never shows during demo playback.** `DuelMiniGame_IsDuelingLocalPlayer` compares against an
  opponent set only by Game Coordinator messages (`tf_consumables.cpp:1937-1946`, cleared at `:2133`), and a demo
  carries none. Its custom-model test is real code that never decides anything here.
- **The wrong turn:** the icons and the cosmetics were first filed as two new bugs so that B765 could merge. That is the
  split D261 refuses: both were parts B765's own entry named.
