using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the first-person view contains at one tick.</summary>
/// <param name="Props">The models to draw, the arms first.</param>
/// <param name="Changed">
/// Whether the weapon or its sequence differs from the last tick asked about, so a caller can log
/// once per weapon rather than once per frame.
/// </param>
/// <remarks>
/// **Empty means draw nothing, and that is a state rather than a failure.** First person is off, the
/// demo has no camera, or the followed player is holding nothing — each leaves the pass with no
/// props, and the caller drops its camera to say so.
/// </remarks>
public readonly record struct ViewmodelSceneResult(
    IReadOnlyList<SceneProp> Props,
    bool Changed);

/// <summary>Where a player's two viewmodels come from.</summary>
/// <remarks>
/// **An interface rather than <c>DemoTimeline</c>, and that is the difference between testable and
/// not.** A timeline is only constructible from a real demo file — <c>DemoTimeline.Build(bytes)</c>
/// is its only entry point — so a <see cref="ViewmodelScene"/> that took one could be exercised
/// only by opening a demo, which is how <c>AddViewmodel</c> came to have no tests at all.
///
/// This is D54's argument in miniature: *"MVP's boundary can be made a compiler error, not just a
/// convention someone (or something) has to remember to follow"*. Depending on the abstraction is
/// what lets a fake answer in two lines.
/// </remarks>
public interface IViewmodelSource
{
    /// <summary>The model in a player's main hand at a tick, or null when they carry none.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="player">The player whose view is being shown.</param>
    /// <returns>Their viewmodel, or null.</returns>
    public SceneViewmodel? MainHandAt(int tick, int player);

    /// <summary>The model in their other hand, which for TF2 is the spy's watch.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="player">The player whose view is being shown.</param>
    /// <returns>Their off-hand viewmodel, or null.</returns>
    /// <remarks>
    /// **Drawn as well as the main hand, not instead of it.** A cloaking spy has both on screen, so
    /// a viewer answering only with the main hand is one model short of what that player saw.
    /// </remarks>
    public SceneViewmodel? OffHandAt(int tick, int player);
}

/// <summary>A demo timeline, as a source of viewmodels.</summary>
/// <param name="timeline">The timeline.</param>
/// <remarks>
/// The whole adapter. It exists so <see cref="ViewmodelScene"/> can depend on the abstraction while
/// production still reads the demo — and so the naming difference between the two
/// (<c>ViewmodelAt</c> against <c>MainHandAt</c>) is resolved in one place rather than at every call.
/// </remarks>
public sealed class TimelineViewmodels(DemoTimeline timeline) : IViewmodelSource
{
    /// <inheritdoc/>
    public SceneViewmodel? MainHandAt(int tick, int player) => timeline.ViewmodelAt(tick, player);

    /// <inheritdoc/>
    public SceneViewmodel? OffHandAt(int tick, int player) =>
        timeline.OffHandViewmodelAt(tick, player);
}

/// <summary>
/// Decides which models the first-person view draws at a tick.
/// </summary>
/// <remarks>
/// **Extracted from <c>MainForm.AddViewmodel</c> on 2026-08-24** (B188). That method was 319 lines
/// inside a 7,263-line form, and it is what B170, B186 and B187 all have to change — so the owner's
/// call was to split it before fixing them rather than after: *"theres no need doing double work"*.
///
/// **It lives in Scene rather than in the viewer, and that is B184's half of the same job.** Nothing
/// here needs WinForms: it reads the timeline, names models and builds <c>SceneProp</c>s. Putting it
/// here means its tests are a plain <c>net10.0</c> project — which runs on the Linux measurement
/// boxes and under Stryker, neither of which the viewer's suite can do.
///
/// **What is NOT here is what genuinely needs a window**: which entity the camera follows, where
/// that camera is, packing geometry, and the render pass itself. Those stay in the form and their
/// results are passed in.
/// </remarks>
public sealed class ViewmodelScene
{
    /// <summary>The arms, or the weapon itself when the weapon is its own viewmodel.</summary>
    /// <remarks>
    /// Well past any real entity index, so it cannot collide with one the demo carries. The engine
    /// has no equivalent — a viewmodel there is a real networked entity — so these are this
    /// project's own numbering and are kept together for that reason.
    /// </remarks>
    public const int ArmsEntityIndex = 4096;

