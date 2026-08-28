using System;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The per-model diagnostics, and what each one is deduped on.
/// </summary>
/// <remarks>
/// **Each of these lines is deduped on something DIFFERENT, and that is the whole design.** Once per
/// model for a fact about the model, once per entity for a fact about where one stands, and on a
/// change of more than a unit for a brush entity that moves. The three are easy to confuse and each
/// confusion has a cost that has already been paid:
///
/// <list type="bullet">
/// <item>too often — a per-frame line printed 1,280 times a second and put 8.2 MB in a log in under
/// two minutes (B163);</item>
/// <item>too rarely — a once-per-MODEL line let one bright control point silence a dark one for
/// ever, while the defect being chased was that one point was dark and its neighbours were not.</item>
/// </list>
///
/// So these tests are about counts, and the interesting assertion in each is the SECOND call.
/// </remarks>
public sealed class ModelReportsTests
{
    [Test]
    public void Lit_TheSameEntityTwice_IsReportedOnce()
    {
        RecordingLogger log = new();
        ModelReports reports = new(log);

        for (int frame = 0; frame < 2; frame++)
        {
            reports.Lit(Prop(entity: 7), Light(), skin: 0);
        }

        log.Count("#7").ShouldBe(1);
    }

    [Test]
    public void Lit_TwoEntitiesSharingAModel_AreBothReported()
    {
        // **Per INSTANCE, not per model, and this is the assertion that pins it.** Five capture
        // points share cap_point_base.mdl; deduping on the path collapses them to one report, and a
        // bright one reporting first silences a dark one for ever. The observation that needed this
        // was that ONE control point was dark and its neighbours were fine — a shape that rules out
        // a missing lighting term, since an absent term darkens every instance equally.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        reports.Lit(Prop(entity: 7), Light(), skin: 0);
        reports.Lit(Prop(entity: 8), Light(), skin: 0);

        log.Count("#7").ShouldBe(1);
        log.Count("#8").ShouldBe(1);
    }

    [Test]
    public void Lit_ABrushEntityWithNoCube_SaysLightmappedRatherThanALuminance()
    {
        // **A number about nothing is worse than no number.** A brush entity carries no ambient
        // cube by design (B131) — its faces were lit by vrad and the samples ride the vertices — so
        // a luminance printed for it would describe something that does not exist. Saying
        // "lightmapped" is what lets this line answer "why is that door flat" without a second run.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        reports.Lit(
            Prop(entity: 3, kind: SceneModelKind.Brush),
            new ModelLight(null, null, 0f, 0f, 0f, []),
            skin: 0);

        log.Count("lightmapped").ShouldBe(1);
        log.Count("luminance").ShouldBe(0);
    }

    [Test]
    public void BrushMoved_AStationaryDoor_IsReportedOnceRatherThanEveryFrame()
    {
        RecordingLogger log = new();
        ModelReports reports = new(log);

        for (int frame = 0; frame < 30; frame++)
        {
            reports.BrushMoved(Prop(entity: 80, kind: SceneModelKind.Brush, z: 640f), 0d);
        }

        log.Count("brush").ShouldBe(1);
    }

    [Test]
    public void BrushMoved_ADoorThatRises_IsReportedAtEachPosition()
    {
        // **Every movement, not the first sighting.** Reporting once per entity was enough to find
        // where the gates are and useless for finding out what one DOES: a shutter that sinks below
        // its frame does so over a handful of frames, and the single line already written came from
        // long before. This is what lets the trace be read against the demo's own keyframes.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        foreach (float z in new[] { 640f, 660f, 700f, 785f })
        {
            reports.BrushMoved(Prop(entity: 80, kind: SceneModelKind.Brush, z: z), 0d);
        }

        log.Count("brush").ShouldBe(4);
    }

    [Test]
    public void BrushMoved_AShiftSmallerThanAUnit_IsNotReported()
    {
        // The threshold, and its control is the test above. Without it a door that jitters by a
        // thousandth of a unit prints every frame, which is the B163 shape again.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        reports.BrushMoved(Prop(entity: 80, kind: SceneModelKind.Brush, z: 640f), 0d);
        reports.BrushMoved(Prop(entity: 80, kind: SceneModelKind.Brush, z: 640.4f), 0d);

        log.Count("brush").ShouldBe(1);
    }

    [Test]
    public void BrushMoved_AStudioModel_IsNotReportedAtAll()
    {
        // The control for the whole family: this line is about brush entities, and a version that
        // reported every prop would satisfy every assertion above.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        reports.BrushMoved(Prop(entity: 5, kind: SceneModelKind.Studio, z: 640f), 0d);

        log.Count("brush").ShouldBe(0);
    }

    [Test]
    public void Animating_TheSameModelOnTwoEntities_IsReportedOnce()
    {
        // Deduped per MODEL here, unlike Lit — because which baked frame a sequence selects is a
        // fact about the model, and repeating it per entity would print it for all eleven ammo
        // packs on the map.
        RecordingLogger log = new();
        ModelReports reports = new(log);

        reports.Animating(Prop(entity: 1), frame: 0, frames: 4, blend: 0f);
        reports.Animating(Prop(entity: 2), frame: 0, frames: 4, blend: 0f);

        log.Count("animating").ShouldBe(1);
    }

    [Test]
    public void FirstTime_TheSameKeyTwice_IsTrueThenFalse()
    {
        ModelReports reports = new(new RecordingLogger());

        reports.FirstTime("models/x.mdl#worn").ShouldBeTrue();
        reports.FirstTime("models/x.mdl#worn").ShouldBeFalse();

        // The control: a different suffix is a different report, which is the only thing that
        // distinguishes them.
        reports.FirstTime("models/x.mdl#skin").ShouldBeTrue();
    }

    private static ModelLight Light() => new(default(AmbientCube), null, 0f, 0f, 0f, []);

    private static SceneProp Prop(
        int entity, SceneModelKind kind = SceneModelKind.Studio, float z = 0f) =>
        new(entity, "models/props/crate.mdl", kind, new ScenePose { Z = z }, null);
}
