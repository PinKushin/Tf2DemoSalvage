using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>C_Sun</c>'s receive table and what it keeps (B378).</summary>
/// <remarks>
/// **`c_sun.cpp:21-33`**: <c>m_clrRender</c> and <c>m_clrOverlay</c> through <c>RecvProxy_IntToColor32</c>,
/// <c>m_vDirection</c>, <c>m_bOn</c>, the two sizes, the two material indices, and <c>HDRColorScale</c>. The values are
/// koth_lakeside_final's sun as `entity-census` decoded it from `tf2-2026-pov-koth_lakeside-triggers.dem`:
/// <c>m_clrRender = 3838491391</c>, direction (-0.469, -0.671, 0.574), sizes 32 and 32, both materials index 1094.
/// </remarks>
public sealed class SunFeedConformanceTests
{
    private static readonly Dictionary<int, string> Precache = new() { [1094] = "sprites/light_glow02_add_noz.vmt" };

    private static EntityState Lakeside(int index = 189, bool on = true)
    {
        EntityState sun = new(index, 0, 0, SunFeed.ClassName);
        string table = SunFeed.Table;

        sun.Set($"{table}.m_clrRender", PropertyValue.FromInt(3838491391));
        sun.Set($"{table}.m_clrOverlay", PropertyValue.FromInt(0));
        sun.Set($"{table}.m_vDirection", PropertyValue.FromVector(-0.469f, -0.671f, 0.574f));
        sun.Set($"{table}.m_bOn", PropertyValue.FromInt(on ? 1 : 0));
        sun.Set($"{table}.m_nSize", PropertyValue.FromInt(32));
        sun.Set($"{table}.m_nOverlaySize", PropertyValue.FromInt(32));
        sun.Set($"{table}.m_nMaterial", PropertyValue.FromInt(1094));
        sun.Set($"{table}.m_nOverlayMaterial", PropertyValue.FromInt(1094));
        sun.Set($"{table}.HDRColorScale", PropertyValue.FromFloat(1f));

        return sun;
    }

    /// <remarks>
    /// `RecvProxy_IntToColor32` (`recvproxy.cpp:20-23`): <c>r = inColor &gt;&gt; 24</c>, <c>g = (inColor &gt;&gt; 16) &amp; 0xFF</c>,
    /// <c>b = (inColor &gt;&gt; 8) &amp; 0xFF</c>. 0xE4CABAFF is the map's <c>rendercolor "228 202 186"</c> at alpha 255.
    /// </remarks>
    [Test]
    public void Color32_LakesidesRenderColor_IsRedFromTheHighByte() =>
        SunFeed.Color32(unchecked((int)3838491391u)).ShouldBe(((byte)228, (byte)202, (byte)186));

    [Test]
    public void Observe_LakesidesSun_IsEveryFieldWithItsMaterialsNamed()
    {
        SunFeed feed = new();

        feed.Observe(189, Lakeside(), EntityUpdateType.Enter);
        feed.Sample(5, index => Precache.GetValueOrDefault(index));

        SceneSun sun = feed.At(5).ShouldHaveSingleItem();

        sun.Entity.ShouldBe(189);
        sun.Direction.ShouldBe((-0.469f, -0.671f, 0.574f));
        sun.On.ShouldBeTrue();
        sun.Size.ShouldBe(32);
        sun.OverlaySize.ShouldBe(32);
        sun.Render.ShouldBe(((byte)228, (byte)202, (byte)186));
        sun.Overlay.ShouldBe(((byte)0, (byte)0, (byte)0));
        sun.Material.ShouldBe("sprites/light_glow02_add_noz.vmt");
        sun.OverlayMaterial.ShouldBe("sprites/light_glow02_add_noz.vmt");
        sun.HdrColorScale.ShouldBe(1f);
        feed.At(4).ShouldBeEmpty();
    }

    /// <remarks><c>InputTurnOff</c> clears <c>m_bOn</c> (`sun.cpp:185-191`); the client deactivates both overlays.</remarks>
    [Test]
    public void Observe_TurnedOffThenDeleted_IsOffThenGone()
    {
        SunFeed feed = new();

        feed.Observe(189, Lakeside(), EntityUpdateType.Enter);
        feed.Sample(1, index => Precache.GetValueOrDefault(index));
        feed.Observe(189, Lakeside(on: false), EntityUpdateType.Delta);
        feed.Sample(10, index => Precache.GetValueOrDefault(index));
        feed.Observe(189, null, EntityUpdateType.Delete);
        feed.Sample(20, index => Precache.GetValueOrDefault(index));

        feed.At(10).ShouldHaveSingleItem().On.ShouldBeFalse();
        feed.At(20).ShouldBeEmpty();
        feed.Samples.Count.ShouldBe(3);
    }

    [Test]
    public void Observe_AnUnchangedResendOrAnotherClass_AddsNoSample()
    {
        SunFeed feed = new();

        feed.Observe(189, Lakeside(), EntityUpdateType.Enter);
        feed.Sample(1, index => Precache.GetValueOrDefault(index));
        feed.Observe(189, Lakeside(), EntityUpdateType.Delta);
        feed.Observe(40, new EntityState(40, 0, 0, "CBaseDoor"), EntityUpdateType.Enter);
        feed.Observe(189, null, EntityUpdateType.Leave);
        feed.Sample(2, index => Precache.GetValueOrDefault(index));

        feed.Samples.Count.ShouldBe(1);
    }
}