    /// <summary>The weapon, when it is a second model attached to the arms.</summary>
    public const int WeaponEntityIndex = 4097;

    /// <summary>The off hand, which is a separate viewmodel slot the engine also carries.</summary>
    public const int OffHandEntityIndex = 4098;

    /// <summary>The sequence a weapon attached to the hands plays: its own first, never the arms'.</summary>
    /// <remarks>
    /// **Zero because the engine never sets one** — see the citation where this is used.
    /// `C_ViewmodelAttachmentModel` is created, parented and skinned, and no code path calls
    /// `SetSequence` on it; its pose comes from merging onto the viewmodel's bones. A named constant
    /// rather than a bare `0` so the next reader finds the reasoning instead of assuming a
    /// placeholder.
    /// </remarks>
    public const int AttachmentSequence = 0;

    private (string Model, int Demo, int Played) _reported = (string.Empty, -1, -1);

    /// <summary>Builds the first-person scene for one tick.</summary>
    /// <param name="viewmodels">Where the player's two viewmodels come from.</param>
    /// <param name="tick">Which tick.</param>
    /// <param name="follower">The player whose eyes the camera is at.</param>
    /// <param name="at">Where that camera is, which every viewmodel prop is placed at.</param>
    /// <param name="hands">
    /// The class's hand model, or null when it has none. When the networked viewmodel IS this
    /// model, the weapon is a second model and <paramref name="heldWeapon"/> supplies it.
    /// </param>
    /// <param name="heldWeapon">The weapon's own viewmodel, for the two-model scheme.</param>
    /// <returns>The props to draw, and whether this differs from the last tick asked about.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewmodels"/> is null.</exception>
    /// <remarks>
    /// **Two schemes, and they are exclusive — this is the whole of the doubled weapon.**
    /// <c>CTFWeaponBase::GetViewModel</c> (<c>tf_weaponbase.cpp:651</c>) asks the item whether it
    /// <c>ShouldAttachToHands()</c>: if it does, the networked viewmodel is the player's ARMS and
    /// the gun is a separate <c>C_ViewmodelAttachmentModel</c> the client creates and parents to
    /// them (<c>econ_entity.cpp:1153</c>); if it does not, the networked viewmodel is the weapon
    /// itself and there is no second model. Drawing both is how one weapon becomes two on screen.
    ///
    /// **The demo's sequence is played, never one chosen here.** The recording says what the weapon
    /// was doing, and overriding it is this viewer inventing motion — the one thing it exists not to
    /// do. The owner's rule: *"we shouldnt be forcing any sequence only stuff from the demo or how
    /// valve does it"*. The engine agrees; nothing in its viewmodel path picks an idle.
    /// </remarks>
    /// <param name="seconds">Demo time now, which the animation's own clock is measured back from.</param>
    /// <param name="intervalPerTick">Seconds per tick, for turning the restart tick into a time.</param>
    /// <param name="teamOf">
    /// The team of an entity, asked about the WEAPON'S OWNER — `GetSkin( pOwner->GetTeamNumber() )`.
    /// Null leaves the skin at family 0, which is what a viewer that cannot say should draw.
    /// <para>
    /// **Asked about the owner rather than handed the followed player's team, and the difference is
    /// the whole bug** (B242). In first person the viewer follows *the recording's own camera* and
    /// `MomentInfo.Followed` is null, so a caller passing the followed player's team passed nothing
    /// — while the arms still drew, because they are the networked viewmodel's own model rather
    /// than the `hands` lookup that needs a followed player. Green tests, correct rule, no effect.
    /// </para>
    /// </param>
    public ViewmodelSceneResult Build(
        IViewmodelSource viewmodels,
        int tick,
        int follower,
        ViewmodelPlacement at,
        string? hands,
        string? heldWeapon,
        double seconds = 0d,
        float intervalPerTick = 0f,
        Func<int, int?>? teamOf = null)
    {
        ArgumentNullException.ThrowIfNull(viewmodels);

        // **The team's skin family, which nothing here set** (B242). `CEconItemView::GetSkin`
        // (`econ_item_view.cpp:975`) takes the owner's team and returns the per-team visual's skin;
        // family 0 is RED on every `c_` model that has two. Measured on the shipped models:
        //
        //   c_medigun.mdl     skin0 'c_medigun'       skin1 'c_medigun_blue'
        //   c_medic_arms.mdl  skin0 'medic_red'       skin1 'medic_blue'
        //                     skin0 'medic_hands_red' skin1 'medic_hands_blue'
        //
        // So a BLU player in first person saw red sleeves, red hands and a red medigun — the owner:
        // *"the 1st person pov always showing a red player viewmodel"*. The player's own BODY has
        // taken its skin from its team since `PlayerProps` was written; the viewmodel never did.
        //
        // **The divergence that remains, named rather than hidden:** Valve reads
        // `pVisData->iSkin` out of the item's per-team `visuals` block, which can name ANY family
        // and which a styled item overrides again. This takes RED 0 / BLU 1, which is what every
        // two-family `c_` model uses and what `PlayerSkin.ForTeam` already encodes for the body.
        // An item whose visuals name a third family draws its red one here.

        if (viewmodels.MainHandAt(tick, follower) is not { } weapon)
        {
            return new ViewmodelSceneResult([], Changed(string.Empty, -1, -1));
        }

        // **Now, less however long ago the animation restarted** — expressed as elapsed TICKS rather
        // than by rebuilding demo time from the tick, so it holds whatever the caller's clock is.
        // `m_nAnimationParity` is what says it restarted at all; see `ViewmodelAnimation.RestartAt`.
        double started = seconds - ((tick - weapon.AnimationStartTick) * intervalPerTick);

        // **The skin family, from the WEAPON'S OWNER** — `CEconItemView::GetSkin( iTeam, … )` takes
        // `pOwner->GetTeamNumber()`. Family 0 is RED on every two-family `c_` model, measured:
        // `c_medigun` skin0 `c_medigun` skin1 `c_medigun_blue`, `c_medic_arms` skin0 `medic_red`
        // skin1 `medic_blue`. Nothing set it, so every viewmodel drew red (B242).
        //
        // **Below the viewmodel lookup deliberately**, because the owner comes from it. A first
        // draft took the FOLLOWED player's team from the caller and had no effect at all: in first
        // person the viewer follows the recording's own camera and `MomentInfo.Followed` is null.
        // **Null when the viewmodel names no owner**, which era demos do — `_viewmodelsNameOwners`
        // exists because `m_hOwner` is not always sent. Family 0 is the honest answer there.
        int skin = PlayerSkin.ForTeam(
            weapon.OwnerEntityIndex is { } owner ? teamOf?.Invoke(owner) : null);

        // **Whether the networked viewmodel IS the weapon** decides where the item lives. Under
        // attach-to-hands the first prop is the ARMS — no item, no attributes — and the weapon is
        // the second prop below; otherwise this one model is the weapon itself and the pilot light
        // hangs off IT (B252).
        bool isHands = AttachesToHands(weapon.ModelPath, hands);

        List<SceneProp> props =
        [
            new SceneProp(
                ArmsEntityIndex,
                weapon.ModelPath,
                SceneModelKind.Studio,
                at.PoseFor(weapon.Sequence, weapon.PlaybackRate, started, skin),
                ItemDefinitionIndex: isHands ? null : weapon.WeaponItem,
                Econ: isHands ? null : weapon.WeaponEcon,
                FirstPerson: true),
        ];

        // **The comparison is a PATH comparison and the separators differ.** A model named in the
        // class schema and one that arrived over the wire disagree on slashes, so comparing them
        // raw answers "these are different models" for the same file — which silently selects the
        // one-model scheme and leaves the weapon undrawn.
        // **The model comes from the ITEM SCHEMA, and that is Valve's own source.** The attachment
        // is built as
        //
        //     pEnt->InitializeAsClientEntity(
        //         pItem->GetPlayerDisplayModel( iClass, pOwner->GetTeamNumber() ), … )
        //
        // (`econ_entity.cpp:1167`), and `GetPlayerDisplayModel` is `model_player` out of
        // `items_game.txt` — exactly what `WeaponModels.For` resolves. An attempt on 2026-08-28 to
        // "fix parity" by taking the weapon entity's own `m_nModelIndex` through
        // `DT_BaseViewModel.m_hWeapon` was WRONG and stopped the weapon drawing at all: `m_hWeapon`
        // names which weapon, and the model still comes from the item. Reverted the same evening.
        //
        // The lesson is the one the owner had already given: check the SDK before swapping a hop,
        // rather than inferring the hop from a memory and measuring afterwards.
        if (isHands && heldWeapon is { Length: > 0 } held)
        {
            // **The weapon does NOT take the arms' sequence, and TF2 is explicit about it** (B222).
            // A `c_` weapon is a `C_ViewmodelAttachmentModel` parented to the viewmodel, and nothing
            // in the engine ever calls `SetSequence` on it: it is created, parented, skinned, and
            // then posed entirely through the bone merge. Its own blending is
            //
            //   BaseClass::StandardBlendingRules( ... );          // its OWN default animation
            //   m_hOuter->ViewModelAttachmentBlending( ... );     // empty for all but two weapons
            //
            // (`econ_entity.cpp:890`, and the hook is `{}` at `econ_entity.h:125` — only the grenade
            // launcher's barrel and the minigun's spin override it).
            //
            // **Handing it the ARMS' sequence index is meaningless and sometimes harmful.** The two
            // models have unrelated sequence tables: `c_demo_arms` merges 74 sequences while
            // `c_stickybomb_launcher` carries exactly one, `idle`. So the moment the arms move to
            // anything but sequence 0 — which is what charging a sticky does, via
            // `SendWeaponAnim( ACT_VM_PULLBACK )` at `tf_weapon_pipebomblauncher.cpp:209` — the
            // weapon is asked for a sequence it does not have. Sequence zero is what the engine
            // leaves it on, and the merge is what actually places it.
            //
            // **Re-applied as parity, and now judged on its own** (B222). Handing the weapon the
            // ARMS' sequence index is wrong under any reading: the two models have unrelated
            // sequence tables — `c_demo_arms` merges 74, `c_stickybomb_launcher` carries one — so
            // every index above zero asks the weapon for something it does not have. The engine
            // never does this; the attachment keeps its own sequence and the merge places it.
            //
            // It matters most during a sticky charge, which is the one action that moves the arms
            // off sequence zero for a sustained period: `SendWeaponAnim( ACT_VM_PULLBACK )` →
            // `SendViewModelMatchingSequence` → `SetSequence`, `SetCycle(0)`
            // (`baseviewmodel_shared.cpp:357`). That is exactly when the weapon disappears.
            props.Add(new SceneProp(
                WeaponEntityIndex,
                held,
                SceneModelKind.Studio,
                at.PoseFor(AttachmentSequence, weapon.PlaybackRate, started, skin),
                AttachedTo: ArmsEntityIndex,

                // **Bone-merged onto the arms, and saying so is not optional** (B231). The comment
                // above already states it — *"the attachment keeps its own sequence and the merge
                // places it"* — but `SceneProp.BoneMerged` defaults to FALSE, so a construction
                // site that stays silent claims the opposite.
                //
                // That default is what broke the viewmodel: the held weapon fell into the
                // transform branch and was composed onto the arms' origin instead of merged onto
                // their skeleton. `DemoTimeline` was updated when the field was added and this site
                // was not, which is the shape `docs/memory/a-moves-regressions-are-wiring.md`
                // records — the field arrived, one caller set it, and the rest silently took a
                // default that is also a legitimate value.
                BoneMerged: true,

                // **The weapon's identity and attributes, so its attachments draw in first person
                // exactly as they do on the world weapon** (B252) — the pilot light and the
                // festivizer both key on these through the same delegate.
                ItemDefinitionIndex: weapon.WeaponItem,
                Econ: weapon.WeaponEcon,
                FirstPerson: true));
        }

        // **A player has two viewmodels, and the second is not a duplicate of the first.** Slot 1 is
        // the off hand — a spy's watch, a demoman's shield — and the engine draws it alongside the
        // weapon rather than instead of it.
        if (viewmodels.OffHandAt(tick, follower) is { } offHand)
        {
            // **Its own parity, not the main hand's.** The off hand is a separate viewmodel entity
            // with its own `m_nAnimationParity`, so a spy's watch restarts when the watch restarts —
            // borrowing the weapon's start would tie two independent animations together.
            double offHandStarted =
                seconds - ((tick - offHand.AnimationStartTick) * intervalPerTick);

            props.Add(new SceneProp(
                OffHandEntityIndex,
                offHand.ModelPath,
                SceneModelKind.Studio,
                at.PoseFor(offHand.Sequence, offHand.PlaybackRate, offHandStarted, skin),
                ItemDefinitionIndex: offHand.WeaponItem,
                Econ: offHand.WeaponEcon,
                FirstPerson: true));
        }

        return new ViewmodelSceneResult(
            props,
            Changed(weapon.ModelPath, weapon.Sequence, weapon.Sequence));
    }

