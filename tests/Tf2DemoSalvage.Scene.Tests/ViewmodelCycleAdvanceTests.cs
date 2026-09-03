using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A viewmodel runs its own cycle, and the prop it is built as has to say so.
/// </summary>
/// <remarks>
/// **<c>C_BaseViewModel</c> advances its cycle unconditionally**, from elapsed time rather than from
/// anything on the wire (<c>c_baseviewmodel.cpp:197</c>):
///
/// <code>
///   float elapsed_time = currentTime - m_flAnimTime;
///   ...
///   float dt = elapsed_time * GetSequenceCycleRate( pStudioHdr, GetSequence() ) * GetPlaybackRate();
///   if ( dt &gt;= 1.0f ) { if ( !IsSequenceLooping(…) ) dt = 0.999f; else dt = fmod( dt, 1.0f ); }
///   SetCycle( dt );
/// </code>
///
/// **This is a DIFFERENT mechanism from <c>m_bClientSideAnimation</c>**, which is what
/// <c>g_ClientSideAnimationList</c> membership turns on and what B280 was about. A viewmodel never
/// joins that list; it advances here instead, on its own, every frame.
///
/// **Both reach the same place in this project.** <c>EntityModelSet.Simulate</c> has one gate —
/// <c>prop.ClientSideAnimated</c> — deciding whether an entity's cycle is advanced from elapsed
/// time, and a viewmodel needs that branch for the reason above. Built without it, a viewmodel
/// holds frame zero of whatever sequence it was handed: the draw animation on a weapon switch never
/// plays, and neither does a reload or a fire. That is the owner's *"no weapon change animation"*.
///
/// **The decode was already right**, which is why this had to be looked for at the pose. Measured on
/// `z1800.dem`, player 25, at every weapon change: the sequence moves (25, 3, 45, 24, 2), the parity
/// moves, and `AnimationStartTick` restamps to the change tick. Everything the animation needs
/// arrives; nothing advanced it.
/// </remarks>
public sealed class ViewmodelCycleAdvanceTests
{
    private const int Player = 3;
    private const int Tick = 100;

    [Test]
    public void Build_AViewmodel_MarksEveryPropClientSideAnimated()
    {
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels
            {
                MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") with
                {
                    OwnerEntityIndex = Player,
                },
            },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null);

        scene.Props.ShouldNotBeEmpty();

        scene.Props.ShouldAllBe(
            prop => prop.ClientSideAnimated,
            "C_BaseViewModel advances its own cycle from elapsed time every frame, so a viewmodel " +
            "prop without this holds frame zero and the draw animation never plays");
    }

    /// <remarks>
    /// **The off-hand too, because it is a second construction site and they have diverged before.**
    /// A spy's watch and a medic's shield are built by a different branch of the same method, and a
    /// flag set on one path and not the other gives a weapon that animates in one hand and freezes
    /// in the other.
    /// </remarks>
    [Test]
    public void Build_AnOffHandViewmodel_IsAlsoClientSideAnimated()
    {
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels
            {
                MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") with
                {
                    OwnerEntityIndex = Player,
                },
                OffHand = Weapon("models/weapons/v_watch_pocket_spy.mdl") with
                {
                    OwnerEntityIndex = Player,
                },
            },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null);

        scene.Props.Count(prop => prop.ClientSideAnimated).ShouldBe(
            scene.Props.Count, "both hands are built by the same rule and must agree");
    }

    /// <remarks>
    /// **The control that keeps this from being satisfiable by setting the flag everywhere.** What
    /// the demo said still has to arrive: the sequence, the rate and the start. The start in
    /// particular is what B237 was about — an animation measured from the beginning of the
    /// recording is clamped to its last frame before it draws once.
    /// </remarks>
    [Test]
    public void Build_AViewmodel_KeepsTheSequenceTheDemoSaid()
    {
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels
            {
                MainHand = Weapon("models/weapons/v_rocketlauncher.mdl", sequence: 12) with
                {
                    OwnerEntityIndex = Player,
                },
            },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null);

        scene.Props[0].Pose.Sequence.ShouldBe(
            12, "the advance runs whatever sequence the wire named, not a substitute");
    }

    private static ViewmodelPlacement At => new(0f, 0f, 0f, 0f, 0f, 0f);

    private static SceneViewmodel Weapon(string path, int sequence = 0) =>
        new(path, sequence, 1f, null, null);

    /// <summary>A viewmodel source that answers with whatever the test set.</summary>
    private sealed class FakeViewmodels : IViewmodelSource
    {
        public SceneViewmodel? MainHand { get; init; }

        public SceneViewmodel? OffHand { get; init; }

        /// <inheritdoc/>
        public SceneViewmodel? MainHandAt(int tick, int player) => MainHand;

        /// <inheritdoc/>
        public SceneViewmodel? OffHandAt(int tick, int player) => OffHand;
    }
}
