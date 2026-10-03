using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>ViewFog.From</c> — which fog the view draws through, chosen as the client chooses it.
/// </summary>
/// <remarks>
/// <c>C_BasePlayer::UpdateFogController</c> (c_baseplayer.cpp:2802) copies the fog of the controller
/// the local player's <c>m_PlayerFog.m_hCtrl</c> names (DT_Local, c_baseplayer.cpp:201), and with no
/// controller sets <c>m_CurrentFog.enable = false</c>. The 3D skybox's fog is the same player's
/// <c>m_skybox3d.fog</c> (viewrender.cpp:4799). This replaced "the first enabled controller in the
/// entity list", which a map with two controllers — or a player assigned none — answers wrongly.
/// </remarks>
public sealed class ViewFogTests
{
    private const int EdictBits = 11;

    [Test]
    public void From_ThePlayersHandleNamesTheSecondController_TakesThatOne()
    {
        EntityStateTable table = new(EntityBaselines.None);

        Controller(table, 100, serial: 1, start: 0f, end: 1000f);
        Controller(table, 194, serial: 3, start: 100f, end: 11000f);
        Player(table, 2, Handle(194, 3));

        ViewFog.From(table).World.ShouldBe(new SceneFog(100f, 11000f, 1f, 1f, 1f, 1f));
    }

    [Test]
    public void From_NoEntityCarriesAHandle_IsNoFog()
    {
        // **No handle, no fog, however many controllers the map has** — `UpdateFogController` sets
        // `m_CurrentFog.enable = false`. This once fell back to the first controller, standing in
        // for a handle point-of-view demos seemed not to carry; they carry it in `dem_stringtables`'
        // player baseline (B452), so the stand-in went.
        EntityStateTable table = new(EntityBaselines.None);

        Controller(table, 100, serial: 1, start: 0f, end: 1000f);
        Controller(table, 194, serial: 3, start: 100f, end: 11000f);

        ViewFog.From(table).ShouldBe(default);
    }

    [Test]
    public void From_AHandleWhoseSlotChangedHands_IsNoFog()
    {
        EntityStateTable table = new(EntityBaselines.None);

        Controller(table, 194, serial: 3, start: 100f, end: 11000f);
        Player(table, 2, Handle(194, 4));

        ViewFog.From(table).World.ShouldBeNull();
    }

    [Test]
    public void From_ThePlayersSkyboxFog_IsTheSkyFog()
    {
        EntityStateTable table = new(EntityBaselines.None);
        EntityState player = Player(table, 2, Handle(194, 3));

        player.Set("DT_Local.m_skybox3d.fog.enable", PropertyValue.FromInt(1));
        player.Set("DT_Local.m_skybox3d.fog.start", PropertyValue.FromFloat(500f));
        player.Set("DT_Local.m_skybox3d.fog.end", PropertyValue.FromFloat(2000f));
        player.Set("DT_Local.m_skybox3d.fog.colorPrimary", PropertyValue.FromInt(0x0000FF));
        player.Set("DT_Local.m_skybox3d.fog.maxdensity", PropertyValue.FromFloat(0.5f));

        ViewFog.From(table).Sky.ShouldBe(new SceneFog(500f, 2000f, 1f, 0f, 0f, 0.5f));
    }

    [Test]
    public void From_SkyboxFogSwitchedOff_IsNoSkyFog()
    {
        // Both SourceTV movement-test recordings carry exactly this: enable 0, start 500, end 2000.
        EntityStateTable table = new(EntityBaselines.None);
        EntityState player = Player(table, 2, Handle(194, 3));

        player.Set("DT_Local.m_skybox3d.fog.enable", PropertyValue.FromInt(0));
        player.Set("DT_Local.m_skybox3d.fog.start", PropertyValue.FromFloat(500f));
        player.Set("DT_Local.m_skybox3d.fog.end", PropertyValue.FromFloat(2000f));
        player.Set("DT_Local.m_skybox3d.fog.colorPrimary", PropertyValue.FromInt(0xFFFFFF));

        ViewFog.From(table).Sky.ShouldBeNull();
    }

    private static int Handle(int slot, int serial) => slot | (serial << EdictBits);

    private const int ControllerClass = 48;

    private static EntityState Enter(EntityStateTable table, int index, int serial, int classId = 0)
    {
        table.SetClassName(ControllerClass, "CFogController");
        table.Apply(new DecodedEntity(index, ClassId: classId, SerialNumber: serial, EntityUpdateType.Enter, []));
        table.TryGet(index, out EntityState? state).ShouldBeTrue();

        return state;
    }

    private static void Controller(EntityStateTable table, int index, int serial, float start, float end)
    {
        EntityState state = Enter(table, index, serial, ControllerClass);

        state.Set("DT_FogController.m_fog.enable", PropertyValue.FromInt(1));
        state.Set("DT_FogController.m_fog.start", PropertyValue.FromFloat(start));
        state.Set("DT_FogController.m_fog.end", PropertyValue.FromFloat(end));
        state.Set("DT_FogController.m_fog.colorPrimary", PropertyValue.FromInt(0xFFFFFF));
        state.Set("DT_FogController.m_fog.maxdensity", PropertyValue.FromFloat(1f));
    }

    private static EntityState Player(EntityStateTable table, int index, int handle)
    {
        EntityState state = Enter(table, index, serial: 1);

        state.Set("DT_Local.m_PlayerFog.m_hCtrl", PropertyValue.FromInt(handle));

        return state;
    }
}