    /// <summary>Whether the networked viewmodel is the class's hands rather than a weapon.</summary>
    /// <param name="viewmodel">What the demo says the viewmodel model is.</param>
    /// <param name="hands">The class's hand model, or null.</param>
    /// <returns>Whether the weapon is a second model.</returns>
    /// <remarks>
    /// Separators normalised because the two names come from different places — one from the wire,
    /// one from the class schema — and a backslash against a forward slash reads as two different
    /// models. That comparison decides which of two exclusive schemes applies, so getting it wrong
    /// does not throw: it draws the wrong number of models.
    /// </remarks>
    public static bool AttachesToHands(string viewmodel, string? hands)
    {
        ArgumentNullException.ThrowIfNull(viewmodel);

        return hands is { Length: > 0 } &&
            string.Equals(
                viewmodel.Replace('\\', '/'),
                hands.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether this differs from the last tick asked about.</summary>
    /// <remarks>
    /// **Once per weapon and sequence, not once per frame.** Measured 2026-08-24: the viewmodel
    /// lines printed 6,588 times each in two minutes — one set per frame — and the log reached
    /// 64,425 lines and 8.2 MB at roughly 1,280 writes a second (B163). What they answer is "which
    /// model, playing what", which is a question about a WEAPON and changes when the player
    /// switches. So holding one weapon for a minute is one line rather than nine thousand.
    /// </remarks>
    private bool Changed(string model, int demo, int played)
    {
        if (_reported == (model, demo, played))
        {
            return false;
        }

        _reported = (model, demo, played);
        return true;
    }
}

/// <summary>Where the first-person camera is, which every viewmodel prop is placed at.</summary>
/// <param name="X">Camera origin.</param>
/// <param name="Y">Camera origin.</param>
/// <param name="Z">Camera origin.</param>
/// <param name="Pitch">Camera angles.</param>
/// <param name="Yaw">Camera angles.</param>
/// <param name="Roll">Camera angles.</param>
/// <remarks>
/// **At the eye, which is where <c>CalcViewModelView</c> puts it.** Two offsets were tried while
/// chasing B160 and neither helped — pushing it 24 units forward, and rotating its yaw by −90 — so
/// it stays at the eye until the reason it is not visible is understood rather than guessed at.
///
/// A record of six floats rather than the viewer's camera type, so this assembly needs nothing from
/// the renderer.
/// </remarks>
public readonly record struct ViewmodelPlacement(
    float X, float Y, float Z, float Pitch, float Yaw, float Roll)
{
    /// <summary>The pose a viewmodel prop gets: this placement, playing that sequence.</summary>
    /// <param name="sequence">What the demo says the weapon is playing.</param>
    /// <param name="playbackRate">How fast, as the wire sent it.</param>
    /// <returns>The pose.</returns>
    /// <param name="animationStartSeconds">
    /// Demo time the animation restarted, from <c>m_nAnimationParity</c>; the cycle is measured from
    /// here rather than from demo time, as <c>m_flAnimTime</c> is.
    /// </param>
    /// <param name="skin">
    /// Which skin family, from the owner's team — <c>CEconItemView::GetSkin( iTeam, bViewmodel )</c>.
    /// </param>
    public ScenePose PoseFor(
        int sequence, float playbackRate, double animationStartSeconds = 0d, int skin = 0) =>
        new()
        {
            Skin = skin,
            X = X,
            Y = Y,
            Z = Z,
            Pitch = Pitch,
            Yaw = Yaw,
            Roll = Roll,
            Sequence = sequence,
            PlaybackRate = playbackRate,
            AnimationStartSeconds = animationStartSeconds,
        };
}
