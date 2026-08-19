using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// The demo header, derived from Valve's declaration rather than from measurement.
/// </summary>
/// <remarks>
/// **`findings/01-container.md` opens: "the only [layer] whose code Valve has never published —
/// `engine.dll` holds the `.dem` reader and is not in the SDK. Everything here was established by
/// measurement."**
///
/// The first half is true: there is no reader in the SDK, and `public/demofile/` contains exactly one
/// file. The second half overstates it. That one file is `demoformat.h`, and it declares
/// `demoheader_t` in full, the `dem_*` command enumeration with values, and `DEMO_HEADER_ID`.
///
/// **Nothing in this project cited it — not the finding, not the code, not a test.** The container
/// was therefore the only layer with no conformance check against the SDK, because the SDK was
/// believed to have nothing to check against. It was measured correctly, which is why this test
/// passes on the first run; what it adds is that the layout is now pinned to Valve's declaration and
/// would fail if either side moved.
///
/// Fifth instance of an absence being recorded more broadly than the evidence supported. The others
/// are listed in <c>findings/05-user-messages.md</c>.
/// </remarks>
public sealed class DemoHeaderConformanceTests
{
    /// <summary>The one file Valve publishes about the container.</summary>
    private const string DemoFormat = "src/public/demofile/demoformat.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void DemoHeader_TheHeaderSize_IsDerivedFromValvesStructure()
    {
        string source = SourceSdk.Text(DemoFormat).ShouldNotBeNull();

        // MAX_OSPATH is declared in this header behind an #if, so it is supplied rather than parsed
        // — the four path fields are what make the header 1072 bytes and getting it wrong would
        // change the answer by a multiple of four.
        CLayoutAttempt header = CStruct.Attempt(
            source,
            "demoheader_t",
            constants: new Dictionary<string, int> { ["MAX_OSPATH"] = 260 });

        header.Refused.ShouldBeNull();

        // 8 + 4 + 4 + (260 * 4) + 4 + 4 + 4 + 4. Derived by the C layout engine from the member
        // list, not transcribed — which is the whole point, because a stride can be right while the
        // fields inside it are read from the wrong offsets.
        header.Layout.ShouldNotBeNull().Size.ShouldBe(DemoHeader.SizeBytes);
    }

    [Test]
    public void DemoHeader_TheStampAndCommandNames_AreValvesOwn()
    {
        string source = SourceSdk.Text(DemoFormat).ShouldNotBeNull();

        // The magic this project checks for, and the command list it decodes. Both were established
        // by measurement and both are declared here.
        source.ShouldContain("#define DEMO_HEADER_ID\t\t\"HL2DEMO\"");

        foreach (string command in new[]
        {
            "dem_signon", "dem_packet", "dem_synctick", "dem_consolecmd",
            "dem_usercmd", "dem_datatables", "dem_stop", "dem_stringtables",
        })
        {
            source.ShouldContain(command);
        }

        // dem_stringtables is last, and dem_lastcmd names it — which is why a protocol that predates
        // it simply has one fewer command rather than a gap in the middle.
        source.ShouldContain("dem_lastcmd\t\t= dem_stringtables");
    }
}
