using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
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

    /// <summary>The disc B83 spent five hypotheses on, and the object this feature is for.</summary>
    private const string CapturePoint = "models/props_gameplay/cap_point_base.mdl";

    [Test]
    public void CubemapLoading_PatchedMaterials_ArriveCarryingTheirCubemap()
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
    public void CubemapLoading_TheMapsOwnPlacements_AreDecodedAndPlaced()
    {
        // **The half a brush face never needs, and therefore the half nothing checked.** vbsp chose
        // each brush face's cubemap at compile time and baked the name into its material, so the
        // world path resolves names and never asks where anything is. A model's material still says
        // the literal `env_cubemap` and picks by position, so the positions have to arrive.
        MapAssets assets = LoadTheMap();
        IReadOnlyList<BspCubemap> lump = BspCubemaps.Read(MapCache.Bytes(MapName));

        TestContext.Out.WriteLine(
            $"{assets.PlacedCubemaps.Count} placements decoded of {lump.Count} in the lump");

        assets.PlacedCubemaps.Count
            .ShouldBe(lump.Count, "every placement this map bakes is packed and decodes");

        // **Position and texture must stay paired, and this is the assertion that says so.** They
        // are two lists built in one loop; a placement dropped from one and not the other shifts
        // every reflection after it onto the wrong cube, which draws as a plausible picture.
        foreach (MapPlacedCubemap placed in assets.PlacedCubemaps)
        {
            placed.Faces.Count.ShouldBe(6);
        }

        assets.PlacedCubemaps.Select(placed => placed.Placement).ShouldBe(lump);
    }

    [Test]
    public void CubemapLoading_AModelMaterial_CarriesALocalReflectionAndNoCubemap()
    {
        // **The capture point, which is the object this was built for.** B83 spent five hypotheses
        // on why its disc draws almost black, and the answer predicted at the end of that entry was
        // "$envmap on a prop". Its material asks for the literal `env_cubemap`, which vbsp cannot
        // patch because Cubemap_CreateTexInfo works on texinfo and a model has none — so it arrived
        // with no cubemap at all and drew matte.
        //
        // Loaded WITH the model, because a map's own material table does not contain a prop's
        // materials; those are appended when the model is registered.
        MapAssets assets = MapCache.Load(entityModels: [CapturePoint]);

        int local = assets.LocalReflections.Count(shading => shading is not null);

        TestContext.Out.WriteLine(
            $"{local} materials of {assets.LocalReflections.Count} ask for the map's own cubemap");

        local.ShouldBeGreaterThan(0, "a model's $envmap is the literal env_cubemap");

        // **Named, not counted.** "Some material asks for the map's cubemap" is satisfied by any of
        // the map's own static props, and would pass with the capture point still matte — which is
        // the exact shape of B55's original dismissal of B83: a survey that found nothing because
        // it was looking at the wrong object, read as evidence about the object.
        string[] capPoint =
        [
            .. Enumerable.Range(0, assets.LocalReflections.Count)
                .Where(index => assets.LocalReflections[index] is not null)
                .Select(index => assets.Materials[index].Name)
                .Where(name => name.Contains("cap_point", StringComparison.OrdinalIgnoreCase)),
        ];

        TestContext.Out.WriteLine($"cap point materials reflecting: {string.Join(", ", capPoint)}");

        capPoint.ShouldNotBeEmpty(
            "the capture point's own material asks for the map's cubemap — B83's prediction");

        // **The control**, and it is the one that makes this mean something: the great majority of
        // materials ask for no reflection at all, so "some carry a local reflection" and "every
        // material carries one" have to be distinguishable.
        assets.LocalReflections.Count(shading => shading is null)
            .ShouldBeGreaterThan(local, "most materials reflect nothing");

        // **And the two lists are mutually exclusive by construction.** A material either names a
        // concrete cubemap or asks for the map's own; carrying both would mean the shader had two
        // answers and picked by order of binding.
        for (int index = 0; index < assets.LocalReflections.Count; index++)
        {
            if (assets.LocalReflections[index] is not null)
            {
                assets.Cubemaps[index]
                    .ShouldBeNull("a material asking for env_cubemap has no cubemap of its own");
            }
        }
    }

    [Test]
    public void CubemapLoading_AReflectiveModelMaterial_IsMaskedByItsNormalMapAlpha()
    {
        // **The wiring assertion for the mask, and the reason it needs one.** Every piece under it
        // is covered — the VMT flag, the exclusivity rule, the shader branch — and all of that can
        // be right while no production material ever sets it. Three no-ops have shipped in this
        // project with a green suite for exactly that reason.
        //
        // Named rather than counted, because "some material is masked" would pass with the capture
        // point still shining uniformly, which is the object this was built for.
        MapAssets assets = MapCache.Load(entityModels: [CapturePoint]);

        string[] masked =
        [
            .. Enumerable.Range(0, assets.LocalReflections.Count)
                .Where(index => assets.LocalReflections[index] is { MaskedByNormalMapAlpha: true })
                .Select(index => assets.Materials[index].Name),
        ];

        TestContext.Out.WriteLine($"{masked.Length} reflective materials masked by normal-map alpha");

        masked.ShouldContain(
            name => name.Contains("cap_point", StringComparison.OrdinalIgnoreCase),
            "the capture point's reflection is masked by its normal map's alpha");

        // **The control, and it is the exclusivity rule rather than a second sample.** A material
        // cannot carry both masks: the shader declares
        // `SKIP: $NORMALMAPALPHAENVMAPMASK && $BASEALPHAENVMAPMASK`, and the engine clears the
        // base-alpha flag when this one is set. A parser that returned both would send a material
        // down whichever branch happened to be tested first.
        assets.LocalReflections
            .Concat(assets.Cubemaps.Select(cubemap => cubemap?.Shading))
            .Where(shading => shading is not null)
            .ShouldAllBe(shading =>
                !(shading!.Value.MaskedByNormalMapAlpha && shading.Value.MaskedByBaseAlpha),
                "the two reflection masks are mutually exclusive by construction");
    }

    [Test]
    public void CubemapLoading_EveryCarriedCubemap_HasSixDecodedFaces()
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

                // **A face is now measured in whatever it is stored in (B149).** This asked for
                // `Width * Height * 4`, which only ever described an expanded image — and a baked
                // cubemap is a DXT VTF, so it now arrives as blocks and goes to the device that way,
                // which is what Valve's own material system does with it.
                face.Image.Top.Length.ShouldBe(ExpectedBytes(face));
            }
        }
    }

    [Test]
    public void CubemapLoading_TheSixFaces_DifferFromEachOther()
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
                System.Security.Cryptography.SHA256.HashData(face.Image.ToRgba(face.Width, face.Height))))
            .Distinct(StringComparer.Ordinal)
            .Count();

        TestContext.Out.WriteLine($"{distinct} distinct images across the six faces");

        distinct.ShouldBeGreaterThan(1);
    }

    [Test]
    public void CubemapLoading_ShadingParameters_ArriveAtEngineDefaults()
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
            cubemap.Shading.Contrast.ShouldBeInRange(0f, 1f);
            cubemap.Shading.Saturation.ShouldBeInRange(0f, 1f);

            cubemap.Shading.Tint.Red
                .ShouldBeGreaterThan(0f, "a tint of zero would black out the reflection");
        }

        // At least one of them must be at the identity, or the defaults are not being applied at
        // all — most materials name none of these three keys.
        assets.Cubemaps.OfType<MapCubemap>()
            .Count(cubemap => cubemap.Shading is { Contrast: 0f, Saturation: 1f, Tint: (1f, 1f, 1f) })
            .ShouldBeGreaterThan(0, "a material naming no envmap keys reflects unchanged");

        // **And the same defaults on the model half**, which arrives by a different route — a
        // material that asks for the literal `env_cubemap` carries the shading with no cube, and
        // reads its parameters from the same VMT keys. A loader that applied the defaults on one
        // path and not the other would leave every prop's reflection grey while the map's was
        // right, which is exactly the kind of split that reads as art direction.
        assets.LocalReflections.OfType<MapEnvmapShading>()
            .Count(shading => shading is { Contrast: 0f, Saturation: 1f, Tint: (1f, 1f, 1f) })
            .ShouldBeGreaterThan(0, "a model material naming no envmap keys reflects unchanged");
    }

    [Test]
    public void CubemapLoading_AMaterialWithACubemap_WasPatchedOrNamedOne()
    {
        // **This test used to assert that ONLY map-patched materials carry a cubemap, and that was
        // true because of a bug.** Model materials were appended to three of the seven lists
        // indexed by material number and their cubemaps were "padded" away with nulls, so every
        // prop arrived reflecting nothing and the stronger claim held by accident. Fixing
        // MaterialTable turned 51 into 53.
        //
        // The reasoning behind the old assertion was sound as far as it went — vbsp patches
        // texinfo, and a static prop has none — but the conclusion was too strong. A prop's own VMT
        // can name a concrete envmap texture without vbsp doing anything, and two of this map's do.
        //
        // What is actually invariant is narrower and still worth guarding: a material that carries
        // a cubemap has a name for it, and nothing carrying one is still asking for the literal
        // env_cubemap.
        MapAssets assets = LoadTheMap();

        int patched = 0;
        int namedItself = 0;

        for (int index = 0; index < assets.Cubemaps.Count; index++)
        {
            if (assets.Cubemaps[index] is null)
            {
                continue;
            }

            if (assets.Materials[index].Name.StartsWith(
                    $"maps/{MapName}/", StringComparison.OrdinalIgnoreCase))
            {
                patched++;
            }
            else
            {
                namedItself++;

                TestContext.Out.WriteLine($"names its own: {assets.Materials[index].Name}");
            }
        }

        TestContext.Out.WriteLine($"{patched} patched by vbsp, {namedItself} naming their own");

        // Both kinds must exist, or this is measuring one case and calling it the rule — which is
        // precisely how the old version passed.
        patched.ShouldBeGreaterThan(0, "vbsp patches the map's own reflecting brush faces");

        // The great majority are still the patched ones; a map whose props out-reflected its
        // brushwork would mean the patch resolution had broken.
        patched.ShouldBeGreaterThan(namedItself);
    }

    private static MapAssets LoadTheMap() => MapCache.Load();
    /// <summary>How many bytes one face of this texture should occupy, in its own format.</summary>
    /// <remarks>
    /// **Block formats are measured in blocks of 4x4 texels**, eight bytes for BC1 and sixteen for
    /// BC2 and BC3, with a level rounded up to whole blocks. Anything else is four bytes a pixel.
    /// </remarks>
    private static int ExpectedBytes(MapTexture face)
    {
        if (!face.Image.IsBlockCompressed)
        {
            return face.Width * face.Height * 4;
        }

        int blockBytes = face.Image.Format is VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha ? 8 : 16;
        int across = Math.Max(1, (face.Width + 3) / 4);
        int down = Math.Max(1, (face.Height + 3) / 4);

        return across * down * blockBytes;
    }
}
