using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The `TF2VIEW_PICK` line, as it left the view (D214) [no engine counterpart: a debugging instrument of this viewer]:
/// the request parsed, the camera's ray through the pixel, the drawn models near that ray and the quads that project
/// near the pixel.
/// </summary>
/// <remarks>
/// The camera stands at the origin looking down +x over a 1280 by 720 viewport, so the centre pixel's ray is the x axis
/// and a point on it projects to (640, 360).
///
/// **Not here:** the live bursts a pick names, which need a particle system that has emitted; `demostf-koth_harvest_event`
/// at tick 1500 holds the whole line, byte for byte, before and after the move.
/// </remarks>
public sealed class PickReportTests
{
    private const string Centre = "640 360";

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly FreeCamera AlongX = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

    private static readonly ParticleMaterial Glow = new(null, [], SpriteBlend.Additive);

    private static SceneProp Prop(int entity, string model, float x, float y) =>
        new(entity, model, SceneModelKind.Studio, new ScenePose { X = x, Y = y, Z = 0f });

    private static PickFrame Frame(
        IReadOnlyList<SceneProp>? drawn = null, IReadOnlyList<ParticleBatch>? particles = null, FreeCamera? viewing = null, bool camera = true) =>
        new(null, 4321, camera ? viewing ?? AlongX : null, 1280, 720, drawn, particles ?? [], [], new ParticleEffects());

    private static string Report(string asked, PickFrame frame, MapLevel? world = null, IReadOnlyDictionary<string, ParticleMaterial>? named = null) =>
        PickReport.DescribeOver(asked, frame, world, named, null).ShouldNotBeNull();

    /// <summary>One quad of six corners, a unit out from a centre.</summary>
    private static ParticleBatch Quad(float x, float y, float z, ParticleMaterial material)
    {
        (float Dy, float Dz)[] corners = [(-1f, -1f), (1f, -1f), (1f, 1f), (-1f, -1f), (1f, 1f), (-1f, 1f)];

        return new ParticleBatch(
            [.. Array.ConvertAll(corners, corner => new DetailSpriteVertex(x, y + corner.Dy, z + corner.Dz, 0f, 0f, 0.25f, 0.5f, 0.75f, 1f, 0f, 0f, 0f))],
            material);
    }

    [TestCase(null, TestName = "DescribeOver_WithNoRequest_IsNothing")]
    [TestCase("640", TestName = "DescribeOver_ARequestWithOneNumber_IsNothing")]
    [TestCase("left 360", TestName = "DescribeOver_ARequestThatIsNotNumbers_IsNothing")]
    public void DescribeOver_ARequestItCannotRead_IsNothing(string? asked) =>
        PickReport.DescribeOver(asked, Frame(), null, null, null).ShouldBeNull();

    [Test]
    public void DescribeOver_BeforeAnyFrameWasDrawn_IsNothing() =>
        PickReport.DescribeOver(Centre, Frame(camera: false), null, null, null).ShouldBeNull();

    [Test]
    public void DescribeOver_TheCentrePixel_SaysTheTickTheEyeAndTheRayDownTheCamerasForward()
    {
        string report = Report(Centre, Frame(viewing: new FreeCamera { Origin = (10f, 20f, 30f), Angles = (0f, 0f, 0f) }));

        report.ShouldStartWith("pick (640 360) at tick 4321 from (10 20 30) along (1.000 ");
        report.ShouldNotContain("world at", Case.Sensitive, "no map, so no world hit");
    }

    [Test]
    public void DescribeOver_APixelRightOfCentre_CastsItsRayToTheCamerasRight()
    {
        // Source's left is +y, so a pixel right of centre looks toward -y.
        string report = Report("1000 360", Frame([Prop(1, "models/right.mdl", 100f, -45f), Prop(2, "models/ahead.mdl", 100f, 300f)]));

        report.ShouldMatch(@"along \(0\.\d+ -0\.\d+ ", "forward and to the right");
        report.ShouldContain("models/right.mdl");
        report.ShouldNotContain("models/ahead.mdl");
    }

    [Test]
    public void DescribeOver_WithAMap_SaysWhereTheRayMeetsTheWorld()
    {
        Report(Centre, Frame(), Empty).ShouldContain("world at 16000 units (16000 ", Case.Sensitive, "nothing in the way: the ray's whole length");
    }

    [Test]
    public void DescribeOver_DrawnModelsNearTheRay_AreListedNearestTheRayFirstWithTheirDistances()
    {
        string report = Report(Centre, Frame(
        [
            Prop(1, "models/far_off.mdl", 200f, 50f),
            Prop(2, "models/near.mdl", 100f, 5f),
            Prop(3, "models/behind.mdl", -100f, 0f),
            Prop(4, "models/beside.mdl", 100f, 128f),
        ]));

        report.ShouldContain("[models/near.mdl entity 2 kind Studio 100 along, 5 off, at (100 5 0) ");
        report.ShouldContain("[models/far_off.mdl entity 1 kind Studio 200 along, 50 off, at (200 50 0) ");
        report.IndexOf("models/near.mdl", StringComparison.Ordinal).ShouldBeLessThan(report.IndexOf("models/far_off.mdl", StringComparison.Ordinal));
        report.ShouldNotContain("models/behind.mdl", Case.Sensitive, "behind the eye");
        report.ShouldNotContain("models/beside.mdl", Case.Sensitive, "128 units off the ray is not under it");
    }

    [Test]
    public void DescribeOver_MoreThanEightModelsNearTheRay_ListsEight()
    {
        List<SceneProp> drawn = [];

        for (int index = 0; index < 10; index++)
        {
            drawn.Add(Prop(index + 1, $"models/row{index}.mdl", 100f + index, index));
        }

        string report = Report(Centre, Frame(drawn));

        report.Split("[models/row").Length.ShouldBe(9, "eight entries");
        report.ShouldNotContain("models/row9.mdl", Case.Sensitive, "the two farthest from the ray are left out");
    }

    [Test]
    public void DescribeOver_AQuadThatProjectsOntoThePixel_IsNamedWithItsCentreItsSizeAndItsColour()
    {
        Dictionary<string, ParticleMaterial> named = new(StringComparer.OrdinalIgnoreCase) { ["effects/glow"] = Glow };

        string report = Report(Centre, Frame(particles: [Quad(100f, 0f, 0f, Glow)]), named: named);

        report.ShouldContain("<quad effects/glow centre (100 0 0) at (640 360) corner 1.4 out rgba (0.25 0.50 0.75 1.00) blend Additive> ");
    }

    [Test]
    public void DescribeOver_AQuadOfAMaterialNobodyNamed_IsAQuestionMark() =>
        Report(Centre, Frame(particles: [Quad(100f, 0f, 0f, Glow)])).ShouldContain("<quad ? centre (100 0 0) ");

    [Test]
    public void DescribeOver_AQuadAwayFromThePixel_IsListedOnlyWhenTheRequestIsWideEnough()
    {
        // Twenty units to the side at a hundred ahead: well past 24 pixels from the centre, well inside 400.
        PickFrame frame = Frame(particles: [Quad(100f, 20f, 0f, Glow)]);

        Report(Centre, frame).ShouldNotContain("<quad");
        Report("640 360 400", frame).ShouldContain("<quad ? centre (100 20 0) ");
    }

    [Test]
    public void DescribeOver_AQuadBehindTheCamera_IsNotListed() =>
        Report("640 360 2000", Frame(particles: [Quad(-100f, 0f, 0f, Glow)])).ShouldNotContain("<quad");
}
