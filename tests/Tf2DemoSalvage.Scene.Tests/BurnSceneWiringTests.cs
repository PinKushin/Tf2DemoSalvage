using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That <c>MomentScene</c> supplies the burn delegate, and that it reaches the instance (B336).
/// </summary>
/// <remarks>
/// **The last hop, and the one nothing else watches** — the same shape as
/// `PaintSceneWiringTests`, and written because that file records the hop being lost in a move
/// once already. `ConditionProxyConformanceTests` proves the arithmetic against the engine; this
/// proves production assigns the delegate and that a burning player's value arrives on the drawn
/// instance.
///
/// **What this cannot show is a PICTURE, and the reason is the corpus rather than the code.** Every
/// demo in `lcor` that contains `TF_COND_BURNING` — `pl_upward_f12` at 146 episodes,
/// `cp_steel_f12` at 134 — is played on a competitive map version that is not installed, and the
/// viewer draws an empty frame for those. Every demo whose map IS installed is a 6s match with no
/// pyro in it: `cp_process_f12`, the parity reference, has **zero** burning player-frames out of
/// 1,380,278. Measured with the `conditions` probe, and recorded in `docs/RISKS.md` because it is a
/// gap in what the corpus can exercise rather than a gap in this work.
/// </remarks>
public sealed class BurnSceneWiringTests
{
    /// <summary>Entity index of the player these fixtures burn.</summary>
    private const int Burning = 3;

    [Test]
    public void Build_AWornItemOnABurningPlayer_ReachesTheDrawnInstanceAlight()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp hat = Hat();

        // At the peak: 0.3 seconds in is exactly where CProxyBurnLevel's ramp reaches 1.
        scene.Build([Player(burningFor: 0.3f)], [], default);

        List<ModelInstance> instances = [];

        models.Add([hat], _ => Frames());
        models.Instances([hat], instances);

        instances.ShouldHaveSingleItem().Burn.ShouldBe(
            1f,
            1e-4f,
            "MomentScene.Build must supply EntityModelSet.BurnLevel; without it every burning "
            + "player in every demo draws unburnt and nothing else in the suite notices");
    }

    /// <remarks>
    /// **The control, and it is the one that matters here**: the proxy rests at zero, so a delegate
    /// that answered a constant — or one never assigned at all, which also yields zero — would pass
    /// a test that only looked at the burning case. A player who is not alight must arrive at zero
    /// while the burning one arrives at one, and only the PAIR distinguishes the two.
    /// </remarks>
    [Test]
    public void Build_AWornItemOnAPlayerNotAlight_ReachesTheDrawnInstanceUnburnt()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp hat = Hat();

        scene.Build([Player(burningFor: null)], [], default);

        List<ModelInstance> instances = [];

        models.Add([hat], _ => Frames());
        models.Instances([hat], instances);

        instances.ShouldHaveSingleItem().Burn.ShouldBe(0f, 1e-4f);
    }

    /// <remarks>
    /// **A burn past the flame's ten-second life is OUT, not stuck on.** The clock keeps running
    /// while the condition holds — `OnAddBurning` only starts it when the effect is not already
    /// going, so a re-ignite does not restart it — and `pl_upward_f12` really does carry clocks up
    /// to 12.27 seconds. The proxy's clamp is what turns those into an extinguished player rather
    /// than a permanently blazing one.
    /// </remarks>
    [Test]
    public void Build_APlayerBurningPastTheFlamesLife_ReachesTheInstanceExtinguished()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp hat = Hat();

        scene.Build([Player(burningFor: 12.27f)], [], default);

        List<ModelInstance> instances = [];

        models.Add([hat], _ => Frames());
        models.Instances([hat], instances);

        instances.ShouldHaveSingleItem().Burn.ShouldBe(
            0f, 1e-4f, "measured on pl_upward_f12: clocks really do run past ten seconds");
    }

    /// <summary>A player, alight for the given time or not at all.</summary>
    private static ScenePlayer Player(float? burningFor) =>
        new(Burning, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 3)
        {
            BurningFor = burningFor,
        };

    /// <summary>A weapon carried by that player.</summary>
    /// <remarks>
    /// **`OwnedBy` rather than `AttachedTo`, and the first attempt used the second.** A prop that
    /// is ATTACHED is bone-merged onto its wearer — `EF_BONEMERGE` takes the owner's bones by name
    /// — so with no wearer prop in the scene it produces no instance at all and the assertion below
    /// failed on an empty list rather than on a wrong value. A carried weapon has an origin of its
    /// own and needs nobody.
    ///
    /// The delegate resolves `OwnedBy ?? AttachedTo`, so this exercises the first arm; the second
    /// is the same lookup and is exercised by the paint path that shares it.
    /// </remarks>
    private static SceneProp Hat() =>
        new(
            1,
            "models/weapons/w_models/w_scattergun.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Y = 0f, Z = 0f, Scale = 1f },
            OwnedBy: Burning);

    private static PropModels.ModelFrames Frames() =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [0, 1],
            [false, false]);
}
