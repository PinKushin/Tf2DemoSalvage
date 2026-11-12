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

    private static int Pose(EntityModelSet models, SceneProp prop, double seconds, ViewFrustum frustum)
    {
        SceneProp[] props = [prop];
        System.Collections.Generic.List<ModelInstance> into = [];

        models.Add(props, Boxed);
        models.Instances(props, into, seconds: seconds, frustum: frustum);
        return into.Count;
    }

    private static PropModels.ModelFrames Boxed(string path) =>
        ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };

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
