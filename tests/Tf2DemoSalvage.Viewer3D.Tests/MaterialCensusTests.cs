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
        // The real finding this was written for, in miniature: two materials want a cubemap and
        // one wants a phong highlight, and neither is implemented.
        IReadOnlyList<(string Parameter, int Materials)> census = MaterialCensus.Unimplemented(
        [
            ["$basetexture", "$envmap"],
            ["$basetexture", "$envmap", "$phong"],
        ]);

        census.Count.ShouldBe(2);

        census[0].Parameter.ShouldBe("$envmap", "the commonest unimplemented parameter comes first");
        census[0].Materials.ShouldBe(2);

        census[1].Parameter.ShouldBe("$phong");
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
            MaterialCensus.Unimplemented([["$envmap", "$envmap", "$ENVMAP"]]);

        census.Single().Materials.ShouldBe(1);
    }
}
