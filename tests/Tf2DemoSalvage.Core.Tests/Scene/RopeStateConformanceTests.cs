using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>DT_RopeKeyframe</c>, the NOBASE table no rope was ever read from (B477).</summary>
/// <remarks>
/// **The receive table, `c_rope.cpp:42-64`**: the start and end handles and attachments, the locked points, slack,
/// length, flags, texture scale, segment count, the constrain flag, subdivision, width, scroll speed, its own origin and
/// move parent, and the material as a <c>modelprecache</c> index. The values are a <c>koth_viaduct</c> cable as the 2011
/// SourceTV demo sends it (`entity-census`): flags 104 (<c>ROPE_SIMULATE | ROPE_NO_WIND | ROPE_INITIAL_HANG</c>), ten
/// segments, width 2, texture scale 1, subdivision 2, both points locked.
/// </remarks>
public sealed class RopeStateConformanceTests
{
    private const string Rope = "DT_RopeKeyframe";

    private const int Start = (3 << 11) | 120;

    private const int End = (3 << 11) | 121;

    /// <remarks>**Every field from the rope's own table**, the boolean included.</remarks>
    [Test]
    public void Rope_ForAViaductCable_ReadsItsOwnTable()
    {
        EntityState cable = new(120, 0, 0, "CRopeKeyframe");

        cable.Set($"{Rope}.m_hStartPoint", PropertyValue.FromInt(Start));
        cable.Set($"{Rope}.m_hEndPoint", PropertyValue.FromInt(End));
        cable.Set($"{Rope}.m_iStartAttachment", PropertyValue.FromInt(1));
        cable.Set($"{Rope}.m_iEndAttachment", PropertyValue.FromInt(2));
        cable.Set($"{Rope}.m_Slack", PropertyValue.FromInt(120));
        cable.Set($"{Rope}.m_RopeLength", PropertyValue.FromInt(300));
        cable.Set($"{Rope}.m_fLockedPoints", PropertyValue.FromInt(3));
        cable.Set($"{Rope}.m_RopeFlags", PropertyValue.FromInt(104));
        cable.Set($"{Rope}.m_nSegments", PropertyValue.FromInt(10));
        cable.Set($"{Rope}.m_bConstrainBetweenEndpoints", PropertyValue.FromInt(1));
        cable.Set($"{Rope}.m_Subdiv", PropertyValue.FromInt(2));
        cable.Set($"{Rope}.m_TextureScale", PropertyValue.FromFloat(1f));
        cable.Set($"{Rope}.m_Width", PropertyValue.FromFloat(2f));
        cable.Set($"{Rope}.m_flScrollSpeed", PropertyValue.FromFloat(0.5f));
        cable.Set($"{Rope}.m_iRopeMaterialModelIndex", PropertyValue.FromInt(77));

        cable.Rope().ShouldBe(new SceneRope(
            StartPoint: Start, EndPoint: End, StartAttachment: 1, EndAttachment: 2, Slack: 120, Length: 300,
            LockedPoints: 3, Flags: 104, Segments: 10, ConstrainBetweenEndpoints: true, Subdiv: 2, TextureScale: 1f,
            Width: 2f, ScrollSpeed: 0.5f));
        cable.RopeMaterialIndex().ShouldBe(77);
    }

    /// <remarks>
    /// **What the client constructor leaves** (`c_rope.cpp:1040-1066`): <c>m_Subdiv</c> 255, "use the cvar", and a
    /// texture scale of 4 — and no handle is the invalid one, not slot zero.
    /// </remarks>
    [Test]
    public void Rope_WithOnlyItsSegments_TakesTheClientConstructorsDefaults()
    {
        EntityState rope = new(120, 0, 0, "CRopeKeyframe");

        rope.Set($"{Rope}.m_nSegments", PropertyValue.FromInt(5));

        SceneRope read = rope.Rope().ShouldNotBeNull();

        (read.Subdiv, read.TextureScale, read.ConstrainBetweenEndpoints).ShouldBe((255, 4f, false));
        (read.StartPoint, read.EndPoint).ShouldBe(((1 << 21) - 1, (1 << 21) - 1));
    }

    /// <remarks>
    /// **A rope has no model index**, which is why it never reached a track: <c>ModelIndex</c> stays null for it, and
    /// the timeline admits it by its material instead. Anything else is not a rope.
    /// </remarks>
    [Test]
    public void Rope_ForAnEntityThatIsNotARope_IsNullAndARopeHasNoModelIndex()
    {
        EntityState rope = new(120, 0, 0, "CRopeKeyframe");
        rope.Set($"{Rope}.m_nSegments", PropertyValue.FromInt(5));
        rope.Set($"{Rope}.m_iRopeMaterialModelIndex", PropertyValue.FromInt(77));

        rope.ModelIndex().ShouldBeNull();

        EntityState door = new(40, 0, 0, "CBaseDoor");
        door.Set("DT_BaseEntity.m_nModelIndex", PropertyValue.FromInt(12));

        door.Rope().ShouldBeNull();
        door.RopeMaterialIndex().ShouldBeNull();
    }
}
