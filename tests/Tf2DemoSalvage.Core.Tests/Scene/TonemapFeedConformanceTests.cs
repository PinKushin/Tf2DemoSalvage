using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>C_EnvTonemapController</c> and the <c>viewpostprocess.cpp</c> globals it writes (B514).</summary>
/// <remarks>
/// **`c_env_tonemap_controller.cpp`**: the receive table (`:43-51`) is three flags and four floats;
/// <c>OnDataChanged</c> (`:83-96`) copies them all into the globals and claims <c>g_hTonemapControllerInUse</c>; the
/// destructor (`:70-78`) of the controller in use clears the three FLAGS and leaves the values. The values are
/// cp_process_f12's: its <c>logic_auto</c> sets 0.5 / 0.7 / bloom 0.5, and each round restart deletes that controller
/// and creates a fresh one, which the <c>logic_auto</c> configures thirteen ticks later (`autoexposure` probe on f12:
/// ticks 41673 → 41686).
/// </remarks>
public sealed class TonemapFeedConformanceTests
{
    private static EntityState Controller(int index, bool use, float min, float max, float bloom)
    {
        EntityState controller = new(index, 0, 0, TonemapFeed.ClassName);
        string table = TonemapFeed.Table;

        controller.Set($"{table}.m_bUseCustomAutoExposureMin", PropertyValue.FromInt(use ? 1 : 0));
        controller.Set($"{table}.m_bUseCustomAutoExposureMax", PropertyValue.FromInt(use ? 1 : 0));
        controller.Set($"{table}.m_bUseCustomBloomScale", PropertyValue.FromInt(use ? 1 : 0));
        controller.Set($"{table}.m_flCustomAutoExposureMin", PropertyValue.FromFloat(min));
        controller.Set($"{table}.m_flCustomAutoExposureMax", PropertyValue.FromFloat(max));
        controller.Set($"{table}.m_flCustomBloomScale", PropertyValue.FromFloat(bloom));

        return controller;
    }

    [Test]
    public void Observe_AConfiguredController_WritesAllSixGlobals()
    {
        TonemapFeed feed = new();

        feed.Observe(195, Controller(195, use: true, 0.5f, 0.7f, 0.5f), EntityUpdateType.Enter);
        feed.Sample(1);

        feed.At(1).ShouldBe(new SceneTonemap(true, 0.5f, true, 0.7f, true, 0.5f));
        feed.At(0).ShouldBe(default);
    }

    /// <remarks>
    /// **The round restart**: deleting the controller in use clears the flags (the destructor), the new one entering with
    /// zeros writes zeros (<c>OnDataChanged</c>), and the <c>logic_auto</c>'s inputs then restore the map's values.
    /// </remarks>
    [Test]
    public void Observe_ARoundRestart_HandsTheRangeBackToTheCvarsUntilReconfigured()
    {
        TonemapFeed feed = new();

        feed.Observe(195, Controller(195, use: true, 0.5f, 0.7f, 0.5f), EntityUpdateType.Enter);
        feed.Sample(1);
        feed.Observe(195, null, EntityUpdateType.Delete);
        feed.Sample(41672);
        feed.Observe(201, Controller(201, use: false, 0f, 0f, 0f), EntityUpdateType.Enter);
        feed.Sample(41673);
        feed.Observe(201, Controller(201, use: true, 0.5f, 0.7f, 0.5f), EntityUpdateType.Delta);
        feed.Sample(41686);

        feed.At(41672).ShouldBe(new SceneTonemap(false, 0.5f, false, 0.7f, false, 0.5f));
        feed.At(41673).ShouldBe(default);
        feed.At(41686).ShouldBe(new SceneTonemap(true, 0.5f, true, 0.7f, true, 0.5f));
        feed.Samples.Count.ShouldBe(4);
    }

    /// <remarks>Only the controller IN USE clears the flags when deleted; a leave is not a destruction.</remarks>
    [Test]
    public void Observe_ADeleteOfAnotherOrALeave_ChangesNothing()
    {
        TonemapFeed feed = new();

        feed.Observe(195, Controller(195, use: true, 0.5f, 0.7f, 0.5f), EntityUpdateType.Enter);
        feed.Observe(300, null, EntityUpdateType.Delete);
        feed.Observe(195, null, EntityUpdateType.Leave);
        feed.Observe(40, new EntityState(40, 0, 0, "CBaseDoor"), EntityUpdateType.Enter);
        feed.Sample(5);

        feed.At(5).ShouldBe(new SceneTonemap(true, 0.5f, true, 0.7f, true, 0.5f));
    }
}
