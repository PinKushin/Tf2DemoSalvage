using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The reader's safety caps, checked against the limits the engine itself allows.
/// </summary>
/// <remarks>
/// **A guard that is too strict rejects a file the game plays.** Every <c>Maximum*</c> in this
/// project exists because a map or model from a download is untrusted input (D32) and a corrupt
/// count would ask for a gigabyte. That is the right instinct and it has a failure mode of its own:
/// set the cap below what Valve's own compiler can emit, and a legitimate asset is refused. The
/// refusal is at least loud — unlike most defects here — but it is still this project failing on
/// correct data.
///
/// So the claim is one-directional and deliberately weak: **no cap may be below the engine's own
/// limit.** Being far above it is fine, because the cap is a sanity bound rather than a schema.
/// Where a number is an array size rather than a bound it must match exactly, and those are
/// asserted separately.
/// </remarks>
public sealed class CapacityGuardTests
{
    /// <summary>Where the engine states its model limits.</summary>
    private const string Studio = "src/public/studio.h";

    /// <summary>Where the engine states its map limits.</summary>
    private const string BspFile = "src/public/bspfile.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheArraySizesMatchExactlyBecauseTheyAreNotBounds()
    {
        // These three are the size of a fixed array in the format, not a limit on a count. An
        // overlay names OVERLAY_BSP_FACE_COUNT faces because the struct has that many slots; a
        // displacement's power decides how many vertices it has. Being generous here is not
        // permissive, it is wrong — a larger number reads past the record.
        IReadOnlyDictionary<string, int> map = SourceSdk.Constants(BspFile);
        IReadOnlyDictionary<string, int> model = SourceSdk.Constants(Studio);

        map["OVERLAY_BSP_FACE_COUNT"].ShouldBe(64);
        map["MAX_MAP_DISP_POWER"].ShouldBe(4);
        model["MAX_NUM_LODS"].ShouldBe(VertexFileLayout.MaximumLods);
    }

    [Test]
    public void NoModelGuardIsStricterThanTheEngineAllows()
    {
        // **MAXSTUDIOBONES is 128 and this project caps at 1024**, which is the shape wanted: the
        // cap is there to refuse a corrupt header, not to enforce Valve's limit. A cap of 64 would
        // reject models the game loads happily.
        IReadOnlyDictionary<string, int> model = SourceSdk.Constants(Studio);

        model["MAXSTUDIOBONES"].ShouldBeLessThanOrEqualTo(
            StudioReaderLimits.Bones,
            "a bone cap below the engine's own limit refuses models TF2 ships");

        model["MAXSTUDIOSKINS"].ShouldBeLessThanOrEqualTo(
            StudioReaderLimits.SkinTableEntries,
            "likewise for skin families");
    }

    [Test]
    public void TheDuplicateMapLimitsResolveToThePcOnes()
    {
        // **bspfile.h defines MAX_MAP_TEXDATA twice — 2048, then 2.** The second set is under an
        // Xbox 360 branch that forces every static array to be tiny. An extractor taking the LAST
        // definition would report the engine's limit as 2 and make every cap in this project look
        // absurdly generous, while quietly inverting the comparison this whole class performs.
        //
        // First-wins is what makes it come out right, and that is worth an assertion rather than a
        // comment, because it is a property of the extractor and not of the header.
        IReadOnlyDictionary<string, int> map = SourceSdk.Constants(BspFile);

        map["MAX_MAP_TEXDATA"].ShouldBe(2048);
        map["MAX_MAP_DISPINFO"].ShouldBe(2048);
        map["MAX_MAP_TEXDATA_STRING_TABLE"].ShouldBe(65536);

        // Stated as the general rule the three above are instances of: none of the PC limits is the
        // console stub, so no comparison in this class is being made against a 2.
        map["MAX_MAP_TEXDATA"].ShouldBeGreaterThan(2);
    }
}
