using System;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Valve's fog arithmetic, and the fact that this renderer does not yet apply any of it.
/// </summary>
/// <remarks>
/// **The conformance sweep turned this file into a gap report, which is what it always was.**
/// Its four tests quoted <c>common_fxc.h</c> and <c>fogcontroller.cpp</c> and asserted the
/// arithmetic in local helper functions — arithmetic written in the test, transcribed from Valve,
/// compared against itself. Nothing in <c>Tf2DemoSalvage.Viewer3D</c> was ever involved.
///
/// **It is not involved because there is nothing to involve.** <see cref="SceneFog"/> is decoded per
/// tick, retained on the timeline, and read by no production code anywhere — the only consumers of
/// <c>DemoTimeline.FogSamples</c> and <c>FogAt</c> in the entire repository are tests. Filed as
/// **B139**.
///
/// That is the fourth instance of the pattern in
/// <c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>: a value decoded, retained,
/// unit-tested and never read. <c>m_flPlaybackRate</c> was the third, and every animation played at
/// rate 1 for as long as it lasted.
///
/// **So the citations stay and the arithmetic goes.** The equations below are the SPECIFICATION for
/// an unimplemented feature, which is a legitimate thing for a conformance test to hold (D45) — but
/// only if it says so, and only if something fails when the feature arrives without its parity
/// check. The gap assertion at the end is that trigger.
/// </remarks>
public sealed class FogConformanceTests
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
    public void Fog_TheEquations_AreRecordedForAnImplementationThatDoesNotExistYet()
    {
        // **Kept as citations rather than as assertions on transcribed arithmetic.** The previous
        // version computed `Squared(0.5f).ShouldBe(0.25f)` against a helper defined in the same
        // file, which tests that squaring squares.
        //
        // What each line is for, when this is built:
        //
        //   lerp( vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor )
        //       range fog SQUARES the factor before the lerp, and Valve says why in a comment
        //       beside it. A linear blend reads as haze rather than as Source's fog.
        //
        //   saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) )
        //       maxdensity clamps BEFORE the saturate, so a controller asking for 0.6 never
        //       reaches full fog however far away the surface is. Clamping after would ignore it.
        //
        //   const float flFogStartOverRange
        //       the first fog constant is start/(end-start), NOT the start distance, despite the
        //       macro `g_FogEndOverRange` twelve lines away suggesting otherwise. Feeding it a
        //       distance puts the fog's onset in the wrong place by a factor of the range.
        string source = Sdk("src/materialsystem/stdshaders/common_ps_fxc.h");

        source.ShouldContain(
            "return lerp( vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor );",
            Case.Sensitive,
            "range fog squares the factor");

        source.ShouldContain(
            "squaring the factor will get the middle range mixing closer to hardware fog",
            Case.Sensitive,
            "and Valve states the reason rather than leaving it to be inferred");

        source.ShouldContain(
            "return saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) );",
            Case.Sensitive,
            "maxdensity clamps before the saturate");

        source.ShouldContain("const float flFogStartOverRange", Case.Sensitive);
        source.ShouldContain("#define g_FogEndOverRange", Case.Sensitive);
    }

    [Test]
    public void Fog_NothingInThisRendererReadsTheDecodedFog_WhichIsB139()
    {
        // **The gap, measured rather than described.** SceneFog carries six floats decoded from
        // DT_FogController; if the renderer applied them there would be a consumer. There is not
        // one, so this asserts the absence in a form that FAILS when fog is implemented — at which
        // point this test is replaced by a parity check against the equations above.
        //
        // Measured by type reference rather than by grepping source: the Viewer3D assembly is the
        // thing that would have to mention SceneFog to use it, and an assembly cannot be out of
        // date with itself the way a text search can.
        bool referenced = false;

        foreach (Type type in typeof(WorldRenderer).Assembly.GetTypes())
        {
            foreach (System.Reflection.FieldInfo field in type.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static))
            {
                referenced |= Mentions(field.FieldType);
            }

            foreach (System.Reflection.MethodInfo method in type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                referenced |= Mentions(method.ReturnType);

                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    referenced |= Mentions(parameter.ParameterType);
                }
            }
        }

        referenced.ShouldBeFalse(
            "SceneFog now appears in the renderer's surface — so fog is being implemented, and "
            + "this gap marker should be replaced by a parity test against the equations in "
            + "Fog_TheEquations_AreRecordedForAnImplementationThatDoesNotExistYet (B139, D45)");

        // **The control, and it is the assertion that makes the one above mean anything.** A
        // reflection sweep that found no types at all, or one looking in the wrong assembly, would
        // also report "not referenced". A type the renderer demonstrably DOES use must be found by
        // the same sweep.
        bool findsAKnownConsumer = false;

        foreach (Type type in typeof(WorldRenderer).Assembly.GetTypes())
        {
            foreach (System.Reflection.MethodInfo method in type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    // **The control's subject changed with the split (D61) and that is why it is a
                    // control.** It named DemoTimeline, which the FORM consumes; when the renderer
                    // moved to its own assembly the sweep followed WorldRenderer correctly and the
                    // control went red, because no method in the render layer takes a timeline. The
                    // claim above was still true, so without this the suite would have gone on
                    // asserting an unfalsifiable "not referenced".
                    //
                    // MapAssets is the replacement: WorldRenderer.UploadTextures takes one, so it
                    // is a scene type the renderer demonstrably does use.
                    findsAKnownConsumer |= parameter.ParameterType == typeof(MapAssets)
                        || parameter.ParameterType.Name.Contains("ModelInstance", StringComparison.Ordinal);
                }
            }
        }

        findsAKnownConsumer.ShouldBeTrue(
            "the sweep must be able to find a scene type the renderer really uses, or its failure "
            + "to find SceneFog says nothing");

        static bool Mentions(Type type) =>
            type == typeof(SceneFog)
            || type == typeof(SceneFog?)
            || (type.IsGenericType && Array.Exists(type.GetGenericArguments(), Mentions));
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
