using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>EntityState.Fog</c> — reading a fog controller's networked atmosphere.
/// </summary>
/// <remarks>
/// **Written to isolate a decode that produced nothing on nine real demos.** The properties were
/// demonstrably arriving — a trace of the 2011 koth_viaduct recording shows
/// <c>DT_FogController.m_fog.enable 1</c>, <c>m_fog.end 6500</c> and the rest — and the timeline
/// still recorded zero samples. Everything between those two facts is this method, so this is where
/// the question gets asked with values chosen rather than decoded.
///
/// The property names are struct PATHS, which is what <c>SENDINFO_STRUCTELEM( m_fog.start )</c>
/// produces, so the qualified key is <c>DT_FogController.m_fog.start</c> — a table name and a
/// property name that itself contains a dot.
/// </remarks>
public sealed class EntityFogTests
{
    private const string Table = "DT_FogController";

    [Test]
    public void Fog_AControllerWithEverythingSet_IsRead()
    {
        // The 2011 koth_viaduct values, taken from a decoded trace rather than invented: fog from 0
        // to 6500 units, colour 14528213 packed, density 1.
        EntityState state = Controller(enable: 1, start: 0f, end: 6500f, colour: 14528213, density: 1f);

        SceneFog? fog = state.Fog();

        fog.ShouldNotBeNull("a controller sending enable, start, end and colour describes fog");
        fog.Value.Start.ShouldBe(0f);
        fog.Value.End.ShouldBe(6500f);
        fog.Value.MaxDensity.ShouldBe(1f);

        // **14528213 is 0xDDAED5**, and the packing is what this asserts: color32 is red in the low
        // byte, then green, then blue. Reading it the other way round gives a plausible colour that
        // is simply the wrong one, which is why the exact bytes are predicted rather than a range.
        //
        // 221·65536 + 174·256 + 213 = 14528213, which is the arithmetic that settles the byte order
        // without appealing to the picture.
        fog.Value.Red.ShouldBe(0xD5 / 255f, 1e-6f);
        fog.Value.Green.ShouldBe(0xAE / 255f, 1e-6f);
        fog.Value.Blue.ShouldBe(0xDD / 255f, 1e-6f);
    }

    [Test]
    public void Fog_AControllerWithFogDisabled_IsNotRead()
    {
        // **A map with a controller and fog switched off is a real case**, not a missing one. It
        // arrives as null and draws the same as no controller at all, which is correct: the
        // alternative is inventing weather.
        Controller(enable: 0, start: 0f, end: 6500f, colour: 14528213, density: 1f)
            .Fog()
            .ShouldBeNull();
    }

    [Test]
    public void Fog_AnEntityThatIsNotAController_IsNotRead()
    {
        // The control. Every entity in a demo is asked this question, so a method that answered for
        // any of them would report fog from a rocket.
        new EntityState(1, 0, 0, "CTFPlayer").Fog().ShouldBeNull();
    }

    [Test]
    public void Fog_ARangeThatIsNotPositive_IsNotRead()
    {
        // The shader divides by `end - start`. A degenerate range reaching the GPU is a screen of
        // NaN, so it is refused here where it can be seen.
        Controller(enable: 1, start: 6500f, end: 6500f, colour: 1, density: 1f).Fog().ShouldBeNull();
        Controller(enable: 1, start: 9000f, end: 6500f, colour: 1, density: 1f).Fog().ShouldBeNull();
    }

    [Test]
    public void Fog_AnAbsentMaxDensity_MeansNoCapRatherThanNoFog()
    {
        // **The inverted default.** maxdensity CAPS the fog, so a controller that does not send one
        // wants no cap — and defaulting to zero would switch fog off while reporting it on.
        EntityState state = new(1, 0, 0, "CFogController");

        state.Set($"{Table}.m_fog.enable", PropertyValue.FromInt(1));
        state.Set($"{Table}.m_fog.start", PropertyValue.FromFloat(0f));
        state.Set($"{Table}.m_fog.end", PropertyValue.FromFloat(1000f));
        state.Set($"{Table}.m_fog.colorPrimary", PropertyValue.FromInt(0xFFFFFF));

        // **Asserted through a non-null check first**, because `state.Fog()?.MaxDensity.ShouldBe(1f)`
        // passes when Fog() returns null — a test that cannot fail in exactly the case it is about.
        // Written that way here for a moment and caught only because the suite's other rows made
        // the vacuity obvious.
        SceneFog? fog = state.Fog();

        fog.ShouldNotBeNull();
        fog.Value.MaxDensity.ShouldBe(1f);
    }

    private static EntityState Controller(int enable, float start, float end, int colour, float density)
    {
        EntityState state = new(1, 0, 0, "CFogController");

        state.Set($"{Table}.m_fog.enable", PropertyValue.FromInt(enable));
        state.Set($"{Table}.m_fog.start", PropertyValue.FromFloat(start));
        state.Set($"{Table}.m_fog.end", PropertyValue.FromFloat(end));
        state.Set($"{Table}.m_fog.colorPrimary", PropertyValue.FromInt(colour));
        state.Set($"{Table}.m_fog.maxdensity", PropertyValue.FromFloat(density));

        return state;
    }
}
