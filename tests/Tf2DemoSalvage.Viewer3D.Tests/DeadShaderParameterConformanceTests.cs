using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A material parameter TF2 ships and nothing consumes.
/// </summary>
/// <remarks>
/// **`$modblend` was this project's standing example of "the SDK cannot answer this, decompile
/// it".** It needed no decompiler: the shipped VMTs answer it, and the answer is that the parameter
/// is dead.
///
/// Established in three steps, and only the first is assertable here — the other two depend on a
/// local Steam library and are recorded in `docs/findings/12-shader-parity.md` with their evidence
/// class stated:
///
/// 1. **No published shader declares it** — assertable, and it is the fact that makes the rest
///    matter. A parameter no shader declares is ignored by the material system.
/// 2. No shipped binary contains the string, against 515 parameter names extracted from
///    `stdshader_dx9.dll`.
/// 3. It appears in three shipped VMTs, and in every one the only thing that reads it is a
///    **commented-out `Equals` proxy** four lines below it.
///
/// **The point of the test is the comparison, not the absence.** Asserting that `$modblend` is
/// missing from the SDK proves nothing on its own — a typo would pass. It is checked against
/// parameters that are unquestionably present, so the measurement is "this one is absent where those
/// are present" rather than "grep found nothing".
/// </remarks>
public sealed class DeadShaderParameterConformanceTests
{
    /// <summary>Where Valve's published shaders live.</summary>
    private const string Shaders = "src/materialsystem/stdshaders";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void NoPublishedShaderDeclaresModblendWhileNeighbouringParametersAbound()
    {
        // The controls come first deliberately. If these are zero the search is broken, and the
        // interesting result below would be an artefact of the instrument rather than a fact about
        // the format — which has already happened twice in this project, most recently when a grep
        // for "$envmap" returned 0 through bad shell escaping and briefly looked like a finding.
        int envmap = ShadersDeclaring("$envmap");
        int detail = ShadersDeclaring("$detail");

        envmap.ShouldBeGreaterThan(20, "the control parameter should be widely declared");
        detail.ShouldBeGreaterThan(20, "the control parameter should be widely declared");

        // The measurement, meaningful only against those controls.
        ShadersDeclaring("$modblend").ShouldBe(0);
    }

    [Test]
    public void ModulateIsARealShaderSoTheMaterialsCarryingItAreNotBroken()
    {
        // The materials that declare $modblend use the Modulate shader, which IS published. So they
        // are ordinary working materials carrying one parameter nothing reads — not broken files,
        // and not evidence of a shader this project is missing.
        //
        // Worth pinning because the tempting reading of "TF2 uses a parameter we cannot find" is
        // "there is an unpublished shader", and that reading is wrong here.
        SourceSdk.Files(Shaders, "modulate*.cpp").Any().ShouldBeTrue();
    }

    /// <summary>How many published shader sources mention a parameter.</summary>
    /// <remarks>
    /// **Every file type, not just <c>.cpp</c>.** A parameter is declared in the C++ shader and used
    /// in the <c>.fxc</c> and <c>.h</c> beside it, so restricting to one extension undercounts by
    /// roughly three times — <c>$envmap</c> reads 8 over <c>.cpp</c> and 28 over the directory. The
    /// first version of this test used <c>.cpp</c> and set its threshold from the shell measurement
    /// of the second, which is how a control ends up failing against correct data.
    /// </remarks>
    private static int ShadersDeclaring(string parameter) =>
        SourceSdk.Files(Shaders, "*")
            .Select(SourceSdk.Text)
            .Count(text => text is not null &&
                text.Contains(parameter, System.StringComparison.OrdinalIgnoreCase));
}
