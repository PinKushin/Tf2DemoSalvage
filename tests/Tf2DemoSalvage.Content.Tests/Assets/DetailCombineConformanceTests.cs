using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every detail blend mode number, checked against the shader header that defines them.
/// </summary>
/// <remarks>
/// **These are twelve small integers that each select a different equation**, and there is no
/// structure to them — mode 2 is not "more" than mode 1. A material asks for one by number, so being
/// off by one silently renders a surface with a blend it never asked for: <c>TCOMBINE_FADE</c>
/// instead of <c>TCOMBINE_DETAIL_OVER_BASE</c> is a visible difference on the map and no difference
/// at all to any assertion that does not name the mode.
///
/// **Valve defines them in <c>common_ps_fxc.h</c>**, which is a shader header rather than C++ — and
/// the numbers are the shader's own combo values, so they are as authoritative as anything gets for
/// this. The names are Valve's; the meanings this project attaches to them are its own, which is why
/// the mapping is written out pair by pair rather than matched by name.
/// </remarks>
public sealed class DetailCombineConformanceTests
{
    /// <summary>Where the pixel shaders define the combine modes.</summary>
    private const string Common = "src/materialsystem/stdshaders/common_ps_fxc.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryModeWeName_HasTheEnginesNumber()
    {
        IReadOnlyDictionary<string, int> engine = Declared();

        (string Name, int Ours)[] claims =
        [
            ("TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2", DetailCombine.BaseTimesDetailDoubled),
            ("TCOMBINE_RGB_ADDITIVE", DetailCombine.Additive),
            ("TCOMBINE_DETAIL_OVER_BASE", DetailCombine.DetailOverBase),
            ("TCOMBINE_FADE", DetailCombine.Fade),
            ("TCOMBINE_BASE_OVER_DETAIL", DetailCombine.BaseOverDetail),
            ("TCOMBINE_RGB_ADDITIVE_SELFILLUM", DetailCombine.AdditiveSelfIllum),
            ("TCOMBINE_RGB_ADDITIVE_SELFILLUM_THRESHOLD_FADE",
                DetailCombine.AdditiveSelfIllumThresholdFade),
            ("TCOMBINE_MOD2X_SELECT_TWO_PATTERNS", DetailCombine.Mod2xSelectTwoPatterns),
            ("TCOMBINE_MULTIPLY", DetailCombine.Multiply),
            ("TCOMBINE_MASK_BASE_BY_DETAIL_ALPHA", DetailCombine.MaskBaseByDetailAlpha),
            ("TCOMBINE_SSBUMP_BUMP", DetailCombine.SelfShadowBump),
            ("TCOMBINE_SSBUMP_NOBUMP", DetailCombine.SelfShadowBumpNoBump),
        ];

        List<string> wrong = [];

        foreach ((string name, int ours) in claims)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not defined by the engine at all");
            }
            else if (theirs != ours)
            {
                wrong.Add($"{name}: we use {ours}, the engine defines {theirs}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void TheHighestModeIsTheEnginesHighest()
    {
        // **Derived, because this bound is what rejects a malformed material.** A VMT is untrusted
        // input and $detailblendmode is just a number in it; the guard is only correct if it knows
        // where the engine's list actually stops. Hardcoding 11 next to a list of twelve is the kind
        // of duplicate that survives the list growing.
        Declared()
            .Where(mode => mode.Key.StartsWith("TCOMBINE_", StringComparison.Ordinal))
            .Max(mode => mode.Value)
            .ShouldBe(DetailCombine.HighestMode);
    }

    [Test]
    public void TheModesAreContiguousFromZero()
    {
        // The control, and a real property: the modes are a dense range because they index a shader
        // combo. A gap would mean the extraction missed one — which is exactly what happened before
        // this suite could read TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2, whose name has lowercase
        // letters that an uppercase-only pattern skipped.
        int[] modes =
        [
            .. Declared()
                .Where(mode => mode.Key.StartsWith("TCOMBINE_", StringComparison.Ordinal))
                .Select(mode => mode.Value)
                .OrderBy(value => value),
        ];

        modes.ShouldBe(Enumerable.Range(0, modes.Length).ToArray());
        modes.Length.ShouldBe(12);
    }

    /// <summary>Every combine mode the shader header defines.</summary>
    private static IReadOnlyDictionary<string, int> Declared()
    {
        IReadOnlyDictionary<string, int> values = SourceSdk.Constants(Common);

        values.Keys.Count(name => name.StartsWith("TCOMBINE_", StringComparison.Ordinal))
            .ShouldBeGreaterThan(10, $"no combine modes were extracted from {Common}");

        return values;
    }
}
