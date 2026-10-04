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

    // **`Beams_AreNotDrawn` stood here** until entity beams drew (B474, 2026-10-04): EntityBeamRenderTests, and
    // EntityBeamsConformanceTests in Scene.Tests. It named the wrong subject twice. The medigun's link is a particle
    // system, not a beam, and no demo in either corpus carries a C_TEBaseBeam at all (`entity-census`,
    // docs/findings/73-beams-trails-and-ropes-are-strips-the-client-builds.md). The beams TF2 maps do carry are CBeam
    // entities, point_spotlight's shafts, which a NOBASE table had kept out of the timeline.

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

    // **`Fog_IsNotApplied` stood here** until fog was drawn (B139, 2026-10-02): FogRenderTests and
    // FogConformanceTests.

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
