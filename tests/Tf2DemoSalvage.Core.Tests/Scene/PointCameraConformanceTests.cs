using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>C_PointCamera</c> and the camera <c>CViewRender::DrawMonitors</c> renders into <c>_rt_Camera</c> (B511).</summary>
/// <remarks>
/// **The receive table, `c_point_camera.cpp:18-29`**: FOV, resolution, the fog enable, colour (a <c>color32</c>, RGBA low
/// byte up), start, end, max density and radial flag, <c>m_bActive</c> and <c>m_bUseScreenAspectRatio</c>. The values are
/// <c>koth_boardwalk</c>'s <c>mirror_camera</c> as <c>b511_boardwalk_mirror.dem</c> sends it (`entity-census`): FOV 45,
/// fog on, black, 192 to 1024, max density 1, active, at (-1168 900 78) facing yaw 270.
/// <c>DrawMonitors</c> (`viewrender.cpp:3240-3287`) skips <c>!IsActive() || IsDormant()</c> and draws every other camera
/// into the one target, in list order; <c>C_EntityClassList::Insert</c> prepends (`cliententitylist.h:54-58`), so the
/// earliest-constructed active camera is drawn last and is the one left in the target.
/// </remarks>
public sealed class PointCameraConformanceTests
{
    private const string Camera = "DT_PointCamera";

    private static EntityState Mirror(int index = 747, bool active = true, bool fog = true)
    {
        EntityState camera = new(index, 0, 0, PointCameraFeed.ClassName);

        camera.Set("DT_BaseEntity.m_vecOrigin", PropertyValue.FromVector(-1168f, 900f, 78f));
        camera.Set("DT_BaseEntity.m_angRotation", PropertyValue.FromVector(0f, 270f, 0f));
        camera.Set($"{Camera}.m_FOV", PropertyValue.FromFloat(45f));
        camera.Set($"{Camera}.m_bFogEnable", PropertyValue.FromInt(fog ? 1 : 0));
        camera.Set($"{Camera}.m_FogColor", PropertyValue.FromInt(0x00402010));
        camera.Set($"{Camera}.m_flFogStart", PropertyValue.FromFloat(192f));
        camera.Set($"{Camera}.m_flFogEnd", PropertyValue.FromFloat(1024f));
        camera.Set($"{Camera}.m_flFogMaxDensity", PropertyValue.FromFloat(1f));
        camera.Set($"{Camera}.m_bFogRadial", PropertyValue.FromInt(0));
        camera.Set($"{Camera}.m_bActive", PropertyValue.FromInt(active ? 1 : 0));
        camera.Set($"{Camera}.m_bUseScreenAspectRatio", PropertyValue.FromInt(0));

        return camera;
    }

    /// <remarks>**Every field <c>DrawOneMonitor</c> reads** (`viewrender.cpp:3168-3238`), the fog colour unpacked as a fog controller's.</remarks>
    [Test]
    public void PointCamera_ForBoardwalksMirrorCamera_ReadsItsOwnTable()
    {
        Mirror().PointCamera().ShouldBe(new ScenePointCamera(
            747, (-1168f, 900f, 78f), (0f, 270f, 0f), 45f, UseScreenAspectRatio: false,
            new SceneFog(192f, 1024f, 0x10 / 255f, 0x20 / 255f, 0x40 / 255f, 1f)));
    }

    /// <remarks>**Fog off means the view's own fog** — <c>DrawOneMonitor</c> touches the fog params only when enabled.</remarks>
    [Test]
    public void PointCamera_WithFogOff_CarriesNoFog()
    {
        Mirror(fog: false).PointCamera().ShouldNotBeNull().Fog.ShouldBeNull();
    }

    /// <remarks><c>!IsActive()</c> is skipped; and anything that is not a camera is not one.</remarks>
    [Test]
    public void PointCamera_InactiveOrNotACamera_IsNull()
    {
        Mirror(active: false).PointCamera().ShouldBeNull();
        new EntityState(40, 0, 0, "CBaseDoor").PointCamera().ShouldBeNull();
    }

    /// <remarks>The earliest-constructed active, non-dormant camera is the one left in the target.</remarks>
    [Test]
    public void Choose_SeveralCameras_IsTheOldestActiveNonDormantOne()
    {
        Dictionary<int, EntityState> held = new()
        {
            [10] = Mirror(10),
            [11] = Mirror(11, active: false),
            [12] = Mirror(12),
            [13] = Mirror(13),
        };

        PointCameraFeed.Choose([11, 13, 12], i => held.GetValueOrDefault(i), _ => false)!.Value.EntityIndex.ShouldBe(13);
        PointCameraFeed.Choose([11, 13, 12], i => held.GetValueOrDefault(i), i => i == 13)!.Value.EntityIndex.ShouldBe(12);
        PointCameraFeed.Choose([11], i => held.GetValueOrDefault(i), _ => false).ShouldBeNull();
    }

    /// <remarks>
    /// **Construction order is creation, not PVS entry**: a dormant camera re-entering is the same client entity; a
    /// delete then an enter is a new one, at the head of the list.
    /// </remarks>
    [Test]
    public void Sample_AReEnterKeepsOrderAndADeleteRequeues_RecordsOnChange()
    {
        Dictionary<int, EntityState> held = new() { [10] = Mirror(10), [12] = Mirror(12) };
        PointCameraFeed feed = new();

        feed.Observe(10, PointCameraFeed.ClassName, EntityUpdateType.Enter);
        feed.Observe(12, PointCameraFeed.ClassName, EntityUpdateType.Enter);
        feed.Sample(100, i => held.GetValueOrDefault(i), _ => false);
        feed.Observe(10, PointCameraFeed.ClassName, EntityUpdateType.Enter);
        feed.Sample(101, i => held.GetValueOrDefault(i), _ => false);
        feed.Observe(10, PointCameraFeed.ClassName, EntityUpdateType.Delete);
        feed.Observe(10, PointCameraFeed.ClassName, EntityUpdateType.Enter);
        feed.Sample(102, i => held.GetValueOrDefault(i), _ => false);

        feed.Samples.Count.ShouldBe(2);
        feed.At(101)!.Value.EntityIndex.ShouldBe(10);
        feed.At(102)!.Value.EntityIndex.ShouldBe(12);
        feed.At(99).ShouldBeNull();
    }
}
