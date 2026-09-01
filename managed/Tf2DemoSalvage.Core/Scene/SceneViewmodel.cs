namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The weapon model a player sees in their own hands.
/// </summary>
/// <param name="ModelPath">What to draw, as <c>modelprecache</c> named it.</param>
/// <param name="Sequence">Which animation it is playing.</param>
/// <param name="PlaybackRate">How fast, which is the third factor in the cycle advance.</param>
/// <param name="OwnerEntityIndex">
/// Whose it is, or <c>null</c> on a point-of-view recording where the demo does not say.
/// </param>
/// <param name="Slot">
/// Which of the player's two viewmodels this is: 0 for the weapon in hand, 1 for the off hand, and
/// <c>null</c> when the demo never sent the property.
/// </param>
/// <param name="Drawn">
/// Whether the engine would draw it, which is <c>EF_NODRAW</c> on the viewmodel's own table.
/// </param>
/// <remarks>
/// **Not a <see cref="SceneProp"/>, because it has nowhere to be.** A viewmodel's table is
/// declared <c>BEGIN_NETWORK_TABLE_NOBASE</c>, so it inherits no origin and no angles at all — the
/// demo names the model and the pose and the client puts it at the camera. Everything else in a
/// scene has a position; a model with none would be a prop every consumer had to special-case.
///
/// **The owner is null on most demos and that is correct rather than missing.** Measured across
/// the corpus: a point-of-view recording carries exactly one viewmodel and never names an owner,
/// because you only ever receive your own. A modern SourceTV recording carries one per player and
/// names every one. See <c>docs/findings/04-entities.md</c>.
/// </remarks>
/// <param name="WeaponItem">
/// The item definition index of the weapon this viewmodel is showing, found through
/// <c>DT_BaseViewModel.m_hWeapon</c> — the engine's own answer to "what is in this hand", where the
/// player's <c>m_hActiveWeapon</c> is a reconstruction of it. Null when the viewmodel names no
/// weapon or the recording never sent the handle.
/// </param>
/// <param name="WeaponClassName">
/// That weapon's entity class, for the stock-model route when it carries no item.
/// </param>
/// <param name="AnimationParity">
/// <c>m_nAnimationParity</c>, three bits, bumped by <c>SendViewModelMatchingSequence</c> every time
/// the server hands the viewmodel an animation — including the one already playing, which is the
/// case <see cref="Sequence"/> cannot express. Carried on the record so its value equality registers
/// a re-fire as a change; without it, firing twice records nothing and the animation never replays.
/// </param>
/// <param name="AnimationStartTick">
/// The tick the current animation restarted on, derived from <see cref="AnimationParity"/>. The
/// cycle is measured from here, which is <c>UpdateAnimationParity</c>'s <c>m_flAnimTime = curtime</c>
/// beside its <c>SetCycle( 0 )</c>.
/// </param>
/// <param name="WeaponEcon">
/// The held weapon's attribute inputs, from the entity <c>m_hWeapon</c> names, or <c>null</c> when
/// it has none (B252).
/// </param>
public readonly record struct SceneViewmodel(
    string ModelPath,
    int Sequence,
    float PlaybackRate,
    int? OwnerEntityIndex,
    int? Slot,
    bool Drawn = true,
    int? WeaponItem = null,
    string? WeaponClassName = null,
    int AnimationParity = 0,
    int AnimationStartTick = 0,

    // **The held weapon's attribute inputs, from the same entity `m_hWeapon` names** (B252).
    // Null when the viewmodel names no weapon or that weapon carries no attributes — the arms
    // themselves never have any. Rides here so the first-person weapon prop can answer
    // `IsFestivized` and the attachments delegate exactly as its world twin does.
    EconAttributeWire? WeaponEcon = null)
{
    /// <summary>The slot TF2 puts the weapon in the player's hands in.</summary>
    public const int MainHand = 0;

    /// <summary>The slot TF2 puts the spy's watch in.</summary>
    /// <remarks>
    /// <c>CTFPlayer::GetOffHandViewModel</c>: "off hand model is slot 1".
    ///
    /// **The watch is the only live user of it, and the SDK suggests otherwise.**
    /// <c>tf_weaponbase_grenade.cpp:74</c> also calls <c>SetViewModelIndex( 1 )</c>, which reads as
    /// a second case — but TF2's throwable grenades were cut before release. The class is still
    /// linked (<c>LINK_ENTITY_TO_CLASS( tf_weaponbase_grenade, CTFWeaponBaseGrenade )</c>) and no
    /// shipped item names it: the only <c>tf_weapon_grenade*</c> item class in
    /// <c>items_game.txt</c> is <c>tf_weapon_grenadelauncher</c>, the demoman's PRIMARY.
    ///
    /// Recorded because it is the shape of mistake this project keeps meeting: living code in the
    /// SDK that nothing shipped exercises, read as evidence about the game.
    /// </remarks>
    public const int OffHand = 1;

    /// <summary>Whether this is the weapon in the player's hands.</summary>
    /// <remarks>
    /// **An unstated slot is the main hand, not an unknown one.** <c>CBaseViewModel</c>'s
    /// constructor sets <c>m_nViewModelIndex = 0</c> (<c>baseviewmodel_shared.cpp:53</c>), so a
    /// property that never arrived means the engine's default rather than a missing answer — the
    /// distinction <c>docs/memory/sentinels-conflate-unknown-with-answer.md</c> is about, applied
    /// in the direction it actually points: on the wire, absent means the DEFAULT.
    ///
    /// Kept out of <see cref="Slot"/> itself so the record still reports what the demo said. The
    /// reader states the wire; the consumer applies the default.
    /// </remarks>
    public bool IsMainHand => Slot is null or MainHand;

    /// <summary>Whether this sample is one to put on screen.</summary>
    /// <remarks>
    /// **A slot-1 entity is not a watch in a hand.** Every player carries both viewmodels for their
    /// whole life, whether or not anything occupies the off hand — z1800 sends 23 of them in its
    /// first 400 snapshots, in a match with one spy. What separates "exists" from "draw it" is
    /// <c>EF_NODRAW</c>, which <c>CTFWeaponInvis::SetWeaponVisible</c> puts on the VIEWMODEL rather
    /// than on the weapon:
    ///
    /// <code>
    /// vm = pOwner->GetViewModel( m_nViewModelIndex );
    /// ...
    /// vm->AddEffects( EF_NODRAW );
    /// </code>
    ///
    /// A model path is still required, because index 0 means "no model" and a viewmodel sitting
    /// unused sends exactly that — 24 of them in the same sample.
    /// </remarks>
    /// <summary>Whether a demo viewer draws this viewmodel — <c>C_BaseViewModel::ShouldDraw</c>.</summary>
    /// <remarks>
    /// **On a demo, Valve's test has nothing to do with the entity's effects** (B222).
    /// <c>c_baseviewmodel.cpp:277</c>:
    ///
    /// <code>
    /// bool C_BaseViewModel::ShouldDraw()
    /// {
    ///     if ( engine->IsHLTV() )
    ///     {
    ///         return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &amp;&amp;
    ///                  HLTVCamera()->GetPrimaryTarget() == GetOwner() );
    ///     }
    ///     …
    ///     return BaseClass::ShouldDraw();
    /// }
    /// </code>
    ///
    /// Two conditions, both about the CAMERA: first person, and this viewmodel belongs to whoever
    /// is being watched. <c>BaseClass::ShouldDraw()</c> — the one that consults <c>EF_NODRAW</c> —
    /// is reachable only for a live client, never during playback.
    ///
    /// **This used to require <c>Drawn</c> as well as a model**, where <c>Drawn</c> is
    /// the effects flag. That is a condition this project invented for the demo path and the engine
    /// deliberately bypasses, so any moment the server marked the viewmodel <c>EF_NODRAW</c> —
    /// which TF2 would still draw through — took the whole viewmodel off screen here. The owner
    /// put it as a question the SDK then answered outright: *"why tf should we ever no draw the
    /// viewmodel, im pretty sure valve doesnt"*.
    ///
    /// **The model check stays**, and it is not the same claim. A viewmodel with no model index is
    /// holding nothing — an unused off hand sends exactly that, all 22 of z1800's — so it has
    /// nothing to draw rather than being hidden. The owner match is applied by the lookup that
    /// calls this, which is the other half of Valve's test.
    /// </remarks>
    /// **REVERTED 2026-08-28 pending the rebuild.** Dropping <c>Drawn</c> did not fix the reported
    /// dropout, and it was one of several unverified edits in one evening. The citation above is
    /// real and this change should be made again deliberately — with a test that shows what a
    /// viewmodel marked <c>EF_NODRAW</c> does on a demo — rather than carried along untested.
    public bool IsOnScreen => Drawn && ModelPath.Length > 0;
}
