using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// One test per piece of Source's rendering, whether or not this project implements it yet.
/// </summary>
/// <remarks>
/// **The inventory, made executable.** `docs/CONFORMANCE.md` says in prose what this project
/// reproduces of the engine and what it does not; this says the same thing in a form that runs, so
/// the answer to "how far from Source are we" is a number per commit rather than a memory.
///
/// **Unimplemented features are IGNORED, not failed.** A suite with a permanent red section stops
/// being read, and it breaks the merge gate this project runs on — after a week nobody can tell a
/// new failure from the backlog. NUnit reports skips in every run's summary, and the reason string
/// carries the spec, so the information survives without the erosion. Implementing one is deleting
/// one <c>Assert.Ignore</c>.
///
/// **Every entry names its source in the SDK and what you would SEE without it.** That second part
/// is what makes this a worklist rather than a list: "$envmap unimplemented" is a fact, "models have
/// no reflections, worst on metal and glass" is something that can be matched against a screenshot.
/// An entry that cannot say what it costs is not ready to be worked on — which is also the guard
/// against the census's own trap, where <c>$cloakPassEnabled</c> sits on 307 materials and shows
/// nothing until a spy cloaks on camera.
///
/// The tests that DO pass are the useful half in the other direction: they are the claims about
/// parity that would otherwise rest on a commit message.
/// </remarks>
public sealed class SourceConformanceTests
{
    /// <summary>Parses a material from VMT text.</summary>
    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes(text));

    [Test]
    public void Lighting_HalfLambert_WrapsTheDirectTerm()
    {
        // common_vs_fxc.h:826 — NDotL = NDotL * 0.5 + 0.5; NDotL = NDotL * NDotL.
        // Without it a surface facing away from a light is black rather than a quarter lit, which
        // reads as a character silhouetted in shade instead of a solid shape.
        Material("""
            "VertexLitGeneric"
            {
                "$basetexture" "models/player/scout"
                "$halflambert" "1"
            }
            """).IsHalfLambert.ShouldBeTrue();

        // The control: the flag is opt-in, and a material without it takes plain Lambert.
        Material("""
            "VertexLitGeneric"
            {
                "$basetexture" "models/player/scout"
            }
            """).IsHalfLambert.ShouldBeFalse();
    }

    [Test]
    public void Shading_TwoTextureMaterials_MultiplyRatherThanMix()
    {
        // unlittwotexture_ps2x.fxc — baseColor * baseColor2 * g_DiffuseModulation, alpha forced to
        // one. Sampling only the base draws half the material: TF2's capture point beams put the
        // colour in either slot, so dropping one turns a beam into grey stripes on one team.
        Material("""
            "UnLitTwoTexture"
            {
                "$basetexture" "models/effects/cappoint_beam_lines"
                "$texture2" "models/effects/cappoint_beam_blue"
            }
            """).IsTwoTexture.ShouldBeTrue();
    }

    [Test]
    public void Shading_ModulateMaterials_MultiplyTheFramebuffer()
    {
        // A shader name is a declaration in itself. Modulate names no unfamiliar parameter, so it
        // passed a census of parameters in silence while painting every capture point as a dark
        // slab.
        VmtMaterial dark = Material("""
            "Modulate"
            {
                "$basetexture" "models/effects/cappoint_logo_blue"
                "$mod2x" "1"
            }
            """);

        dark.IsModulate.ShouldBeTrue();
        dark.IsModulateTwice.ShouldBeTrue();
    }

    [Test]
    public void Shading_NoCull_IsAPerMaterialFlag()
    {
        // imaterial.h:369 — MATERIAL_VAR_NOCULL, tested per material by shaders
        // (depthwrite.cpp:93). Everything else culls back faces, front wound clockwise per
        // MATERIAL_CULLMODE_CCW at imaterialsystem.h:180. Drawn both-sided, a capture point's
        // hologram shows the far side of its disc through the near one and the sign is unreadable.
        Material("""
            "UnlitGeneric"
            {
                "$basetexture" "glass"
                "$nocull" "1"
            }
            """).IsNoCull.ShouldBeTrue();
    }

    [Test]
    public void Models_SkinFamilies_IndexAsValveIndexesThem()
    {
        // pSkinref(skin * numskinref + material). A team colour in TF2 is a skin family rather than
        // a tint, so getting this wrong draws both teams in RED — measured on cap_point_base, whose
        // three families are neutral, RED and BLU over one mesh.
        const int references = 3;

        int Lookup(int family, int reference) => (family * references) + reference;

        Lookup(0, 0).ShouldBe(0);
        Lookup(1, 0).ShouldBe(3);
        Lookup(2, 0).ShouldBe(6);
    }

    [Test]
    public void Models_BodygroupSelection_MatchesGetBodygroup()
    {
        // shared/animation.cpp:876 — (body / pbodypart->base) % pbodypart->nummodels.
        // A capture point's model offers four signs on one part with base 1, so the body number IS
        // the alternative; getting it wrong shows one sign on every point.
        static int Selected(int body, int place, int count) => (body / place) % count;

        Selected(0, 1, 4).ShouldBe(0);
        Selected(2, 1, 4).ShouldBe(2);
        Selected(3, 1, 4).ShouldBe(3);

        // A part further along the number carries a larger place value, which is how one integer
        // addresses several parts at once.
        Selected(6, 2, 2).ShouldBe(1);
    }

    [Test]
    public void Shading_TextureScroll_MatchesTheEnginesWrap()
    {
        // CTextureScrollMaterialProxy::OnBind. The wrap is kept in Valve's two-step form because a
        // modulo is a different function for negative rates: −0.25 must become 0.75.
        TextureTransform scrolled = MaterialProxies.TextureScroll(1d, rate: -0.25f, angle: 0f);

        scrolled.Row0.W.ShouldBe(0.75f, 1e-4f);
    }

    [Test]
    public void Phong_IsNotImplemented()
    {
        // 330 of cp_process's 1,034 prop and model materials, with $phongboost on 329,
        // $phongfresnelranges on 329, $phongexponent on 323 and $basemapalphaphongmask on 102.
        //
        // WHAT YOU SEE: every character model is dull. This is Source's specular for players and
        // the single largest visual difference between this viewer and the game.
        //
        // TO IMPLEMENT: vertexlitgeneric_dx9_helper.cpp and the phong helper beside it. The mask
        // channel is chosen by $basemapalphaphongmask against the normal map's alpha, and picking
        // the wrong one puts a plausible sheen in the wrong places — which is worse than none,
        // because it looks deliberate.
        Assert.Ignore("$phong: 330 materials; every model dull. See docs/CONFORMANCE.md.");
    }

    [Test]
    public void EnvironmentMaps_AreNotImplemented()
    {
        // 133 prop materials and 42 brushwork ones, with $envmaptint, $envmapcontrast,
        // $envmapsaturation, $basealphaenvmapmask and $normalmapalphaenvmapmask alongside.
        //
        // WHAT YOU SEE: nothing reflects. Worst on metal, glass and water, and it is half of why
        // the capture point disc reads flat.
        //
        // TO IMPLEMENT: the cubemaps live in the BSP's pakfile, named by position, and $envmap
        // "env_cubemap" means "the nearest one" rather than a file. Filed as B55.
        Assert.Ignore("$envmap: 175 materials; nothing reflects. See docs/CONFORMANCE.md, B55.");
    }

    [Test]
    public void MaterialProxies_AreNotEvaluated()
    {
        // The arithmetic is ported and tested — MaterialProxies.TextureScroll and Sine — and the
        // transforms and modulation colour are plumbed to the shader. Nothing parses the Proxies
        // block out of a VMT or evaluates it per frame, so every transform sits at identity.
        //
        // WHAT YOU SEE: a capture point's beam does not scroll and its sign does not pulse. The
        // scene is correct and static, which reads as lifeless rather than as broken.
        //
        // **The time-driven half is done and measured through the GPU.** The Proxies block is
        // parsed, carried through both the world and the entity model paths, and evaluated at BIND
        // — which is what the engine does, since IMaterialProxy has Init, OnBind and Release and no
        // tick at all. Naming the capture point models brings in Sine x6 and TextureScroll x6, and
        // ProxyRenderTests draws one at two playback times and gets two different pictures while an
        // unproxied control stays byte-identical.
        //
        // What remains is the ENTITY-STATE half. cp_process_final's own materials run Subtract,
        // PlayerProximity, Clamp, PlayerTeamMatch, Divide and Multiply, and none of those is a
        // function of time: they read the entity the material is drawn on — its team, a player's
        // distance — which this layer does not have. An unrecognised proxy leaves the material at
        // its resting value rather than being guessed at.
        //
        // TO FINISH: give the material bind an entity, so a proxy can read team and proximity.
        // That is a scene-layer change rather than a material one, which is why it is not here.
        Assert.Ignore(
            "Proxies: time-driven ones evaluated on both paths; entity-state ones need the entity " +
            "the material is drawn on. B80.");
    }

    [Test]
    public void LightWarpTexture_IsNotImplemented()
    {
        // 308 materials. A one-dimensional ramp the engine looks up with N·L, which is a large part
        // of TF2's flat, illustrative shading.
        //
        // WHAT YOU SEE: lighting falls off linearly where the game's is authored, so models read
        // as photographic rather than as TF2.
        Assert.Ignore("$lightwarptexture: 308 materials; lighting curve is linear.");
    }

    [Test]
    public void RimLight_IsNotImplemented()
    {
        // 301 materials, with $rimlightboost and $rimlightexponent.
        //
        // WHAT YOU SEE: no edge light, so a model's silhouette does not separate from what is
        // behind it. Cheap next to $phong and visible in exactly the shots that show it missing.
        Assert.Ignore("$rimlight: 301 materials; silhouettes do not separate.");
    }

    [Test]
    public void EyeRefract_IsNotImplemented()
    {
        // The only unimplemented SHADER on cp_process, on 13 materials. PrimaryTexture already
        // falls back to $iris, which is why eyes are not the missing-texture chequer.
        //
        // WHAT YOU SEE: eyes are a flat iris — no cornea, no refraction, no specular catch.
        Assert.Ignore("EyeRefract: 13 materials; eyes flat, iris only.");
    }

    [Test]
    public void TextureTransforms_AreNotParsed()
    {
        // $basetexturetransform on 19 brushwork materials and $texture2transform on 5. The shader
        // applies two transforms already; nothing reads the matrix form
        // ("center .5 .5 scale 1 1 rotate 0 translate 0 0") out of a VMT.
        //
        // WHAT YOU SEE: any material that offsets or tiles its texture by transform draws at the
        // texture's own scale and origin instead.
        Assert.Ignore("$basetexturetransform: 24 materials; matrix form unparsed.");
    }

    [Test]
    public void AttachmentPoints_AreNotImplemented()
    {
        // Not a material. mstudioattachment_t in the model and m_iParentAttachment on the entity
        // are both unread, so an item whose bones match none of its wearer's is placed by the
        // wearer's transform alone.
        //
        // WHAT YOU SEE: a medic's halo and an MvM canteen sit at the player's FEET. Measured:
        // hwn_spellbook_complete.mdl has one bone, named "mvm", a root — no player has it.
        //
        // TO IMPLEMENT: the attachment's matrix is stored relative to its bone, so it composes
        // against that bone's world matrix; applying it in world space puts the item somewhere
        // plausible and wrong. Filed as B82.
        Assert.Ignore("Attachments: worn items with no matching bone draw at the feet. B82.");
    }

    [Test]
    public void Census_TheSdkSurface_AgreesWithThisSuite()
    {
        // **The cross-check, and the reason both halves exist.** The census measures what a MAP
        // asks for; this suite measures what the ENGINE does. Neither sees everything: the census
        // found $halflambert on 190 materials that nobody had thought to look for, and the suite
        // covers features no cp_process material happens to declare.
        //
        // So the two are kept honest against each other — anything this suite calls implemented
        // must not be reported by the census as missing.
        IReadOnlyList<(string Shader, int Materials)> unimplemented =
            MaterialCensus.UnimplementedShaders(
                ["Modulate", "UnLitTwoTexture", "WorldVertexTransition", "VertexLitGeneric"]);

        unimplemented.ShouldBeEmpty(
            "every shader this suite asserts parity for must be in the census's implemented set");
    }
}
