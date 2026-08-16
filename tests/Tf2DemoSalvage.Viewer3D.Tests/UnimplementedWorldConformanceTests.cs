using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whole world features this project does not reproduce, specified before they are built.
/// </summary>
/// <remarks>
/// **Third batch, and the largest, because it stops asking about material parameters and starts
/// asking what the MAP contains that nothing here reads.** Four of these come from one place: the
/// static prop lump carries nine fields and this project reads three. Every one of the other six is
/// a behaviour the game performs and this viewer does not, and none of them produces an error.
///
/// The question that generated them is worth repeating on any format: **which fields has a
/// conformance test already derived that no reader consumes?** A test pinning a structure's layout
/// is simultaneously an inventory of what is being skipped over, and reading it that way turned up
/// four gaps in one structure.
///
/// **Nothing here is filtered by whether the project will want it.** The owner's instruction is to
/// specify everything now and decide relevance later, which is the right order — a gap that is
/// written down can be dismissed on purpose, and a gap that is not written down gets rediscovered
/// as a bug.
/// </remarks>
public sealed class UnimplementedWorldConformanceTests
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
    public void AStaticPropFadesOutBetweenTwoDistances()
    {
        // **The most visible of the unread prop fields.** m_FadeMinDist and m_FadeMaxDist are at
        // offsets 36 and 40 in every declared version, and V5 onward adds m_flForcedFadeScale at 56.
        // The game fades a prop out between those distances and stops drawing it past the far one.
        //
        // This viewer draws every prop at every distance, which is not merely a cost: the map author
        // chose those distances because the prop looks wrong far away, and detail clutter meant to
        // vanish stays visible through the whole level.
        RequireStaticPropField("Fade");

        Assert.Fail("unreachable once implemented; see RequireStaticPropField");
    }

    [Test]
    public void AStaticPropCanBeExcludedByDetailLevel()
    {
        // m_nMinDXLevel and m_nMaxDXLevel, at 60 and 62 from V6 onward. A prop outside the running
        // DirectX level is not drawn AT ALL — this is how maps ship low-end variants alongside
        // high-end ones in the same lump.
        //
        // Ignoring them draws both variants simultaneously, occupying the same space. That is the
        // kind of defect that reads as z-fighting or as a mapper's mistake rather than as ours, and
        // it is worth pinning precisely because the owner is targeting the highest settings: the
        // props to EXCLUDE at that level are the low-end ones.
        RequireStaticPropField("DXLevel");

        Assert.Fail("unreachable once implemented; see RequireStaticPropField");
    }

    [Test]
    public void AStaticPropNamesTheLeavesItOccupies()
    {
        // m_FirstLeaf and m_LeafCount at 26 and 28, indexing the leaf list the compiler built. The
        // engine uses them for visibility: a prop is drawn when one of its leaves is visible.
        //
        // This project draws every prop always, which is why it is correct today and wrong at scale
        // — a viewer that ignores the PVS draws more than the engine, never less, so the picture is
        // right and the cost is unbounded. Recorded as a gap rather than a defect for that reason.
        RequireStaticPropField("Leaf");

        Assert.Fail("unreachable once implemented; see RequireStaticPropField");
    }

    [Test]
    public void AStaticPropCarriesItsOwnLightmapResolution()
    {
        // m_nLightmapResolutionX and Y, at 68 and 70, and only in V10. Props lit per-lightmap rather
        // than per-vertex carry their own texture size here.
        //
        // Unread, so a prop using lightmapped lighting has nowhere to put it. Related to the vertex
        // lighting path this project DOES have: the two are alternatives, and only one is
        // implemented.
        RequireStaticPropField("Lightmap");

        Assert.Fail("unreachable once implemented; see RequireStaticPropField");
    }

    [Test]
    public void TheThreeDimensionalSkyboxIsDrawnAtASharedScale()
    {
        // **A sky_camera entity states a SCALE**, an integer keyfield read at SkyCamera.cpp:38:
        //
        //     DEFINE_KEYFIELD( m_skyboxData.scale, FIELD_INTEGER, "scale" )
        //
        // The 3D skybox is a small model of the distant world, drawn first with the camera's motion
        // divided by that scale so it parallaxes like something far away. Typically 16.
        //
        // This project already recognises skybox props well enough to keep them out of the world —
        // the note in PropModels about a 3D skybox prop being "a valid shape at a valid position,
        // just nowhere near where the player sees it" is exactly this. What is missing is drawing
        // them, which needs the entity's origin and scale rather than only exclusion.
        RequireMapEntitySupport("sky_camera");

        Assert.Fail("unreachable once implemented; see RequireMapEntitySupport");
    }

    [Test]
    public void WaterIsASurfaceWithItsOwnShaderAndAnAboveOrBelowState()
    {
        // Water is not a material variation, it is a separate shader with a refraction pass, a
        // reflection pass and an $abovewater flag deciding which side the camera is on
        // (Water.cpp:52). Surfaces carry SURF_WARP, which this project already decodes.
        //
        // Unimplemented means water draws as an ordinary flat surface with its base texture, which
        // on a TF2 map is a blue-grey plane where a reflective one belongs.
        RequireImplementedShader("Water");

        Assert.Fail("unreachable once implemented; see RequireImplementedShader");
    }

    [Test]
    public void ABrushEntityIsDrawnAtItsEntitysPositionRatherThanTheWorlds()
    {
        // **B71, and the one with a decode half already done.** A door, a lift or a moving platform
        // is brushwork stored as its own model — dmodel_t index N, referenced by an entity as "*N" —
        // and positioned by that entity rather than by the world.
        //
        // BspModels reads every model and this project draws only model 0, so doors are decoded and
        // then skipped. They are absent rather than misplaced, which is the quieter failure: a map
        // with no doors looks like a map whose doors are open.
        //
        // The entity's origin is what places them, so this needs the entity lump — which
        // BspEntities already parses — joined to the models BspModels already reads.
        int models = ModelCount();

        models.ShouldBeGreaterThan(
            1,
            "a map with only the world model would make this test vacuous");

        Assert.Ignore(
            $"B71: the map declares {models} brush models and only the world is drawn, so doors " +
            "and moving platforms are decoded and skipped. Both halves exist — BspModels reads " +
            "the models, BspEntities reads the origins — and nothing joins them.");
    }

    /// <summary>Skips while <see cref="BspStaticProp"/> exposes no field matching a name.</summary>
    /// <remarks>
    /// **The capability check is reflection over the placement record**, so these activate the day
    /// the field is read rather than needing to be remembered. The static prop lump has nine fields
    /// and this project reads three; each test names the one it is waiting for.
    /// </remarks>
    private static void RequireStaticPropField(string fragment)
    {
        bool exposed = typeof(BspStaticProp)
            .GetProperties()
            .Any(property => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        if (!exposed)
        {
            Assert.Ignore(
                $"BspStaticProp exposes nothing matching '{fragment}'. StaticPropLump_t carries " +
                "nine fields and this project reads three — origin, angles and prop type. The " +
                "layout is already derived by StaticPropConformanceTests, so implementing this is " +
                "reading a field that is known to be there.");
        }
    }

    /// <summary>Skips while no shader of that name is implemented.</summary>
    private static void RequireImplementedShader(string shader)
    {
        if (!MaterialCensus.ImplementedShaderNames.Contains(shader, StringComparer.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                $"the {shader} shader is not implemented, so those surfaces draw with their base " +
                "texture and nothing else.");
        }
    }

    /// <summary>Skips while nothing reads a named map entity.</summary>
    private static void RequireMapEntitySupport(string className)
    {
        // No reader exposes entity-driven world features yet; when one does, this check becomes a
        // real capability query rather than an unconditional skip.
        Assert.Ignore(
            $"nothing reads the {className} entity. BspEntities parses the lump, so the data is " +
            "available and unused.");
    }

    /// <summary>How many brush models the test map declares.</summary>
    private static int ModelCount()
    {
        string? game = GameFolder;

        if (game is null)
        {
            throw new IgnoreException("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
        }

        string map = System.IO.Path.Combine(game, "maps", "cp_process_final.bsp");

        if (!System.IO.File.Exists(map))
        {
            throw new IgnoreException("cp_process_final is not installed.");
        }

        return BspModels.Read(System.IO.File.ReadAllBytes(map)).Count;
    }

    /// <summary>Where Team Fortress 2 is, or null when it is not installed.</summary>
    private static string? GameFolder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && System.IO.Directory.Exists(configured))
            {
                return configured;
            }

            foreach (string root in new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(root, "tf2_textures_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }
}
