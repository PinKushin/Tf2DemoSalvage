namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Everything the engine draws that is not the world and not a model.
/// </summary>
/// <remarks>
/// **The part of a match that a still frame does not show.** A demo viewer can have every player in
/// the right place, wearing the right hat, and still look nothing like TF2 because none of the
/// shooting is drawn: no muzzle flash, no rocket trail, no explosion, no blood, no tracer.
///
/// This is also the largest single body of unimplemented work in the project, so the entries below
/// are deliberately split by MECHANISM rather than listed as one item — they arrive by different
/// routes in the demo and can be implemented independently.
/// </remarks>
public sealed class EffectConformanceTests
{
    [Test]
    public void TempEntities_AreDecodedButNotDrawn()
    {
        // svc_TempEntities (inetmsghandler.h:178) carries one-shot effects, and this project
        // DECODES it — the message is read and its payload accounted for rather than skipped.
        //
        // The engine turns each into a C_BaseTempEntity: c_te_armorricochet.cpp, c_te_beamlaser.cpp,
        // c_te_bloodsprite.cpp and about forty siblings beside them in game/client.
        //
        // WHAT YOU SEE: no impact sparks, no blood, no tracers, no ricochets. A firefight is
        // players moving and nothing happening between them.
        Assert.Ignore("svc_TempEntities decoded, effects undrawn; no sparks, blood or tracers.");
    }

    [Test]
    public void ParticleSystems_AreNotDrawn()
    {
        // TF2's modern effects are particle systems named in the demo and defined in the game's
        // .pcf files, distinct from the older temp-entity effects.
        //
        // WHAT YOU SEE: no rocket trails, no explosions, no medigun beam, no jarate cloud, no
        // unusual hat effects. Together with temp entities this is most of what makes a match look
        // like a match.
        Assert.Ignore("Particle systems undrawn; no trails, explosions or medigun beams.");
    }

    [Test]
    public void Sprites_AreNotDrawn()
    {
        // A model path can name a sprite rather than a .mdl — mod_sprite in model_types.h — and
        // this project already classifies them (SceneModelKind.Sprite) rather than handing one to
        // the studio loader.
        //
        // WHAT YOU SEE: glows and lamp flares are absent. The classification means they are
        // skipped cleanly instead of drawing as a missing model, which is why this is a gap rather
        // than a defect.
        Assert.Ignore("Sprites classified but undrawn; no glows or flares.");
    }

    [Test]
    public void Beams_AreNotDrawn()
    {
        // C_TEBaseBeam and its subclasses — beamlaser, beampoints, beamring, beamfollow. The
        // medigun beam and the grappling line are beams rather than particles.
        //
        // WHAT YOU SEE: a medic and his patient are two unconnected players.
        Assert.Ignore("Beams undrawn; medigun and similar links invisible.");
    }

    [Test]
    public void RuntimeDecals_AreNotDrawn()
    {
        // svc_BSPDecal (inetmsghandler.h:173) places a decal during play — bullet holes, blood
        // spatter, sprays. This project decodes the message; the map's AUTHORED overlays are drawn,
        // which is a different lump and a different mechanism.
        //
        // WHAT YOU SEE: walls stay clean through an entire match.
        Assert.Ignore("svc_BSPDecal decoded, runtime decals undrawn; walls stay clean.");
    }

    [Test]
    public void DynamicLights_AreNotDrawn()
    {
        // Muzzle flashes and explosions light the world around them for a moment.
        //
        // WHAT YOU SEE: an explosion does not brighten the room. Compounds with the missing
        // particles: the event is invisible AND its light is missing, so nothing marks it at all.
        Assert.Ignore("Dynamic lights undrawn; explosions do not light the world.");
    }

    [Test]
    public void Shadows_AreNotDrawn()
    {
        // Source renders per-entity shadows — the blob or the render-to-texture kind, decided by
        // the engine's shadow manager.
        //
        // WHAT YOU SEE: players and props have no shadow, so they read as floating rather than
        // standing. This is one of the strongest cues that a scene is not the game, and it is
        // independent of every other item here.
        Assert.Ignore("Entity shadows undrawn; models read as floating.");
    }

    [Test]
    public void Fog_IsNotApplied()
    {
        // A map's env_fog_controller sets start, end and colour, and every shader in the engine
        // takes a fog factor — CalcPixelFogFactor is in the same pixel shaders this project has
        // ported other parts of.
        //
        // WHAT YOU SEE: distance does not fade. On an outdoor map the far side reads as close as
        // the near side, which flattens depth and is why a skybox alone would not fix the horizon.
        Assert.Ignore("Fog unapplied; distance does not fade.");
    }

    [Test]
    public void HdrAndTonemapping_AreNotApplied()
    {
        // LUMP_LIGHTING_HDR 53 exists beside the LDR lighting at 8, and the engine tonemaps the
        // result with an exposure that adapts to what the camera sees.
        //
        // WHAT YOU SEE: brightness is fixed, so moving from a dark room into daylight does not
        // adjust. Whether this MATTERS for a viewer is a genuine question — a demo is watched, not
        // played, and a stable exposure may read better than a shifting one.
        Assert.Ignore("HDR and tonemapping unapplied; exposure fixed. Value is an open question.");
    }

    // **`ViewModels_AreNotDrawn` stood here and was false.** It said "invisible until a
    // first-person camera exists" and predicted its own obsolescence exactly — the camera arrived,
    // arms, weapon and the spy's off-hand watch all draw (D42, docs/findings/30-viewmodel-drawing.md),
    // and the marker went on skipping through the whole session that built them.

    [Test]
    public void CloakAndInvulnerability_AreNotDrawn()
    {
        // $cloakPassEnabled sits on 307 of cp_process's prop and model materials, and
        // vertexlitgeneric_dx9.cpp:288 gates it per frame on CLOAKFACTOR between 0 and 1.
        // Invulnerability is a comparable material pass.
        //
        // WHAT YOU SEE: nothing at all, until a spy cloaks or a medic pops uber on camera — and
        // then a spy is fully visible when he should be a shimmer. This is the entry that proves
        // census counts are not priorities: 307 materials, and it shows for seconds a match.
        Assert.Ignore("Cloak and uber passes undrawn; visible only in the moments they fire.");
    }
}
