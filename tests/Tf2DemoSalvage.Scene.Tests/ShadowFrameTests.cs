using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A frame a draw never took passes its owed cell redraws on (B702, finding 107).</summary>
public sealed class ShadowFrameTests
{
    private static readonly ShadowProjection Projection = ShadowProjection.ForRenderToTexture(
        Vector3.Zero, (0f, 0f, 0f), new Vector3(-4f), new Vector3(4f), -Vector3.UnitZ, 50f);

    private static CastShadow Shadow(int entity, ShadowCell cell, bool redraw, IReadOnlyList<int> hierarchy) =>
        new(entity, hierarchy, Projection, cell) { Redraw = redraw };

    private static ShadowFrame FrameOf(params CastShadow[] casters) => new(casters, [], Vector3.One, []);

    /// <remarks>
    /// The engine clears the dirty flag only once the cell is drawn (`DrawRenderToTextureShadow`,
    /// `clientshadowmgr.cpp:3774-3796`): entity 1's new cell, in a frame no draw took, is redrawn by the next frame — with
    /// the earlier hierarchy when the next has none of its own. Entity 2 moved to another cell, and entity 3 was not owed.
    /// </remarks>
    [Test]
    public void CarryRedraws_AFrameNoDrawTook_PassesItsOwedCellsToTheNextOnTheSameCell()
    {
        ShadowCell first = new(0, 0, 16, 16);
        ShadowCell second = new(16, 0, 16, 16);

        ShadowFrame undrawn = FrameOf(Shadow(1, first, true, [1, 40]), Shadow(2, first, true, [2]), Shadow(3, second, false, [3]));
        ShadowFrame next = FrameOf(Shadow(1, first, false, []), Shadow(2, second, false, [2]), Shadow(3, second, false, [3]));

        ShadowFrame carried = ShadowFrame.CarryRedraws(undrawn, next);

        carried.Casters.Select(shadow => (shadow.EntityIndex, shadow.Redraw)).ShouldBe([(1, true), (2, false), (3, false)]);
        carried.Casters[0].Hierarchy.ShouldBe([1, 40]);
        ShadowFrame.CarryRedraws(FrameOf(), next).ShouldBeSameAs(next, "the control: nothing owed changes nothing");
    }
}
