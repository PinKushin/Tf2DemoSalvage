using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a model loaded the way the viewer loads one knows whether it is two-pass.
/// </summary>
/// <remarks>
/// **The component tests cannot catch what this catches**, which is the same argument
/// <see cref="ModelBoundsWiringTests"/> makes for the render bounds.
/// `StudioHeaderFlagsConformanceTests` reads the flag out of a real `.mdl` and passes whether or not
/// the loader ever calls it; `TwoPassConformanceTests` decides from a boolean and passes whether or
/// not that boolean ever came from a file. Between them is the loader, and a loader that dropped the
/// flag would be silent: every model would draw in one pass, which is the right answer for 99.4% of
/// them.
///
/// **That number is why the control matters more than the subject here.** Measured over TF2's own
/// archives by `StudioModelFlagCensus`: **88 of 14,109 shipped models carry
/// `STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS`, 0.62%.** So a test that only checked a two-pass model
/// would be checking the rare case, and an implementation that returned `false` for everything would
/// be right about the other 14,021.
///
/// **Sniper and Scout are the pair, and they are as alike as two subjects get**: both TF2 player
/// models, same directory, same compiler, same era. `models/player/sniper.mdl` declares
/// `$mostlyopaque` and `models/player/scout.mdl` does not — a difference in the file rather than in
/// how the test treats them.
/// </remarks>
public sealed class TwoPassWiringTests
{
    private const string Sniper = "models/player/sniper.mdl";
    private const string Scout = "models/player/scout.mdl";

    /// <summary>That the loader carries the header's flag onto the model it builds.</summary>
    [Test]
    public void Geometry_TheSniperAsTheViewerLoadsIt_KnowsItIsTwoPass()
    {
        MapAssets assets = MapCache.Load(entityModels: [Sniper, Scout]);

        PropModels.ModelFrames sniper =
            assets.Geometry(Sniper).ShouldNotBeNull("the sniper should have loaded");

        PropModels.ModelFrames scout =
            assets.Geometry(Scout).ShouldNotBeNull("the scout should have loaded");

        sniper.TwoPass.ShouldBeTrue(
            "models/player/sniper.mdl declares $mostlyopaque — see StudioModelFlagCensus");

        // **The control, and the one that would catch the likelier mistake.** `TwoPass` defaults to
        // false, so a loader that never set it would pass the assertion above only by accident and
        // this one always. A loader that set it unconditionally fails here.
        scout.TwoPass.ShouldBeFalse("models/player/scout.mdl declares no such flag");
    }
}
