using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>An <c>env_sun</c> reaches the timeline from a real demo's wire (B378).</summary>
/// <remarks>
/// **The synthetic suite proves the bookkeeping; only a real file proves the production decode reaches it.**
/// koth_lakeside_final ships one <c>env_sun</c> — <c>rendercolor "228 202 186"</c>, <c>size 32</c>, <c>overlaysize -1</c>,
/// both materials <c>sprites/light_glow02_add_noz</c>, <c>use_angles 1</c> at pitch -35 and yaw 55 — and this short
/// recording of it sends the sun on entering. Of 242 shipped maps, eight carry an <c>env_sun</c> (measured, 2026-10-14);
/// this is the only recording of one in the corpus.
/// </remarks>
public sealed class SunCorpusTests
{
    [Test]
    public void SunsAt_Lakeside_IsTheMapsSunWithItsMaterialsNamed()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("tf2-2026-pov-koth_lakeside-triggers"));

        SceneSun sun = timeline.Suns.At(int.MaxValue).ShouldHaveSingleItem();

        sun.On.ShouldBeTrue();
        sun.Render.ShouldBe(((byte)228, (byte)202, (byte)186), "rendercolor, red from the high byte");
        sun.Size.ShouldBe(32);
        sun.OverlaySize.ShouldBe(32, "the server's -1 becomes the core's size");
        sun.Material.ShouldNotBeNull().ShouldEndWith("light_glow02_add_noz.vmt", Case.Insensitive);
        sun.OverlayMaterial.ShouldBe(sun.Material);

        // `SetupLightNormalFromProps` at pitch -35, yaw 55, then negated: up 0.574, and toward -X -Y.
        sun.Direction.Z.ShouldBe(0.574f, 0.001f);
        sun.Direction.X.ShouldBeLessThan(0f);
        sun.Direction.Y.ShouldBeLessThan(0f);
    }
}
