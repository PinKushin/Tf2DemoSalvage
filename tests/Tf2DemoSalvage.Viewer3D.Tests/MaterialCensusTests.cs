using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Counting what a map's materials ask for that the renderer does not do.
/// </summary>
/// <remarks>
/// **Written because the log went silent through an hour of searching.** Every control point on
/// cp_process drew as a black disc while every material resolved successfully, so nothing was
/// logged: the viewer reports what fails to LOAD and never what a surface resolved TO. The gap
/// that mattered — 43 of 189 materials declaring <c>$envmap</c>, which is not implemented — was
/// found by writing a throwaway probe, and would have been one line of startup log.
///
/// Same shape as <c>measure-the-output-not-the-capability</c>: a report built only from failures
/// reads clean while every instance quietly falls back.
/// </remarks>
public sealed class MaterialCensusTests
{
    [Test]
    public void AParameterTheRendererIgnores_IsCountedByHowManyMaterialsAskForIt()
    {
        // The real finding this was written for, in miniature: two materials want an authored
        // lighting curve and one wants a rim light, and neither is implemented.
        //
        // **These examples were $envmap and $phong, and BOTH have now graduated** — $envmap first,
        // then $phong on 2026-08-21 — so the census correctly stopped reporting them and this test
        // correctly failed, twice. That churn is the feature rather than a nuisance: an example here
        // is a claim about what this project does NOT do, and it should stop compiling the moment
        // that stops being true.
        //
        // The comment above this one predicted its own second failure almost word for word —
        // "whoever implements phong will land in this file" — and that is what happened. Written to
        // be found, and found.
        IReadOnlyList<(string Parameter, int Materials)> census = MaterialCensus.Unimplemented(
        [
            ["$basetexture", "$lightwarptexture"],
            ["$basetexture", "$lightwarptexture", "$rimlight"],
        ]);

        census.Count.ShouldBe(2);

        census[0].Parameter.ShouldBe(
            "$lightwarptexture", "the commonest unimplemented parameter comes first");

        census[0].Materials.ShouldBe(2);

        census[1].Parameter.ShouldBe("$rimlight");
        census[1].Materials.ShouldBe(1);
    }

    [Test]
    public void AParameterTheRendererImplements_IsNotReported()
    {
        // **The control, and without it the census is worthless.** A report that named every
        // parameter would list $basetexture at the top of every map and bury the one that matters.
        MaterialCensus.Unimplemented(
        [
            ["$basetexture", "$bumpmap", "$detail", "$translucent", "$alphatest", "$selfillum"],
        ]).ShouldBeEmpty();
    }

    [Test]
    public void AParameterWithNoRenderingEffect_IsNotReportedEither()
    {
        // $surfaceprop picks a footstep sound and %keywords is a search tag for Hammer. Neither is
        // unimplemented in any sense worth a line of log - they are not ours to implement - and
        // listing them would put two entries above $envmap on every TF2 map.
        MaterialCensus.Unimplemented([["$surfaceprop", "%keywords", "$basetexture"]]).ShouldBeEmpty();
    }

    [Test]
    public void ParametersAreCountedOncePerMaterial_NotOncePerDeclaration()
    {
        // A patched material can name the same key in the patch and in what it includes. Counting
        // declarations rather than materials would report more materials than the map contains,
        // which is the kind of number that gets quoted into a document and then disbelieved.
        IReadOnlyList<(string Parameter, int Materials)> census =
            MaterialCensus.Unimplemented([["$rimlight", "$rimlight", "$RIMLIGHT"]]);

        census.Single().Materials.ShouldBe(1);
    }

    [Test]
    public void AShaderTheRendererDoesNotReproduce_IsCounted()
    {
        // **The case the parameter census could not see.** Modulate multiplies the framebuffer
        // purely by BEING Modulate — it declares nothing the renderer did not already know — so it
        // passed a census of parameters in silence while every capture point drew as a dark slab
        // for a session and a half.
        //
        // A material's shader decides what its parameters mean, so an unhandled shader is the
        // larger gap and the better-hidden one.
        IReadOnlyList<(string Shader, int Materials)> census = MaterialCensus.UnimplementedShaders(
            ["Refract", "Refract", "Water", "LightmappedGeneric", "VertexLitGeneric"]);

        census.Count.ShouldBe(2, "the two implemented shaders should not be reported");
        census[0].ShouldBe(("Refract", 2), "commonest first");
        census[1].ShouldBe(("Water", 1));
    }

    [Test]
    public void TheShadersThisProjectImplements_AreNotReported()
    {
        // **The control, and it is the assertion that decays.** Every entry here is a shader whose
        // behaviour is actually reproduced; when one stops being reproduced, or a new one is
        // implemented and not added, this is what says so. Modulate and UnLitTwoTexture are in the
        // list because they were implemented — before that they belonged in the test above.
        MaterialCensus.UnimplementedShaders(
        [
            "LightmappedGeneric",
            "VertexLitGeneric",
            "UnlitGeneric",
            "WorldVertexTransition",
            "UnLitTwoTexture",
            "Modulate",
            "Patch",
        ]).ShouldBeEmpty();
    }

    [Test]
    public void AMaterialWithNoShaderName_IsNotCounted()
    {
        // A material that failed to parse has no shader, and reporting it as an unimplemented one
        // named "" would put a blank entry at the top of a log people are meant to read.
        MaterialCensus.UnimplementedShaders([""]).ShouldBeEmpty();
    }
}
