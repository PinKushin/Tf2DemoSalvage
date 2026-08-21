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
public readonly record struct SceneViewmodel(
    string ModelPath,
    int Sequence,
    float PlaybackRate,
    int? OwnerEntityIndex,
    int? Slot,
    bool Drawn = true)
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
    public bool IsOnScreen => Drawn && ModelPath.Length > 0;
}
