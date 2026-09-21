using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`UTIL_ImpactTrace` through `Impact` for a bullet into the world (B415).</summary>
/// <remarks>
/// Texinfo 0 is metal, 1 the sky, 2 nodraw, 3 a surface marked `-`, 4 concrete, 5 a texdata past the table. The
/// groups hold one file each so the pick is decided by the group alone.
/// </remarks>
public sealed class ImpactDecalsConformanceTests
{
    private const string Script = """
        "TranslationData" { "-" "" "C" "Impact.Concrete" "M" "Impact.Metal" }
        "Impact.Concrete" { "decals/concrete/shot1" "1" }
        "Impact.Metal" { "decals/metal/shot1" "1" }
        """;

    private static readonly BspTexinfo[] Texinfo =
    [
        new(SurfaceProperties.None, 0),
        new(SurfaceProperties.Sky, 0),
        new(SurfaceProperties.NoDraw, 0),
        new(SurfaceProperties.None, 2),
        new(SurfaceProperties.None, 1),
        new(SurfaceProperties.None, 9),
    ];

    [TestCase(0, "decals/metal/shot1", TestName = "For_AMetalSurface_IsTheMetalGroup")]
    [TestCase(4, "decals/concrete/shot1", TestName = "For_AConcreteSurface_IsImpactConcrete")]
    [TestCase(5, "decals/concrete/shot1", TestName = "For_ATexdataPastTheTable_IsUntranslated")]
    public void For_ASurface_PicksItsGroupsDecal(int texinfo, string expected)
    {
        Decals().For(Impact(texinfo), static (_, _) => 0f).ShouldNotBeNull().Name.ShouldBe(expected);
    }

    [TestCase(1, TestName = "For_TheSky_IsNoDecal")]
    [TestCase(2, TestName = "For_ANodrawSurface_IsNoDecal")]
    [TestCase(3, TestName = "For_ASurfaceMarkedDash_IsNoDecal")]
    [TestCase(-1, TestName = "For_Terrain_IsNoDecal")]
    public void For_ASurfaceThatTakesNone_IsNull(int texinfo)
    {
        Decals().For(Impact(texinfo), static (_, _) => 0f).ShouldBeNull();
    }

    [Test]
    public void Drawn_EveryGroupFile_IsItsDrawnMaterial()
    {
        Decals().Drawn().ShouldBe(["decals/concrete/shot1", "decals/metal/shot1"], ignoreOrder: true);
    }

    private static ImpactDecals Decals() =>
        new(
            DecalEmitters.Parse(Encoding.UTF8.GetBytes(Script)),
            new DecalMaterials(
                path => path.StartsWith("materials/decals/", System.StringComparison.Ordinal)
                    ? Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"decals/any\" }")
                    : null,
                _ => (64, 64)),
            Texinfo,
            ['M', 'C', '-']);

    private static ShotImpact Impact(int texinfo) =>
        new(0, 0, 1, 5, 2, (0f, 0f, 0f), (1f, 0f, 0f), (2f, 0f, 0f), texinfo);
}
