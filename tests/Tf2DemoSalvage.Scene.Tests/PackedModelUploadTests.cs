using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That geometry packed outside the uploader's own call still reaches the device (B363).
/// </summary>
/// <remarks>
/// **Both of these were found by a picture, not by a test.** The map's detail models packed
/// correctly and drew nothing, and the only symptom was the renderer's
/// *"was posed before its geometry was uploaded"* — once per instance, on a model whose geometry
/// was sitting in the set.
///
/// Two independent causes, and each is a general fault rather than a detail-prop one:
///
/// - **"Did it grow" was asked of the CALL.** `MomentScene` uploads when its own `Add` returns
///   true, so a set grown at the level boundary never triggered one.
/// - **An empty entry is permanent.** `Add` remembers a failed load so the loader is not re-asked
///   every frame — right for the per-frame path, and it meant a path packed before the map's
///   geometry loader existed could never be packed again.
/// </remarks>
public sealed class PackedModelUploadTests
{
    [Test]
    public void Grown_AfterPackingSomething_IsTrueUntilUploadedIsCalled()
    {
        EntityModelSet models = new();

        models.Grown.ShouldBeFalse("a fresh set has nothing the device has not seen");

        models.Precache(["models/props_foliage/grass_02_detailmodel.mdl"]);

        models.Grown.ShouldBeTrue("the set grew, however it grew");

        models.Uploaded();

        models.Grown.ShouldBeFalse();
    }

    /// <remarks>
    /// **The control: an upload does not un-grow a set that grew after it.** Without this,
    /// "cleared by Uploaded" and "cleared by anything" read the same.
    /// </remarks>
    [Test]
    public void Grown_WhenTheSetGrowsAgainAfterAnUpload_IsTrueAgain()
    {
        EntityModelSet models = new();

        models.Precache(["models/a.mdl"]);
        models.Uploaded();
        models.Precache(["models/b.mdl"]);

        models.Grown.ShouldBeTrue();
    }

    /// <remarks>
    /// **A precache is a deliberate request, so it retries an EMPTY entry.** The first call packs
    /// nothing because the loader answers null — which is what a map's detail model met, packed by
    /// the demo's precache before the map's loader existed. The second call has a loader and must
    /// pack it rather than skipping a path it has seen.
    /// </remarks>
    [Test]
    public void Precache_ForAPathThatPackedEmpty_PacksItOnceGeometryIsAvailable()
    {
        EntityModelSet models = new();

        models.Precache([Path]);
        models.Geometry(Path).ShouldBeNull("nothing could be loaded the first time");

        models.Geometry = ModelFramesFixture.OneTriangle;

        models.Precache([Path]);

        models.Geometry(Path).ShouldNotBeNull("the second, deliberate request must pack it");
    }

    /// <remarks>
    /// **The control, and the reason this is not simply "always re-pack".** A path that packed real
    /// geometry is left alone: re-reading it would throw away the work and re-open the file every
    /// time anything precached.
    /// </remarks>
    [Test]
    public void Precache_ForAPathThatAlreadyPacked_DoesNotReadItAgain()
    {
        int reads = 0;

        EntityModelSet models = new()
        {
            Geometry = path =>
            {
                reads++;
                return ModelFramesFixture.OneTriangle(path);
            },
        };

        models.Precache([Path]);
        models.Precache([Path]);

        reads.ShouldBe(1);
    }

    private const string Path = "models/props_foliage/grass_02_detailmodel.mdl";
}
