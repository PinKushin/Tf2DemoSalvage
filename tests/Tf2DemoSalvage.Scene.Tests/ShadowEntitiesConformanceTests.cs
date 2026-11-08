using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

    private static List<ShadowEntity> Gather(IReadOnlyList<SceneProp> drawn, Func<SceneProp, bool> animates, params int[] players)
    {
        List<ShadowEntity> into = [];
        new ShadowEntities().Gather(drawn, players.Contains, _ => Bounds, animates, prop => (byte)prop.EntityIndex, default, into);
        return into;
    }

    /// <remarks>
    /// **The blob for a player** — `cl_blobbyshadows` (`c_tf_player.cpp:9274`), or `r_shadowrendertotexture 0`, which makes
    /// every render-to-texture shadow simple (`GetActualShadowCastType`, `clientshadowmgr.cpp:2217-2236`): its bounds are
    /// the collision box, TF2's hull (−24, −24, 0)–(24, 24, 82), 62 high ducked (`:9281-9289`, `tf_gamerules.cpp:1310-1324`),
    /// not the fattened render bounds, and it is cast straight down (`:9317-9324`). With render-to-texture off a prop is
    /// simple too, on its own bounds and the shared direction.
    /// </remarks>
    [Test]
    public void Gather_ABlobbyPlayerAndRenderToTextureOff_CastSimpleOnTheHullStraightDown()
    {
        SceneProp standing = Prop(1);
        SceneProp ducked = Prop(2) with { Pose = Prop(2).Pose with { Flags = 1 << 1 } };

        List<ShadowEntity> Run(ShadowGatherContext context)
        {
            List<ShadowEntity> into = [];
            new ShadowEntities().Gather([standing, ducked, Prop(40)], index => index < 3, _ => Bounds, _ => false, _ => 0, context, into);
            return into;
        }

        List<ShadowEntity> blobby = Run(new ShadowGatherContext(null, 0d, Blobby: true));

        blobby.Select(entity => entity.Cast).ShouldBe([ShadowCast.Simple, ShadowCast.Simple, ShadowCast.RenderToTexture]);
        (blobby[0].Mins, blobby[0].Maxs).ShouldBe((new Vector3(-24f, -24f, 0f), new Vector3(24f, 24f, 82f)));
        blobby[1].Maxs.ShouldBe(new Vector3(24f, 24f, 62f));
        blobby[0].CastDirection.ShouldBe(-Vector3.UnitZ);
        blobby[2].CastDirection.ShouldBeNull();

        List<ShadowEntity> off = Run(new ShadowGatherContext(null, 0d, RenderToTextureOff: true));

        off.Select(entity => entity.Cast).ShouldAllBe(cast => cast == ShadowCast.Simple);
        off[0].Maxs.ShouldBe(new Vector3(24f, 24f, 82f));
        (off[2].Mins, off[2].Maxs).ShouldBe(Bounds);
    }

    /// <remarks>`cl_blobbyshadows`, off by default as TF2 registers it (`c_tf_player.cpp:9254`), read as `GetBool`.</remarks>
    [TestCase("", false)]
    [TestCase("cl_blobbyshadows 1\n", true)]
    [TestCase("cl_blobbyshadows 0\n", false)]
    public void Parse_BlobbyShadows_IsReadAsTheClientsGetBool(string config, bool expected) =>
        ViewerSettings.Parse(config).BlobbyShadows.ShouldBe(expected);

    /// <remarks>
    /// **A NextBot's shadow by its distance** (`C_NextBotCombatCharacter::UpdateShadowLOD`, `C_NextBot.cpp:66-124`):
    /// render-to-texture dynamic within <c>nb_shadow_dist</c> 400 of the local player, simple beyond it, simple with no local
    /// player — and it tests no <c>EF_NOSHADOW</c>. Decided again only once 0.15 seconds have passed: the Headless Horseman
    /// stepping to 399 units at 0.1 seconds keeps the simple shadow, and at 0.2 takes the dynamic one. The tank
    /// (<c>C_TFBaseBoss</c>) is always dynamic.
    /// </remarks>
    [Test]
    public void Gather_ANextBotAndATank_CastByTheNextBotsDistanceOnItsTimerAndTheTankAlwaysDynamic()
    {
        ShadowEntities gather = new();
        SceneProp horseman = Prop(5) with { ClassName = "CHeadlessHatman" };
        SceneProp tank = Prop(6) with { ClassName = "CTFTankBoss" };
        Vector3 local = new(1f + 401f, 2f, 3f);

        ShadowCast CastAt(SceneProp bot, Vector3? player, double seconds)
        {
            List<ShadowEntity> into = [];
            gather.Gather([bot, tank], _ => false, _ => Bounds, _ => false, _ => 0, new ShadowGatherContext(player, seconds), into);
            into[1].Cast.ShouldBe(ShadowCast.Dynamic, "the tank");
            return into[0].Cast;
        }

        CastAt(horseman, local, 0d).ShouldBe(ShadowCast.Simple, "401 units away");
        CastAt(horseman with { Pose = horseman.Pose with { X = 3f } }, local, 0.1d).ShouldBe(ShadowCast.Simple, "the timer has not run out");
        CastAt(horseman with { Pose = horseman.Pose with { X = 3f, NoShadow = true } }, local, 0.2d).ShouldBe(ShadowCast.Dynamic);
        CastAt(horseman, null, 0.4d).ShouldBe(ShadowCast.Simple, "no local player");
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

    /// <remarks>A brush or sprite model casts none (<c>mod_studio</c> only), nor — a filed gap — a corpse.</remarks>
    [Test]
    public void Gather_ABrushASpriteAndACorpse_CastNothing() =>
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
