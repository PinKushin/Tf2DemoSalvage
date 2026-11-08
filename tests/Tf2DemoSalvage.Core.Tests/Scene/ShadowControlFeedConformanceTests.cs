using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>C_ShadowControl</c> and the client shadow manager's globals it writes (B702, finding 107).</summary>
/// <remarks>
/// **`c_shadowcontrol.cpp`**: the receive table (`:37-42`) is a vector, a 32-bit colour, a float and a bool;
/// <c>OnDataChanged</c> (`:50-57`) hands all four to <c>g_pClientShadowMgr</c>; there is no destructor, so nothing undoes
/// them. The server's defaults are direction (0.2, 0.2, −2), distance 50 and colour (64, 64, 64, 0)
/// (`shadowcontrol.cpp:71-77`).
/// </remarks>
public sealed class ShadowControlFeedConformanceTests
{
    private static EntityState Controller(int index, (float, float, float) direction, long colour, float distance, bool disabled)
    {
        EntityState controller = new(index, 0, 0, ShadowControlFeed.ClassName);
        string table = ShadowControlFeed.Table;

        controller.Set($"{table}.m_shadowDirection", PropertyValue.FromVector(direction.Item1, direction.Item2, direction.Item3));
        controller.Set($"{table}.m_shadowColor", PropertyValue.FromInt(colour));
        controller.Set($"{table}.m_flShadowMaxDist", PropertyValue.FromFloat(distance));
        controller.Set($"{table}.m_bDisableShadows", PropertyValue.FromInt(disabled ? 1 : 0));

        return controller;
    }

    /// <remarks>The colour's low byte is red: <c>color32</c>'s memory as an unsigned int, here (100, 120, 140, 255).</remarks>
    [Test]
    public void Observe_AController_WritesAllFourWithRedInTheLowByte()
    {
        ShadowControlFeed feed = new();

        feed.Observe(Controller(80, (0.2f, 0.2f, -2f), 0xFF8C7864, 75f, false), EntityUpdateType.Enter);
        feed.Sample(3);

        feed.At(3).ShouldBe(new SceneShadowControl((0.2f, 0.2f, -2f), (100, 120, 140), 75f, false));
    }

    /// <remarks>Before any controller the client's own defaults stand, which the caller supplies: null.</remarks>
    [Test]
    public void At_BeforeTheFirstController_IsNull()
    {
        ShadowControlFeed feed = new();

        feed.Observe(Controller(80, (0f, 0f, -1f), 0x404040, 50f, true), EntityUpdateType.Enter);
        feed.Sample(10);

        feed.At(9).ShouldBeNull();
        feed.At(10)!.Value.Disabled.ShouldBeTrue();
    }

    /// <remarks>Nothing undoes a controller: a delete, a leave and another class change nothing, and nothing is sampled.</remarks>
    [Test]
    public void Observe_ADeleteALeaveOrAnotherClass_ChangesNothing()
    {
        ShadowControlFeed feed = new();

        feed.Observe(Controller(80, (0f, 0f, -1f), 0x404040, 50f, false), EntityUpdateType.Enter);
        feed.Sample(1);
        feed.Observe(null, EntityUpdateType.Delete);
        feed.Observe(Controller(80, (1f, 0f, 0f), 0, 9f, true), EntityUpdateType.Leave);
        feed.Observe(new EntityState(40, 0, 0, "CBaseDoor"), EntityUpdateType.Enter);
        feed.Sample(2);

        feed.Samples.Count.ShouldBe(1);
        feed.At(2).ShouldBe(new SceneShadowControl((0f, 0f, -1f), (64, 64, 64), 50f, false));
    }
}
