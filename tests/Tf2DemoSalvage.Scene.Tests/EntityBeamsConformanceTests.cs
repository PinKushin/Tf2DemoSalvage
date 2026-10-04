using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CViewRenderBeams::DrawBeam( C_Beam* )</c> and what it calls — the entity beam, from the networked state to the
/// corners the renderer draws.
/// </summary>
/// <remarks>
/// **The spotlight is the corpus's only beam**, so it is the fixture: a <c>BEAM_POINTS</c> shaft from (0,0,100) straight
/// down to the origin, <c>FBEAM_SHADEOUT | FBEAM_NOTILE</c>, width 64 and end width 30, a halo of scale 60, render colour
/// white at alpha 64 — what `demostf-cp_process_f12-2026-08-08-2207`'s spotlights send. The camera stands 500 units out
/// along +X at mid height, looking back along −X, so the strip's normal is ±Y and every corner is a round number.
/// </remarks>
public sealed class EntityBeamsConformanceTests
{
    private const string Shaft = "sprites/glow_test02.vmt";

    private const string Halo = "sprites/light_glow03.vmt";

    private static readonly BeamView FromTheSide =
        new(new Vector3(500f, 0f, 50f), -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    /// <remarks>
    /// **The shaft is drawn at its START width at both ends, in two segments** — not <c>FBEAM_HALOBEAM</c>, so
    /// <c>DrawSegs( …, pbeam-&gt;width, pbeam-&gt;width, …, 2, … )</c> (`view_beams.cpp:1837-1839`). The networked end width
    /// of 30 never reaches the screen: the strip is 128 wide at the bottom as at the top.
    /// </remarks>
    [Test]
    public void Build_ASpotlight_DrawsItsShaftAtTheStartWidthInTwoSegments()
    {
        EntityBeams beams = new();

        IReadOnlyList<ParticleBatch> batches = Build(beams, Spotlight());

        List<DetailSpriteVertex> shaft = ShaftOf(batches);

        shaft.Count.ShouldBe(6, "one quad: two segments");
        (shaft[0].X, shaft[0].Y, shaft[0].Z).ShouldBe((0f, -64f, 100f));
        (shaft[5].X, shaft[5].Y, shaft[5].Z).ShouldBe((0f, 64f, 0f), "the end width ignored");
        beams.Drawn.ShouldBe(1);
    }

    /// <remarks>
    /// **The Sprite shader's additive mode ignores the vertex colour by default**, so the shaft's shade — dark at the
    /// bottom under <c>FBEAM_SHADEOUT</c> — never reaches the pixels: every corner carries the material's constant colour
    /// (`sprite_dx9.cpp:332-338`, <c>$ignorevertexcolors</c> defaulting to one at `:39`). The control clears the flag,
    /// and the far end goes black.
    /// </remarks>
    [Test]
    public void Build_ASpotlightOnTheDefaultSpriteMaterial_DrawsTheConstantColourNotTheShade()
    {
        List<DetailSpriteVertex> ignored = ShaftOf(Build(new EntityBeams(), Spotlight()));
        List<DetailSpriteVertex> taken = ShaftOf(Build(new EntityBeams(), Spotlight(), ignoresVertexColours: false));

        ignored.ConvertAll(corner => (corner.Red, corner.Alpha)).Distinct().ShouldBe([(0.5f, 0.75f)]);

        taken[5].Red.ShouldBe(0f, "the vertex colour shaded to black at the far end");
        taken[0].Red.ShouldBeGreaterThan(0f);
    }

    /// <remarks>
    /// **The halo is a glow at the lamp, sized by how near the camera is to the beam's axis.** 500 units off the axis is
    /// past <c>width · 4</c>, so the remap clamps to one and the halo is the networked scale, 60 — a square ±60 about the
    /// lamp, drawn at <c>kRenderGlow</c>: additive, with no depth test.
    /// </remarks>
    [Test]
    public void Build_ASpotlightSeenFromInFront_DrawsItsHaloAtTheLamp()
    {
        IReadOnlyList<ParticleBatch> batches = Build(new EntityBeams(), Spotlight());

        ParticleBatch halo = batches.Single(batch => batch.Material.Depth == SpriteDepth.Off);

        halo.Material.Blend.ShouldBe(SpriteBlend.Additive);
        halo.Corners.Count.ShouldBe(6);
        (halo.Corners[0].X, halo.Corners[0].Y, halo.Corners[0].Z).ShouldBe((0f, 60f, 40f));
    }

    /// <remarks>
    /// **Behind the lamp — looking at the beam from the side it shines away from — there is no halo**:
    /// <c>dotpr &lt; 0</c> sets <c>fade</c> to zero, and the halo draws only <c>if ( fade &amp;&amp; … )</c>. The shaft draws
    /// regardless.
    /// </remarks>
    [Test]
    public void Build_ASpotlightSeenFromBehindItsLamp_DrawsNoHalo()
    {
        BeamView above = new(new Vector3(500f, 0f, 150f), -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

        IReadOnlyList<ParticleBatch> batches = Build(new EntityBeams(), Spotlight(), view: above);

        batches.ShouldNotContain(batch => batch.Material.Depth == SpriteDepth.Off);
        ShaftOf(batches).Count.ShouldBe(6);
    }

    /// <remarks>
    /// **The halo takes the SOURCE colour — the render colour, before the entity's brightness** — scaled only by how
    /// squarely it is faced: <c>VectorScale( srcColor, colorFade * haloFractionVisible, haloColor )</c>. The same
    /// spotlight at alpha 64 and alpha 255 draws the same halo.
    /// </remarks>
    [Test]
    public void Build_TheHalo_IsTheRenderColourWhateverTheBrightness()
    {
        float Dim() => Build(new EntityBeams(), Spotlight(alpha: 64)).Single(b => b.Material.Depth == SpriteDepth.Off).Corners[0].Red;
        float Bright() => Build(new EntityBeams(), Spotlight(alpha: 255)).Single(b => b.Material.Depth == SpriteDepth.Off).Corners[0].Red;

        Dim().ShouldBe(Bright());
        Dim().ShouldBeGreaterThan(0f);
    }

    /// <remarks>
    /// **A halo the camera cannot see is not drawn** — <c>haloFractionVisible &gt; 0.0f</c> gates it. The fraction is the
    /// line-of-sight trace here (B378's open half), so "unseen" is all or nothing.
    /// </remarks>
    [Test]
    public void Build_AHaloBehindSomething_IsNotDrawn()
    {
        IReadOnlyList<ParticleBatch> batches = Build(new EntityBeams(), Spotlight(), visible: _ => false);

        batches.ShouldNotContain(batch => batch.Material.Depth == SpriteDepth.Off);
    }

    /// <remarks>**No material, no beam** — <c>SetupBeam</c> returns before anything when the model does not load.</remarks>
    [Test]
    public void Build_ABeamWhoseMaterialDidNotLoad_IsSkipped()
    {
        EntityBeams beams = new();

        Build(beams, Spotlight(), loaded: false).ShouldBeEmpty();
        beams.Skipped.ShouldBe(1);
    }

    /// <remarks>
    /// **<c>C_Beam::ShouldDraw</c> refuses a beam asking for more than the hardware's DirectX level**
    /// (`beam_shared.cpp:1046`) — TF2's spotlights ask for 90 and draw; one asking for 98 would not.
    /// </remarks>
    [TestCase(90, 1)]
    [TestCase(98, 0)]
    public void Build_ABeamsMinimumDxLevel_DecidesWhetherItDraws(int level, int drawn)
    {
        EntityBeams beams = new();

        Build(beams, Spotlight() with { Pose = Spotlight().Pose with { Beam = Spotlight().Pose.Beam! with { MinDxLevel = level } } });

        beams.Drawn.ShouldBe(drawn);
    }

    /// <remarks>
    /// **Only five of the entity's flags reach the beam** — <c>SINENOISE | SOLID | SHADEIN | SHADEOUT | NOTILE</c>
    /// (`view_beams.cpp:2340`). An entity stating <c>FBEAM_FADEOUT</c> would be drawn black — <c>color · ( 1 − t )</c> with
    /// <c>t</c> one for a beam that lives zero seconds — if the flag got through; it does not.
    /// </remarks>
    [Test]
    public void Build_AnEntityStatingFadeOut_IsNotFaded()
    {
        SceneProp fading = Spotlight(haloPath: null);

        fading = fading with
        {
            Pose = fading.Pose with { Beam = fading.Pose.Beam! with { Flags = EntityBeams.FadeOutFlag } },
        };

        List<DetailSpriteVertex> shaft = ShaftOf(Build(new EntityBeams(), fading, ignoresVertexColours: false));

        shaft.ShouldAllBe(corner => corner.Red > 0f);
    }

    /// <remarks>
    /// **A paused frame repeats its noise**: <c>if ( frametime == 0.0f ) beamRandom.SetSeed( (int)gpGlobals-&gt;curtime )</c>
    /// (`view_beams.cpp:1478-1481`). Two paused builds at the same time draw the same noisy strip; two RUNNING builds
    /// draw two different ones, because the stream carries on.
    /// </remarks>
    [TestCase(0f, true)]
    [TestCase(0.015f, false)]
    public void Build_TwoFramesOfANoisyBeamAtOneTime_RepeatOnlyWhilePaused(float frameTime, bool same)
    {
        SceneProp noisy = Spotlight(haloPath: null);

        noisy = noisy with { Pose = noisy.Pose with { Beam = noisy.Pose.Beam! with { Amplitude = 4f } } };

        EntityBeams beams = new();

        List<float> first = ShaftOf(Build(beams, noisy, frameTime: frameTime)).ConvertAll(corner => corner.Y);
        List<float> second = ShaftOf(Build(beams, noisy, frameTime: frameTime)).ConvertAll(corner => corner.Y);

        first.SequenceEqual(second).ShouldBe(same);
    }

    /// <remarks>
    /// **A laser looked along, away from its source, is not drawn at all** — <c>if ( flDot &gt; 0 ) return;</c>
    /// (`view_beams.cpp:1883`). Facing back toward the source from beside the beam, within the 30 units its proximity fade
    /// spares, it draws: a 400-unit laser 64 wide is cut to nine points by the overlap rule, eight quads.
    /// </remarks>
    [TestCase(1f, 0)]
    [TestCase(-1f, 48)]
    public void Build_ALaser_DrawsOnlyWhenFacedFromItsSourceSide(float forward, int corners)
    {
        SceneBeam laser = Spotlight().Pose.Beam! with
        {
            Type = EntityBeams.BeamLaser,
            EntityCount = 2,
            Ends = [(1, 0), (2, 0), .. Enumerable.Repeat((NoHandle, 0), 8)],
            Flags = 0,
            HaloIndex = 0,
        };

        laser = laser with { HaloPath = null };

        BeamView view = new(
            new Vector3(200f, 10f, 0f), new Vector3(forward, 0f, 0f), new Vector3(0f, -forward, 0f), Vector3.UnitZ);

        IReadOnlyList<ParticleBatch> batches = Build(
            new EntityBeams(),
            Spotlight() with { Pose = Spotlight().Pose with { Beam = laser } },
            view: view,
            ends: (handle, _, _) => handle switch
            {
                1 => Vector3.Zero,
                2 => new Vector3(400f, 0f, 0f),
                _ => null,
            });

        batches.Sum(batch => batch.Corners.Count).ShouldBe(corners);
    }

    /// <remarks>
    /// **An entity-ended beam whose end entity is gone draws from where the update left it**: <c>RecomputeBeamEndpoints</c>
    /// clears <c>FBEAM_ENDENTITY</c> and returns false, <c>UpdateBeam</c> returns early, and <c>DrawBeam</c> — which never
    /// asks — draws the beam anyway, with the end <c>GetAbsEndPos</c> fell back to. That is <c>m_vecEndPos</c>.
    /// </remarks>
    [Test]
    public void Build_BeamEntsWhoseEndEntityIsGone_StillDrawsToItsNetworkedEnd()
    {
        SceneBeam ents = Spotlight(haloPath: null).Pose.Beam! with
        {
            Type = EntityBeams.BeamEnts,
            EntityCount = 2,
            Ends = [(1, 0), (2, 0), .. Enumerable.Repeat((NoHandle, 0), 8)],
            EndPosition = (0f, 0f, 0f),
        };

        IReadOnlyList<ParticleBatch> batches = Build(
            new EntityBeams(),
            Spotlight() with { Pose = Spotlight().Pose with { Beam = ents } },
            ends: (handle, _, _) => handle == 1 ? new Vector3(0f, 0f, 100f) : null);

        List<DetailSpriteVertex> shaft = ShaftOf(batches);

        shaft.ShouldNotBeEmpty();
        shaft.Max(corner => corner.Z).ShouldBe(100f);
        shaft.Min(corner => corner.Z).ShouldBe(0f);
    }

    /// <remarks>
    /// **A spline beam passes through each entity end** — a Catmull-Rom span runs from one control point to the next, so
    /// the last point of the first span is the middle end exactly, and the strip's two edges there straddle it.
    /// </remarks>
    [Test]
    public void Build_ASplineBeam_PassesThroughItsMiddleEnd()
    {
        SceneBeam spline = Spotlight(haloPath: null).Pose.Beam! with
        {
            Type = EntityBeams.BeamSpline,
            EntityCount = 3,
            Ends = [(1, 0), (2, 0), (3, 0), .. Enumerable.Repeat((NoHandle, 0), 7)],
            Width = 2f,
            EndWidth = 2f,
        };

        Vector3 middle = new(0f, 0f, 50f);

        IReadOnlyList<ParticleBatch> batches = Build(
            new EntityBeams(),
            Spotlight() with { Pose = Spotlight().Pose with { Beam = spline } },
            ends: (handle, _, _) => handle switch
            {
                1 => new Vector3(0f, 0f, 100f),
                2 => middle,
                3 => new Vector3(0f, 100f, 0f),
                _ => null,
            });

        List<DetailSpriteVertex> shaft = ShaftOf(batches);

        shaft.ShouldContain(corner => Vector3.Distance(new Vector3(corner.X, corner.Y, corner.Z), middle) <= 2.0001f);
    }

    /// <summary><c>INVALID_NETWORKED_EHANDLE_VALUE</c>.</summary>
    private const int NoHandle = (1 << 21) - 1;

    private static SceneProp Spotlight(byte alpha = 64, string? haloPath = Halo) => new(
        EntityIndex: 530,
        ModelPath: Shaft,
        Kind: SceneModelKind.Sprite,
        Pose: new ScenePose
        {
            Z = 100f,
            RenderMode = RenderModes.TransTexture,
            RenderAlpha = alpha,
            RenderColor = ((byte)255, (byte)255, (byte)255),
            Beam = new SceneBeam(
                Type: EntityBeams.BeamPoints,
                Flags: BeamDraw.ShadeOutFlag | BeamDraw.NoTileFlag,
                EntityCount: 2,
                Ends: [.. Enumerable.Repeat((NoHandle, 0), SceneBeam.MaximumEnds)],
                HaloIndex: haloPath is null ? 0 : 1191,
                HaloScale: 60f,
                Width: 64f,
                EndWidth: 30f,
                FadeLength: 100f,
                Amplitude: 0f,
                StartFrame: 0f,
                Speed: 0f,
                FrameRate: 0f,
                HdrColourScale: 1f,
                Frame: 0f,
                EndPosition: (0f, 0f, 0f),
                MinDxLevel: 90) { HaloPath = haloPath },
        },
        ClassName: "CBeam");

    private static IReadOnlyList<ParticleBatch> Build(
        EntityBeams beams,
        SceneProp prop,
        bool ignoresVertexColours = true,
        BeamView? view = null,
        System.Func<Vector3, bool>? visible = null,
        bool loaded = true,
        float frameTime = 0.015f,
        BeamEntityPosition? ends = null)
    {
        Dictionary<string, EngineSprite> sprites = new(System.StringComparer.OrdinalIgnoreCase)
        {
            [Halo] = Sprite(SpriteBlend.Translucent, (1f, 1f, 1f, 1f), ignoresVertexColours: true),
        };

        if (loaded)
        {
            sprites[Shaft] = Sprite(SpriteBlend.Translucent, (0.5f, 0.5f, 0.5f, 0.75f), ignoresVertexColours);
        }

        return beams.Build(
            [prop],
            view ?? FromTheSide,
            sprites,
            absolute: one => one.Pose,
            entityPosition: ends ?? ((_, _, _) => null),
            visible: visible ?? (_ => true),
            currentTime: 10f,
            frameTime: frameTime);
    }

    private static EngineSprite Sprite(
        SpriteBlend blend, (float, float, float, float) constant, bool ignoresVertexColours) =>
        new(
            new ParticleMaterial(new MapTexture(128, 128, 128, 128, TextureImage.None, IsTransparent: true), [], blend),
            128,
            128,
            SpriteOrientation.Parallel,
            SpriteExtents.Of(128, 128, origin: null),
            constant,
            ignoresVertexColours);

    /// <summary>The shaft's corners: every batch drawn depth-tested, which the halo's glow pass is not.</summary>
    private static List<DetailSpriteVertex> ShaftOf(IReadOnlyList<ParticleBatch> batches) =>
        [.. batches.Where(batch => batch.Material.Depth != SpriteDepth.Off).SelectMany(batch => batch.Corners)];
}
