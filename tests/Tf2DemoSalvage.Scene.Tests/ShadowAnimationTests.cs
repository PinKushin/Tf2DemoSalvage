using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The animation a render-to-texture shadow keys its <c>ANIMATION_CHANGED</c> on (B723): the CYCLE the entity is animated
/// at, as <c>SetCycle</c> stores it (`c_baseanimating.cpp:5282-5289`), whether or not the entity is drawn this frame.
/// </summary>
/// <remarks>
/// The fixture loops once a second (<see cref="ModelFramesFixture.OneSecondLoop"/>), client-animated with a speed, so the
/// first frame's cycle is the closed form <c>seconds × rate</c> and later frames integrate (B172): at 0.5 s then 0.75 s the
/// cycle is 0.5 then 0.75. The wire cycle stays 0 throughout, so a key read off the wire is told apart; and at 0.75 a
/// frame-and-fraction key over the fixture's 31 frames would read 22.5, not 0.75.
/// </remarks>
public sealed class ShadowAnimationTests
{
    /// <summary>The cycle `Simulate` advanced to, exactly — not the wire's 0, not a frame index.</summary>
    [Test]
    public void ShadowAnimation_AClientAnimatedPropPosedTwice_IsTheCycleItIsAnimatedAt()
    {
        EntityModelSet models = new();
        SceneProp prop = Prop();

        Pose(models, prop, 0.5d, default);
        models.ShadowAnimation(prop).ShouldBe((0, 0.5f));

        Pose(models, prop, 0.75d, default);
        models.ShadowAnimation(prop).ShouldBe((0, 0.75f));
    }

    /// <remarks>
    /// **Advanced whether or not a view draws it**: `UpdateClientSideAnimation` walks every entity on the client-side
    /// animation list each frame (`c_baseanimating.cpp:6368`), and `FrameAdvance` sets the cycle there, before any view
    /// culls. A prop behind the camera, its box culled, still answers the cycle of this frame. The control: the culled
    /// prop's instance list is empty.
    /// </remarks>
    [Test]
    public void ShadowAnimation_APropTheFrustumCulls_StillAdvancesItsCycle()
    {
        EntityModelSet models = new();
        SceneProp prop = Prop();

        Pose(models, prop, 0.5d, LookingAwayFromIt()).ShouldBe(0, "the control: the frustum culls the prop");
        Pose(models, prop, 0.75d, LookingAwayFromIt());

        models.ShadowAnimation(prop).ShouldBe((0, 0.75f));
    }

    /// <summary>A prop whose model was never loaded is never simulated: its key is the wire's sequence and cycle.</summary>
    [Test]
    public void ShadowAnimation_APropNeverPosed_IsTheWireSequenceAndCycle()
    {
        new EntityModelSet().ShadowAnimation(Prop() with { Pose = new ScenePose { Sequence = 4, Cycle = 0.25f } })
            .ShouldBe((4, 0.25f));
    }

    /// <remarks>
    /// **On a `STUDIO_REALTIME` sequence the drawn pose moves and `m_flCycle` does not** (B723 remainder 1). The realtime
    /// clock is applied only where the bones are built — `CalcPoseSingle`'s first branch (`bone_setup.cpp:1955-1960`) and
    /// the IK rules' (`:3270-3275`) — and never written back to the entity. The shadow's dirt comes from `m_flCycle`
    /// changing: `SetCycle` (`c_baseanimating.cpp:5281-5288`), the interpolated cycle (`Interpolate`, `:4484-4486`) and a
    /// data update (`PostDataUpdate`, `:4720-4727`). A SERVER-animated entity's `m_flCycle` is its wire cycle, interpolated,
    /// so with the wire holding 0.25 its key holds 0.25 at 0.5 s and 0.75 s while its pose turns. A CLIENT-animated entity's
    /// `m_flCycle` is advanced by `FrameAdvance` (`:5492-5546`, `SetCycle` at `:5546`) whatever the flag, from the cycle it
    /// received: 0.25 + 0.5 = 0.75, then 0.75 + 0.25 wraps the loop to 0 — the control, and the reason the key cannot simply
    /// be the wire's for every realtime sequence. (The drawn phase is the clock's, 0.5 then 0.75, in both cases.)
    /// </remarks>
    [TestCase(false, 0.25f, 0.25f)]
    [TestCase(true, 0.75f, 0f)]
    public void ShadowAnimation_ARealtimeSequence_IsTheEntitysCycleNotTheRealtimeClock(
        bool clientSideAnimated, float first, float second)
    {
        EntityModelSet models = new();
        SceneProp prop = Prop() with
        {
            Pose = new ScenePose { X = 100f, Speed = 1f, PlaybackRate = 1f, Cycle = 0.25f },
            ClientSideAnimated = clientSideAnimated,
        };

        Pose(models, prop, 0.5d, default, Realtime).ShouldBe(1, "the control: the prop is posed");
        models.ShadowAnimation(prop).ShouldBe((0, first));

        Pose(models, prop, 0.75d, default, Realtime);
        models.ShadowAnimation(prop).ShouldBe((0, second));
    }

    private static int Pose(EntityModelSet models, SceneProp prop, double seconds, ViewFrustum frustum) =>
        Pose(models, prop, seconds, frustum, Boxed);

    private static int Pose(
        EntityModelSet models, SceneProp prop, double seconds, ViewFrustum frustum, Func<string, PropModels.ModelFrames> load)
    {
        SceneProp[] props = [prop];
        List<ModelInstance> into = [];

        models.Add(props, load);
        models.Instances(props, into, seconds: seconds, frustum: frustum);
        return into.Count;
    }

    private static PropModels.ModelFrames Boxed(string path) =>
        ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };

    private static PropModels.ModelFrames Realtime(string path) =>
        ModelFramesFixture.RealtimeLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };

    private static SceneProp Prop() =>
        new(
            7,
            "models/items/medkit_medium.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Speed = 1f, PlaybackRate = 1f },
            null,
            ClientSideAnimated: true);

    /// <summary>A camera at the origin looking down −x, so a prop at x = 100 is behind it.</summary>
    private static ViewFrustum LookingAwayFromIt() =>
        ViewFrustum.Perspective(
            origin: (0f, 0f, 0f),
            forward: (-1f, 0f, 0f),
            right: (0f, 1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 1f,
            farZ: 10000f,
            fovX: 90f,
            fovY: 60f);
}
