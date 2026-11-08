using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The drawn props as the client shadow manager reads them: <c>C_BaseEntity::ShadowCastType</c>
/// (`c_baseentity.cpp:1502-1509`) and <c>C_TFPlayer</c>'s overrides (`c_tf_player.cpp:9255-9303`) (B702, finding 107).
/// </summary>
/// <remarks>Every prop's render bounds are (−10, −10, 0)–(10, 10, 72) here, so a change is the gather's.</remarks>
public sealed class ShadowEntitiesConformanceTests
{
    private static readonly (Vector3 Min, Vector3 Max) Bounds = (new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));

    private static SceneProp Prop(int index, SceneModelKind kind = SceneModelKind.Studio, int? attachedTo = null, int? firstTick = null) =>
        new(index, "models/x.mdl", kind, new ScenePose { X = 1f, Y = 2f, Z = 3f, Yaw = 90f, Sequence = 4, Cycle = 0.25f },
            AttachedTo: attachedTo, FirstTick: firstTick);

    private static List<ShadowEntity> Gather(IReadOnlyList<SceneProp> drawn, params int[] players) =>
        Gather(drawn, _ => false, players);

    private static List<ShadowEntity> Gather(IReadOnlyList<SceneProp> drawn, Func<SceneProp, bool> animates, params int[] players) =>
        Gather(drawn, animates, _ => null, players);

    private static List<ShadowEntity> Gather(
        IReadOnlyList<SceneProp> drawn, Func<SceneProp, bool> animates, Func<SceneProp, ShadowPhysics?> physics, params int[] players)
    {
        List<ShadowEntity> into = [];
        new ShadowEntities().Gather(drawn, players.Contains, _ => Bounds, animates, prop => (byte)prop.EntityIndex, physics, into);
        return into;
    }

    /// <remarks>
    /// **A simulated corpse** (`C_BaseAnimating`, `IsRagdoll()`): `ShadowCastType` skips the pose-parameter test for a ragdoll,
    /// so even a model that animates is plain render-to-texture (`c_baseanimating.cpp:820-851`); `GetRenderOrigin` is the
    /// first physics object's position and `GetRenderAngles` is `vec3_angle` (`:4581-4604`, `ragdoll.cpp:177-181`); the
    /// bounds are `CRagdoll`'s cube of ±`m_radius` (`ragdoll.cpp:81-90`, `:205-208`), scaled by the model scale
    /// (`c_baseanimating.cpp:4570-4574`). The pose's own position, the death spot, is not used.
    /// </remarks>
    [Test]
    public void Gather_ASimulatedCorpseWhoseModelAnimates_IsRenderToTextureAtItsRootWithItsRadiusCube()
    {
        SceneProp corpse = Prop(7, firstTick: 100);

        ShadowEntity entity = Gather([corpse], _ => true, _ => new ShadowPhysics(new Vector3(50f, 60f, 70f), (10f, 20f, 30f), 53f)).Single();

        entity.ShouldBe(new ShadowEntity(
            7, null, ShadowCast.RenderToTexture, new Vector3(50f, 60f, 70f), (0f, 0f, 0f), new Vector3(-53f), new Vector3(53f),
            "models/x.mdl", (4, 0.25f), FalloffBias: 7, Ragdoll: true));
    }

    /// <remarks>`theMaxs *= flScale; theMins *= flScale;` applies to a ragdoll's cube as to any render bounds.</remarks>
    [Test]
    public void Gather_ACorpseAtModelScaleTwo_DoublesItsRadiusCube()
    {
        SceneProp corpse = Prop(7, firstTick: 100) with { Pose = Prop(7).Pose with { Scale = 2f } };

        ShadowEntity entity = Gather([corpse], _ => false, _ => new ShadowPhysics(Vector3.Zero, default, 10f)).Single();

        (entity.Mins, entity.Maxs).ShouldBe((new Vector3(-20f), new Vector3(20f)));
    }

    /// <remarks>
    /// **A gib** (`C_PhysPropClientside` → `C_BreakableProp` → `C_BaseAnimating`, no `ShadowCastType` override; it has no
    /// `CRagdoll`, so `IsRagdoll()` is false): cast by its model's own rule, from where its physics object put it, origin and
    /// angles, bounded by its render bounds.
    /// </remarks>
    [Test]
    public void Gather_AGib_CastsByItsModelsRuleFromItsPhysicsPlacementWithItsRenderBounds()
    {
        SceneProp gib = Prop(9, firstTick: 100);

        ShadowEntity entity = Gather([gib], _ => false, _ => new ShadowPhysics(new Vector3(5f, 6f, 7f), (10f, 20f, 30f), null)).Single();

        (entity.Cast, entity.RenderOrigin, entity.RenderAngles, entity.Mins, entity.Maxs, entity.Ragdoll)
            .ShouldBe((ShadowCast.RenderToTexture, new Vector3(5f, 6f, 7f), (10f, 20f, 30f), Bounds.Min, Bounds.Max, false));
    }

    /// <remarks><c>EF_NOSHADOW</c> still casts none for a simulated corpse (`c_baseanimating.cpp:824-825`).</remarks>
    [Test]
    public void Gather_ASimulatedCorpseWithNoShadow_CastsNothing()
    {
        SceneProp corpse = Prop(7, firstTick: 100) with { Pose = Prop(7).Pose with { NoShadow = true } };

        Gather([corpse], _ => false, _ => new ShadowPhysics(Vector3.Zero, default, 10f)).Single().Cast.ShouldBe(ShadowCast.None);
    }

    /// <remarks>
    /// **`CRagdoll::BuildRagdollBounds`** (`ragdoll.cpp:81-90`): `m_radius = |(maxs − mins) · 0.5|` of
    /// `modelinfo-&gt;GetModelBounds`, which is the model's `model_t` box — the header's `hull_min`/`hull_max`
    /// (`engine.dll` `FUN_1801060b0`, `studiohdr_t + 0x68`/`+ 0x74`) with the first collision solid's `CollideGetAABB` at the
    /// origin added once the `.phy` loads (`FUN_1800fca70`, `MDLCACHE_VCOLLIDE`). Hull (−24, −24, 0)–(24, 24, 82). A solid
    /// past its three lower faces, (−30, −28, −5)–(10, 5, 8), makes (−30, −28, −5)–(24, 24, 82): half (27, 26, 43.5),
    /// √3297.25 = 57.421686. One past its three upper faces, (−10, −5, 1)–(26, 30, 90), makes (−24, −24, 0)–(26, 30, 90):
    /// half (25, 27, 45), √3379 = 58.129167. So every corner of both boxes decides one of the two. The hull alone: half
    /// (24, 24, 41), √2833 = 53.225934.
    /// </remarks>
    [Test]
    public void RagdollRadius_AHullAndAFirstSolid_IsTheirUnionsHalfDiagonalAndTheHullAloneWithout()
    {
        StudioBox hull = new(-24f, -24f, 0f, 24f, 24f, 82f);

        ShadowEntities.RagdollRadius(hull, ((-30f, -28f, -5f), (10f, 5f, 8f))).ShouldBe(57.421686f, 1e-4f);
        ShadowEntities.RagdollRadius(hull, ((-10f, -5f, 1f), (26f, 30f, 90f))).ShouldBe(58.129167f, 1e-4f);
        ShadowEntities.RagdollRadius(hull, null).ShouldBe(53.225934f, 1e-4f);
    }

    /// <remarks>Each entity carries its own falloff bias, as `ComputeFxBlend` left it on its shadow.</remarks>
    [Test]
    public void Gather_EachEntity_CarriesItsFalloffBias() =>
        Gather([Prop(7), Prop(9)]).Select(entity => entity.FalloffBias).ShouldBe([(byte)7, (byte)9]);

    /// <remarks>
    /// `C_BaseAnimating::ShadowCastType` (`c_baseanimating.cpp:820-851`): a prop whose model animates — pose parameters,
    /// bone controllers or IK chains — is dynamic; one whose model does not is plain render-to-texture.
    /// </remarks>
    [Test]
    public void Gather_APropWhoseModelAnimates_IsDynamicAndOneThatDoesNotIsRenderToTexture()
    {
        SceneProp sentry = Prop(5) with { ModelPath = "models/buildables/sentry1.mdl" };

        Gather([sentry, Prop(6)], prop => prop.ModelPath == sentry.ModelPath).Select(entity => entity.Cast)
            .ShouldBe([ShadowCast.Dynamic, ShadowCast.RenderToTexture]);
    }

    /// <remarks>A player is dynamic and its bounds grow 36 in x and y only; anything else studio is render-to-texture.</remarks>
    [Test]
    public void Gather_APlayerAndAProp_AreDynamicFattenedAndRenderToTexture()
    {
        List<ShadowEntity> entities = Gather([Prop(1), Prop(40)], 1);

        entities[0].ShouldBe(new ShadowEntity(
            1, null, ShadowCast.Dynamic, new Vector3(1f, 2f, 3f), (0f, 90f, 0f), new Vector3(-46f, -46f, 0f), new Vector3(46f, 46f, 72f),
            "models/x.mdl", (4, 0.25f), FalloffBias: 1));
        entities[1].Cast.ShouldBe(ShadowCast.RenderToTexture);
        (entities[1].Mins, entities[1].Maxs).ShouldBe(Bounds);
    }

    /// <remarks>
    /// A brush or sprite model casts none (<c>mod_studio</c> only), nor a corpse with no simulation to place it — a stuck
    /// arrow, or a corpse whose model has no ragdoll — which has no origin of its own here.
    /// </remarks>
    [Test]
    public void Gather_ABrushASpriteAndAnUnsimulatedCorpse_CastNothing() =>
        Gather([Prop(5, SceneModelKind.Brush), Prop(6, SceneModelKind.Sprite), Prop(7, firstTick: 100)])
            .ShouldAllBe(entity => entity.Cast == ShadowCast.None);

    /// <remarks>
    /// <c>EF_NOSHADOW</c> casts none for a prop and a player alike (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`);
    /// <c>EF_NORECEIVESHADOW</c> alone does not stop a caster.
    /// </remarks>
    [Test]
    public void Gather_APropAndAPlayerWithNoShadow_CastNothingAndNoReceiveShadowStillCasts()
    {
        SceneProp hidden = Prop(1) with { Pose = Prop(1).Pose with { NoShadow = true } };
        SceneProp player = Prop(2) with { Pose = Prop(2).Pose with { NoShadow = true } };
        SceneProp receivesNone = Prop(3) with { Pose = Prop(3).Pose with { NoReceiveShadow = true } };

        Gather([hidden, player, receivesNone], 2).Select(entity => entity.Cast)
            .ShouldBe([ShadowCast.None, ShadowCast.None, ShadowCast.RenderToTexture]);
    }

    /// <remarks>A hat keeps its wearer as its shadow parent; one whose wearer is not drawn has no origin, so it is left out.</remarks>
    [Test]
    public void Gather_AHatOnADrawnAndOnAnUndrawnWearer_KeepsTheFirstAndDropsTheSecond()
    {
        List<ShadowEntity> entities = Gather([Prop(1), Prop(40, attachedTo: 1), Prop(41, attachedTo: 2)], 1);

        entities.Select(entity => entity.EntityIndex).ShouldBe([1, 40]);
        entities[1].ShadowParent.ShouldBe(1);
    }
}
