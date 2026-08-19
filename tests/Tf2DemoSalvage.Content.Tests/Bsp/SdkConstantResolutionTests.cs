using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A control on the instrument: which of two declarations of one name the reader returns.
/// </summary>
/// <remarks>
/// **`bspfile.h` declares roughly thirty capacity constants twice**, and the second declaration of
/// every one of them is the literal <c>2</c>. Lines 105 to 148 are the <c>#else</c> of
/// <c>BSP_USE_LESS_MEMORY</c> — a build that keeps the type definitions and throws away the
/// capacities, so a tool can walk a BSP without allocating for one. Nothing on those lines says so;
/// they are an unbroken run of <c>#define MAX_MAP_SOMETHING 2</c>.
///
/// **This has already produced one wrong constant in this project.** <c>MAX_MAP_TEXDATA</c> was read
/// as 2, which is a plausible-looking number for a limit and is wrong by six orders of magnitude.
/// The failure mode is the one that matters: a capacity of 2 does not throw, it makes a guard reject
/// every real map, and the rejection reads as a corrupt file rather than as a bad constant.
///
/// So the resolution rule — **first declaration wins**, implemented as <c>TryAdd</c> in
/// <see cref="SourceSdk"/> — is load-bearing across every capacity test in this suite, and it is
/// invisible. Changing that one call to an indexer assignment would silently flip thirty constants
/// to 2 and break nothing that says why.
///
/// **The second assertion is the control, and it is the point of the class.** Checking only that
/// <c>MAX_MAP_VISIBILITY</c> is large would pass just as happily if Valve deleted the stub block
/// entirely — the test would then be measuring nothing while still going green. Asserting that the
/// stub is *present and not what was returned* is what makes this an experiment: it requires two
/// declarations to exist and requires the reader to have chosen between them.
/// </remarks>
public sealed class SdkConstantResolutionTests
{
    /// <summary>The header that carries both declarations of every map capacity.</summary>
    private const string BspHeader = "src/public/bspfile.h";

    /// <summary>What the low-memory build declares every capacity as.</summary>
    private const int LowMemoryStub = 2;

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void SdkConstants_TheFirstDeclaration_WinsOverTheLowMemoryStub()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspHeader);

        // 0x1000000 from bspfile.h:91, under `#if !defined( BSP_USE_LESS_MEMORY )`. The comment
        // beside it — "increased BSPVERSION 7" — is itself a small piece of history: this is a limit
        // Valve raised once, so it is a real capacity rather than a round number chosen for looks.
        constants["MAX_MAP_VISIBILITY"].ShouldBe(0x1000000);
    }

    [Test]
    public void SdkConstants_TheStubBlock_IsStillPresentToChooseAgainst()
    {
        // The control. Without this, the test above passes unchanged in a world where the ambiguity
        // it exists to resolve no longer exists — and a passing test that measures nothing is worse
        // than no test, because it is counted.
        string header = SourceSdk.Text(BspHeader).ShouldNotBeNull();

        header.ShouldContain("#define\tMAX_MAP_VISIBILITY\t\t\t\t2");
    }

    [Test]
    public void SdkConstants_EveryStubCapacity_ResolvesAwayFromTwo()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspHeader);

        // **Swept rather than sampled, because the stub block is a block.** One name proves the rule
        // for one name; the defect this guards against — a resolution policy changing — would move
        // all of them at once, and a single sample gives no sense of how much rides on it.
        //
        // MAX_MAP_DISP_VERTS and MAX_MAP_DISP_TRIS are deliberately absent: they are derived from
        // MAX_MAP_DISPINFO by arithmetic rather than declared, so they are a test of the expression
        // parser and not of this rule.
        string[] swept =
        [
            "MAX_MAP_ENTITIES", "MAX_MAP_TEXINFO", "MAX_MAP_TEXDATA", "MAX_MAP_DISPINFO",
            "MAX_MAP_AREAS", "MAX_MAP_AREAPORTALS", "MAX_MAP_PLANES", "MAX_MAP_NODES",
            "MAX_MAP_BRUSHSIDES", "MAX_MAP_LEAFS", "MAX_MAP_VERTS", "MAX_MAP_FACES",
            "MAX_MAP_LEAFFACES", "MAX_MAP_LEAFBRUSHES", "MAX_MAP_CLUSTERS", "MAX_MAP_EDGES",
            "MAX_MAP_SURFEDGES", "MAX_MAP_LIGHTING", "MAX_MAP_VISIBILITY", "MAX_MAP_TEXTURES",
            "MAX_MAP_WORLDLIGHTS", "MAX_MAP_CUBEMAPSAMPLES", "MAX_MAP_OVERLAYS",
        ];

        foreach (string name in swept)
        {
            constants.ShouldContainKey(name);
            constants[name].ShouldBeGreaterThan(
                LowMemoryStub,
                $"{name} resolved to the BSP_USE_LESS_MEMORY stub rather than the real capacity.");
        }
    }
}
