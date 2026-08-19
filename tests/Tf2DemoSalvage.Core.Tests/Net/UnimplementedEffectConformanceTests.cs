using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Effects and entity behaviour the demo carries and this project does not act on.
/// </summary>
/// <remarks>
/// **Fifth batch, and it moves from the map to the STREAM.** Everything here arrives in the demo
/// rather than in the BSP, which changes the failure mode: a missing map feature is a static
/// difference you could photograph, while a missing stream feature is something that should have
/// happened at a tick and did not. Nobody notices an explosion that never appeared.
///
/// **Several of these are decoded already and thrown away**, which is the specific thing worth
/// recording. This project reads <c>svc_TempEntities</c> well enough to step over it and
/// <c>svc_BSPDecal</c> well enough to name its fields; what neither has is anything downstream. A
/// message that parses and does nothing is indistinguishable, in a log, from one that never arrived.
/// </remarks>
public sealed class UnimplementedEffectConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Effects_EveryTempEntity_IsAOneShotEffectWithItsOwnClass()
    {
        // **39 effect classes, one file each**, declared as C_TE* in game/client/c_te_*.cpp. Each is
        // a one-shot the server fires and the client plays: explosions, sparks, blood, tracers,
        // shell casings, dust.
        //
        // svc_TempEntities is decoded here to the point of knowing its length — enough to step over
        // it and keep reading the packet — and nothing interprets the payload. So a demo of a fight
        // shows the players and none of the shooting.
        //
        // The count is derived rather than stated so this cannot go stale against the SDK.
        int effects = TempEntityClasses().Count;

        effects.ShouldBeGreaterThan(30, "the C_TE class scan found too few to be right");

        Assert.Ignore(
            $"{effects} temp entity classes exist and none is interpreted. svc_TempEntities is " +
            "decoded far enough to skip and no further, so explosions, tracers, blood and sparks " +
            "are absent from every demo.");
    }

    [Test]
    public void Effects_ABspDecal_IsPlacedOnTheWorldAtAPosition()
    {
        // TE_BSPDecal (c_te.cpp:59) places a decal on world geometry at a position, naming the
        // texture by index into the decal precache string table. This project decodes the message —
        // DecalTextureBits is 9 and the entity and model indices are read — and draws nothing.
        //
        // These are the bullet holes and scorch marks a match accumulates. Absent, a demo's
        // architecture stays factory-clean through a full game, which reads as correct because
        // nobody has a mental image of how scuffed a map should be at five minutes in.
        NetMessageReader.DecalTextureBits.ShouldBe(9);

        Assert.Ignore(
            "svc_BSPDecal is decoded and never drawn, so bullet holes and scorch marks never " +
            "appear. The message's fields are already read.");
    }

    [Test]
    public void Effects_AnAnimationLayer_BlendsOverTheBaseSequence()
    {
        // **A player is not one animation.** C_BaseAnimatingOverlay carries m_AnimOverlay, a vector
        // of C_AnimationLayer, and the client composes them over the base sequence — which is how a
        // Soldier runs and reloads at the same time, or aims up while walking.
        //
        // This project selects a single sequence per entity. That is why B84's fix stopped at "which
        // animation plays"; the layers are a second axis nothing here has, so an upper-body action
        // over a lower-body run cannot be represented at all.
        //
        // MAX_OVERLAYS bounds the count, and the names are declared in
        // c_baseanimatingoverlay.cpp:48.
        SourceSdk.Text("src/game/client/c_baseanimatingoverlay.cpp")
            .ShouldNotBeNull("the animation overlay source is missing from the SDK checkout");

        Assert.Ignore(
            "animation layers are not implemented. A player draws one sequence, so an upper-body " +
            "action over a lower-body movement cannot be shown — the second axis B84 stopped at.");
    }

    [Test]
    public void Effects_APoseParameter_DrivesASequencesBlendGrid()
    {
        // StudioSequences already reads paramindex, paramstart and paramend, and StudioLayout pins
        // all three — so this project knows a sequence is a GRID and which pose parameters drive it.
        //
        // What it does not have is the values. A pose parameter is networked per entity
        // (m_flPoseParameter) and this project reads none, so every grid is sampled at its corner.
        // That is precisely B84's residue: the movement blend is a 9-way grid and we always take
        // one cell.
        Assert.Ignore(
            "pose parameter VALUES are not decoded. The grids are read and always sampled at one " +
            "corner, which is what B84 identified and did not close.");
    }

    [Test]
    public void Effects_Fog_IsControlledByAnEntityWithStartAndEndDistance()
    {
        // c_env_fog_controller declares the fog's colour, start and end distances and density. TF2's
        // maps use it heavily and this project draws no fog at all, so distance reads flat and the
        // 3D skybox — when it is drawn — will not blend into it.
        //
        // Worth specifying now rather than later precisely because it interacts: fog and the skybox
        // are two halves of how a Source map fakes distance, and implementing either alone looks
        // worse than neither.
        SourceSdk.Text("src/game/client/c_env_fog_controller.cpp")
            .ShouldNotBeNull("the fog controller source is missing from the SDK checkout");

        Assert.Ignore(
            "fog is not implemented. It pairs with the 3D skybox — implementing one without the " +
            "other looks worse than neither, because the skybox then has a visible seam.");
    }

    [Test]
    public void Effects_RopesAndSprites_AreEntitiesWithTheirOwnDrawing()
    {
        // c_rope.cpp and c_sprite.cpp: a rope is a simulated catenary between keyframe entities and
        // a sprite is a camera-facing quad, often additive. TF2 maps use ropes for cables and
        // sprites for the glows on lights and control points.
        //
        // Neither is drawn. A cable is simply missing; a glow is missing in a way that makes lights
        // look off rather than absent, which is the harder one to notice.
        List<string> sources =
        [
            .. new[] { "c_rope.cpp", "c_sprite.cpp" }
                .Where(name =>
                    SourceSdk.Text($"src/game/client/{name}") is not null),
        ];

        sources.Count.ShouldBe(2, "both entity sources should be present in the SDK");

        Assert.Ignore(
            "ropes and sprites are not drawn. Cables are absent outright; light glows are absent " +
            "in a way that makes the lighting look wrong rather than incomplete.");
    }

    [Test]
    public void Effects_TwoServerMessages_AreNotDecodedAtAll()
    {
        // The gaps in NetMessageType's numbering, from NetMessageConformanceTests: the engine
        // declares handlers for SendTable and CrosshairAngle and this project handles neither.
        //
        // svc_SendTable (id 9) is how a server sends a data table outside the signon, and hitting
        // one stops the packet — these messages carry no length prefix. svc_CrosshairAngle (20)
        // forces a client's crosshair angle, which for a POV demo is part of what the player saw.
        //
        // Both are stated here as effects rather than as decode gaps because that is how they show
        // up: a stop mid-packet loses everything after it in that packet.
        HashSet<string> declared = EngineMessages();

        declared.ShouldContain("SendTable");
        declared.ShouldContain("CrosshairAngle");

        Assert.Ignore(
            "svc_SendTable (9) and svc_CrosshairAngle (20) are unimplemented. Messages carry no " +
            "length prefix, so meeting either stops the packet and loses the rest of it.");
    }

    /// <summary>Every temp entity class the SDK declares, one file per family.</summary>
    private static HashSet<string> TempEntityClasses() =>
        SourceSdk.Names(
            "src/game/client",
            "c_te_*.cpp",
            new Regex(@"class (C_TE[A-Za-z0-9_]+)\s*:", RegexOptions.None, TimeSpan.FromSeconds(10)));

    /// <summary>Every message the engine declares a handler for.</summary>
    private static HashSet<string> EngineMessages() =>
        SourceSdk.Names(
            "src/public",
            "inetmsghandler.h",
            new Regex(
                @"PROCESS_(?:NET|SVC)_MESSAGE\(\s*([A-Za-z0-9_]+)\s*\)\s*=\s*0",
                RegexOptions.None,
                TimeSpan.FromSeconds(10)));
}
