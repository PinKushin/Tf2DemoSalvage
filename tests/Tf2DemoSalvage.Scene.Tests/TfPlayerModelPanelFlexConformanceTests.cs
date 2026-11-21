using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFPlayerModelPanel::SetupFlexWeights` (tf_playermodelpanel.cpp:2275-2361): the flex controls its root's draw runs the
/// rules on, outside a scene and with no voice playing (B725 5f).
/// </summary>
/// <remarks>
/// **Read** (published source): the panel's `m_flexWeight[ 96 ]` is zeroed in the constructor (:129) and never reset. Each
/// paint, every controller decays, `SetFlexWeight( i, GetFlexWeight( i ) * 0.95 )` (:2300-2303): `GetFlexWeight` rescales the
/// stored 0..1 weight to the range where `max != min`, else returns it raw (:2254-2270); `SetFlexWeight` normalises back and
/// clamps to 0..1 where `max != min` (:2233-2249). Then `m_pFlexControls[ localToGlobal ] = w · (max − min) + min` (:2313-2320).
/// **Read** (TF2 x64 `client.dll`, disassembled): `RunFlexRules`' FETCH1 reads `src[ controller[ index ].localToGlobal ]`
/// (`0x1803177ea-0x18031781f`), as `CStudioHdr::RunFlexRules` does in the SDK (`studio.cpp:1481-1489`). So each rule reads
/// its own controller's value whatever global number it was given — the mapping is the model's, not the session's — except
/// that two controllers of one name share one global slot (`AddGlobalFlexController`'s `stricmp`, `c_baseflex.cpp:1341-1361`)
/// and the later one written, the higher local index, is what both read.
/// </remarks>
public sealed class TfPlayerModelPanelFlexConformanceTests
{
    /// <remarks>A −1..1 controller at the constructor's 0 reads −1; one paint: −0.95 (weight 0.025); two: −0.9025.</remarks>
    [TestCase(1, -0.95f, 0.025f)]
    [TestCase(2, -0.9025f, 0.04875f)]
    public void StepFlexControls_ASymmetricControllerFromTheConstructor_DecaysTowardZero(int paints, float control, float weight)
    {
        float[] flexWeight = new float[TfPlayerModelPanel.MaxStudioFlexCtrl];
        StudioFlexController[] controllers = [new("default", "lip", -1f, 1f)];
        float[] source = [];

        for (int paint = 0; paint < paints; paint++)
        {
            source = Step(flexWeight, controllers);
        }

        source[0].ShouldBe(control, 1e-6f);
        flexWeight[0].ShouldBe(weight, 1e-6f);
    }

    /// <remarks>
    /// **A range above zero stays at its minimum**: 0.5 · 0.95 normalises to −0.05, clamped to 0, so the control is 0.5.
    /// **A degenerate range is the raw weight**: 2..2 reads 0, stores 0, and the control is `0 · 0 + 2` = 2.
    /// </remarks>
    [TestCase(0.5f, 1f, 0.5f)]
    [TestCase(2f, 2f, 2f)]
    public void StepFlexControls_ARangeNotSpanningZero_HoldsWhereTheClampLeavesIt(float min, float max, float control)
    {
        float[] flexWeight = new float[TfPlayerModelPanel.MaxStudioFlexCtrl];
        StudioFlexController[] controllers = [new("default", "jaw", min, max)];

        Step(flexWeight, controllers);
        float[] source = Step(flexWeight, controllers);

        source[0].ShouldBe(control, 1e-6f);
    }

    /// <remarks>
    /// **Two controllers of one name share a global slot**: `jaw` 0..1 (index 0, control 0 after a paint) and `JAW` −1..1
    /// (index 1, −0.95): index 1 is written last, so a rule fetching either reads −0.95.
    /// </remarks>
    [Test]
    public void StepFlexControls_TwoControllersOfOneName_BothReadTheLaterOnesValue()
    {
        float[] flexWeight = new float[TfPlayerModelPanel.MaxStudioFlexCtrl];
        StudioFlexController[] controllers = [new("default", "jaw", 0f, 1f), new("default", "JAW", -1f, 1f)];

        float[] source = Step(flexWeight, controllers);

        source[0].ShouldBe(-0.95f, 1e-6f);
        source[1].ShouldBe(-0.95f, 1e-6f);
    }

    /// <remarks>
    /// **A paint's flex step allocates nothing once its buffers exist** (review 1 item 4): the decay is in place, the shared
    /// slots are computed once per model, and the controls are written into a kept buffer. The thread's allocation counter
    /// also counts the runtime, so the least over several steps is taken (memory: a thread allocation counter counts the
    /// runtime).
    /// </remarks>
    [Test]
    public void StepFlexControls_AfterTheFirstStep_AllocatesNothing()
    {
        float[] flexWeight = new float[TfPlayerModelPanel.MaxStudioFlexCtrl];
        StudioFlexController[] controllers = [new("default", "jaw", 0f, 1f), new("default", "lip", -1f, 1f), new("default", "JAW", -1f, 1f)];
        int[] slots = TfPlayerModelPanel.SharedSlots(controllers);
        float[] source = new float[controllers.Length];
        long least = long.MaxValue;

        for (int run = 0; run < 8; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            TfPlayerModelPanel.DecayFlexWeights(flexWeight, controllers);
            TfPlayerModelPanel.FlexControls(flexWeight, controllers, slots, source);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
    }

    private static float[] Step(float[] flexWeight, StudioFlexController[] controllers)
    {
        float[] source = new float[controllers.Length];

        TfPlayerModelPanel.DecayFlexWeights(flexWeight, controllers);
        TfPlayerModelPanel.FlexControls(flexWeight, controllers, TfPlayerModelPanel.SharedSlots(controllers), source);
        return source;
    }
}
