using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Entity-driven behaviour the client performs and this project does not.
/// </summary>
/// <remarks>
/// **Seventh batch, and the last of the systematic sweep.** These are client-side systems rather
/// than file formats: things the engine constructs at runtime from networked state, where the demo
/// carries the inputs and the client carries the behaviour.
///
/// **That makes them the hardest class to notice as missing**, because there is no lump to open and
/// no parameter to count. A ragdoll that never appears leaves a player standing; a particle system
/// that never runs leaves the air clear. Nothing in the file says either should have happened.
///
/// **One of them is not visual at all.** Sound carries a position and this project decodes voice and
/// sound messages without placing them, which for a review tool is a real loss: where a sound came
/// from is often the whole question.
/// </remarks>
public sealed class UnimplementedEntityConformanceTests
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
    public void ARagdollReplacesTheModelWhenAPlayerDies()
    {
        // ragdoll.cpp and c_ragdoll_manager.cpp. On death the client swaps the animated model for a
        // physically simulated one, carrying the last pose and the damage force as its initial
        // velocity — which is why a rocketed player flies and a shot one crumples.
        //
        // This project draws the animated model throughout, so a dead player either freezes on their
        // last frame or keeps playing an animation. Both read as an animation bug rather than as a
        // missing system, which is the reason to write it down.
        //
        // **Physics is the project's one stated exclusion**, and this is the honest edge of it: a
        // ragdoll is simulation, so reproducing it exactly is out of scope by that rule. What is NOT
        // out of scope is knowing the player died and stopping the animation, which is the part that
        // currently looks wrong.
        SourceFiles("src/game/client", "ragdoll.cpp").ShouldNotBeEmpty();

        Assert.Ignore(
            "ragdolls are not implemented. Physics simulation is out of scope by the project's own " +
            "rule, but a dead player currently keeps animating, which is a separate and fixable " +
            "half of the same symptom.");
    }

    [Test]
    public void AViewmodelIsDrawnInItsOwnSpaceWithFlippedCulling()
    {
        // c_baseviewmodel.cpp:375-379 — the same passage this project already cites for
        // MATERIAL_CULLMODE_CCW. A viewmodel is drawn with a separate field of view, in view space
        // rather than world space, and with the cull mode temporarily flipped because the model is
        // mirrored for the left-handed view.
        //
        // For a POV demo the viewmodel is a large part of what the player actually saw. This project
        // draws none, which is defensible for an overhead review view and a real absence for a POV
        // one.
        //
        // The cull flip is pinned because it is the detail that makes a naive implementation draw
        // the weapon inside out.
        SourceFiles("src/game/client", "c_baseviewmodel.cpp").ShouldNotBeEmpty();

        Assert.Ignore(
            "viewmodels are not drawn. For a POV demo that is a large part of the frame, and the " +
            "cull mode is flipped for them (c_baseviewmodel.cpp:375) — a naive implementation " +
            "draws the weapon inside out.");
    }

    [Test]
    public void AParticleSystemIsAnEntityNamingAnEffectByIndex()
    {
        // C_ParticleSystem (c_particle_system.cpp:20) networks m_iEffectIndex — an index into the
        // particle manifest — plus a start flag and control points. So the demo says WHICH effect
        // and WHERE, and the definition lives in the game's particle files.
        //
        // Unimplemented, every explosion, muzzle flash, jarate cloud and übercharge glow is absent.
        // Combined with temp entities being unhandled, a demo of a fight currently shows movement
        // and nothing else.
        //
        // Worth separating from temp entities in the record: those are one-shot engine effects, this
        // is a persistent entity with its own lifetime, and implementing one gains nothing from the
        // other.
        SourceFiles("src/game/client", "c_particle_system.cpp").ShouldNotBeEmpty();

        Assert.Ignore(
            "particle systems are not implemented. C_ParticleSystem networks m_iEffectIndex and " +
            "control points, so the demo says which effect and where — the definitions are in the " +
            "game's particle files.");
    }

    [Test]
    public void AProjectedTextureIsADynamicLightWithAFrustum()
    {
        // c_env_projectedtexture.cpp. A projected texture is a spotlight with a real frustum and,
        // optionally, a shadow map — TF2 uses them for flashlights and for the lamps that light
        // spawn rooms.
        //
        // This project's lighting is entirely baked, so these do not exist at all. That is a
        // deliberate consequence rather than an oversight — a demo viewer replaying a recorded map
        // can lean on the lightmap — but anything the map lit dynamically is unlit here, and the
        // difference shows most where a mapper relied on it.
        SourceFiles("src/game/client", "c_env_projectedtexture.cpp").ShouldNotBeEmpty();

        Assert.Ignore(
            "dynamic lights and projected textures are not implemented. All lighting here is baked, " +
            "so anything the map lit dynamically is unlit.");
    }

    [Test]
    public void ASoundCarriesThePositionItWasPlayedFrom()
    {
        // **The non-visual gap, and for a review tool a real one.** SoundInfo_t (soundinfo.h:72)
        // carries an origin and a direction alongside the sound index, and the engine spatialises
        // from them. Its WriteDelta and ReadDelta at lines 157 and 262 are the wire format this
        // project already walks past in svc_Sounds.
        //
        // Where a footstep or a rocket came from is often the entire question being reviewed, and
        // this project decodes sounds without placing them. That is not a rendering gap — a text
        // trace could answer it — which is why it belongs on this list rather than in the renderer's.
        NetMessageReader.SoundsLengthBits.ShouldBe(16);

        Assert.Ignore(
            "sound positions are not used. SoundInfo_t carries an origin and direction the engine " +
            "spatialises from, and for a review tool 'where did that come from' is often the whole " +
            "question. A text trace could answer it without any rendering.");
    }

    [Test]
    public void AGameEventIsDecodedAndNeverShown()
    {
        // Game events ARE decoded here — GameEventCodec reads the list and the events, and
        // GameEventConformanceTests pins the widths. What nothing does is present them.
        //
        // A kill, a capture, a round win and a player joining all arrive as events, and a review
        // tool that decodes them and shows nothing has done the hard half and skipped the visible
        // one. This is the cheapest entry on the whole list: the data is parsed, named and typed,
        // and the missing piece is a display.
        //
        // Stated so the ratio is visible: decoding without presenting is not the same as not having
        // the feature, and the score should distinguish them.
        GameEventCodec.EventIdBits.ShouldBe(9);

        Assert.Ignore(
            "game events are decoded and never presented. Kills, captures and round results are " +
            "all parsed already — the missing half is a display, which makes this the cheapest " +
            "user-visible gap on the list.");
    }

    [Test]
    public void MaterialOverridesRecolourAnEntityWithoutChangingItsModel()
    {
        // An übercharged player, a disguised Spy and a burning enemy all draw with a material
        // OVERRIDE — the same model with a different material forced over it — rather than with a
        // different model or skin.
        //
        // **This comment previously said TF2's game code is not public and the override list could
        // not be cited. That was wrong, and the correction is worth more than the test.**
        // source-sdk-2013 ships TF2's own game code — 1,318 files under game/{shared,client,server}/tf
        // — so the overrides are named outright rather than inferred:
        //
        //   c_tf_player.cpp:395,398 — "models/effects/invulnfx_blue.vmt" / "invulnfx_red.vmt"
        //
        // Two materials, chosen by team, applied over the ordinary one. Not a shader flag, not a
        // skin: a whole material substituted for the duration of a condition.
        //
        // Still distinct from skins, which is the part that was right: skins are per-model families
        // and overrides are per-entity replacements, and conflating them gets team colour right and
        // übercharge wrong.
        string player = SourceSdk.Text("src/game/client/tf/c_tf_player.cpp").ShouldNotBeNull();

        player.ShouldContain("models/effects/invulnfx_blue.vmt");
        player.ShouldContain("models/effects/invulnfx_red.vmt");

        Assert.Ignore(
            "material overrides are not implemented, so übercharge, disguises and burning draw as " +
            "the ordinary material. The uber materials are named in c_tf_player.cpp:395 — TF2's " +
            "game code is in the SDK, which this project had wrongly recorded as closed.");
    }

    /// <summary>Files matching a pattern under the SDK, to confirm a citation exists.</summary>
    private static IReadOnlyCollection<string> SourceFiles(string folder, string pattern) =>
        [.. SourceSdk.Files(folder, pattern)];
}
