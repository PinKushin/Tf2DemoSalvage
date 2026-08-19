using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whether a real map's baked reflections reach <c>MapAssets</c>.
/// </summary>
/// <remarks>
/// **The wiring assertion.** Everything under it is covered — the lump reader, the name derivation,
/// the face decode, the material parameters — and all of that can be right while nothing calls it.
/// That gap has shipped three no-ops in this project with a green suite, so the rule here is that
/// anything producing output is not done until an assertion has read that output on a real demo or
/// map.
/// </remarks>
public sealed class CubemapLoadingTests
{
    private const string MapName = "cp_process_final";

    [Test]
    public void TheMapsPatchedMaterialsArriveCarryingTheirCubemap()
    {
        MapAssets assets = LoadTheMap();

        int carried = assets.Cubemaps.Count(cubemap => cubemap is not null);
        int plain = assets.Cubemaps.Count(cubemap => cubemap is null);

        TestContext.Out.WriteLine($"{carried} of {carried + plain} materials carry a cubemap");

        // 51 materials on this map are patched to a baked cubemap, measured independently by
        // CubemapAssignmentTests reading the pakfile. Stated as a floor rather than as 51 so a
        // Valve update that adds a material does not fail the suite, but high enough that losing
        // the resolution entirely would.
        carried.ShouldBeGreaterThan(40, "51 of this map's materials are patched to a baked cubemap");

        // **The control**, without which "reflections are loaded" and "everything reflects" are the
        // same observation.
        plain.ShouldBeGreaterThan(carried, "most of a map's materials reflect nothing");
    }

    [Test]
    public void EveryCarriedCubemapHasSixDecodedFaces()
    {
        // Six, not seven: the file's last face is a fallback spheremap rather than a direction, and
        // a TextureCube has six. Uploading seven is not possible, so a wrong count here surfaces as
        // a device error much later and a long way from its cause.
        MapAssets assets = LoadTheMap();

        foreach (MapCubemap cubemap in assets.Cubemaps.OfType<MapCubemap>())
        {
            cubemap.Faces.Count.ShouldBe(6);

            foreach (MapTexture face in cubemap.Faces)
            {
                face.Width.ShouldBeGreaterThan(0);
                face.Pixels.Length.ShouldBe(face.Width * face.Height * 4);
            }
        }
    }

    [Test]
    public void TheSixFacesAreNotAllTheSameImage()
    {
        // **The one that catches a face argument that is ignored.** Six identical faces satisfy
        // every count and size assertion above, and a room does not reflect the same image in all
        // six directions.
        //
        // "Not all identical" rather than "all distinct": a cubemap in a symmetrical corridor can
        // legitimately have two matching faces, and demanding six unique ones would fail on correct
        // data.
        MapAssets assets = LoadTheMap();

        MapCubemap cubemap = assets.Cubemaps.OfType<MapCubemap>().First();

        int distinct = cubemap.Faces
            .Select(face => Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(face.Pixels.Span)))
            .Distinct(StringComparer.Ordinal)
            .Count();

        TestContext.Out.WriteLine($"{distinct} distinct images across the six faces");

        distinct.ShouldBeGreaterThan(1);
    }

    [Test]
    public void TheShadingParametersComeThroughAtTheirEngineDefaults()
    {
        // **Defaults are the failure that looks like art direction.** Contrast is normal at ZERO
        // and saturation at ONE; a loader that defaulted both the same way would grey out or square
        // every reflection on the map, with nothing reporting it.
        //
        // Asserted on real materials rather than synthetically, because this measures what arrived
        // rather than what VmtMaterial computes — VmtEnvMapTests already covers the latter.
        MapAssets assets = LoadTheMap();

        foreach (MapCubemap cubemap in assets.Cubemaps.OfType<MapCubemap>())
        {
            cubemap.Contrast.ShouldBeInRange(0f, 1f);
            cubemap.Saturation.ShouldBeInRange(0f, 1f);

            cubemap.Tint.Red.ShouldBeGreaterThan(0f, "a tint of zero would black out the reflection");
        }

        // At least one of them must be at the identity, or the defaults are not being applied at
        // all — most materials name none of these three keys.
        assets.Cubemaps.OfType<MapCubemap>()
            .Count(cubemap => cubemap is { Contrast: 0f, Saturation: 1f, Tint: (1f, 1f, 1f) })
            .ShouldBeGreaterThan(0, "a material naming no envmap keys reflects unchanged");
    }

    [Test]
    public void NoStaticPropMaterialClaimsABakedCubemap()
    {
        // A prop's material is never patched — Cubemap_CreateTexInfo works on texinfo and a prop
        // has none — so one arriving with a cubemap would mean this loader had invented an
        // assignment the map never made.
        MapAssets assets = LoadTheMap();

        for (int index = 0; index < assets.Cubemaps.Count; index++)
        {
            if (assets.Cubemaps[index] is not null)
            {
                assets.Materials[index].Name
                    .ShouldStartWith(
                        $"maps/{MapName}/",
                        Case.Insensitive,
                        "only a material vbsp patched into the map names a baked cubemap");
            }
        }
    }

    private static MapAssets LoadTheMap()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");

            throw new InvalidOperationException("unreachable; Assert.Ignore throws");
        }

        string path = Path.Combine(game, "maps", MapName + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{MapName} is not installed.");
        }

        return MapAssets.Load(
            File.ReadAllBytes(path), GameArchives.Open(game), maximumTextureSize: 32);
    }
}
