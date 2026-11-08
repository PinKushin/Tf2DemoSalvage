using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A frame a draw never took leaves its cells owed until a frame draws them — <c>SHADOW_FLAGS_TEXTURE_DIRTY</c>, which the
/// engine clears only once the cell is drawn (`DrawRenderToTextureShadow`, `clientshadowmgr.cpp:3774-3796`; B702, finding 107).
/// </summary>
public sealed class OwedShadowCellsTests
{
    private static readonly ShadowProjection Projection = ShadowProjection.ForRenderToTexture(
        Vector3.Zero, (0f, 0f, 0f), new Vector3(-4f), new Vector3(4f), -Vector3.UnitZ, 50f);

    private static readonly ShadowCell First = new(0, 0, 16, 16);
    private static readonly ShadowCell Second = new(16, 0, 16, 16);

    /// <summary>An enumerated shadow: its hierarchy lists its caster.</summary>
    private static CastShadow Listed(int entity, ShadowCell cell, bool redraw = false) =>
        new(entity, [entity], Projection, cell) { Redraw = redraw };

    /// <summary>A shadow handed back unenumerated, with the cell it last had and nothing to draw.</summary>
    private static CastShadow Unlisted(int entity, ShadowCell cell) => new(entity, [], Projection, cell);

    private static ShadowFrame FrameOf(params CastShadow[] casters) => new(casters, [], Vector3.One, []);

    private static IEnumerable<(int, bool)> Redraws(ShadowFrame frame) => frame.Casters.Select(shadow => (shadow.EntityIndex, shadow.Redraw));

    /// <remarks>
    /// Entity 1's new cell, in a frame no draw took, is redrawn by the next frame that lists it on that cell; entity 3 was
    /// owed nothing. With nothing owed a frame passes through untouched — the control.
    /// </remarks>
    [Test]
    public void Apply_AFrameListingAnOwedShadowOnItsCell_RedrawsIt()
    {
        OwedShadowCells owed = new();
        ShadowFrame next = FrameOf(Listed(1, First), Listed(3, Second));

        owed.Apply(next).ShouldBeSameAs(next, "the control: nothing owed changes nothing");

        owed.Owe(FrameOf(Listed(1, First, redraw: true), Listed(3, Second)));

        Redraws(owed.Apply(next)).ShouldBe([(1, true), (3, false)]);
        owed.Count.ShouldBe(0);
    }

    /// <remarks>
    /// **The debt outlives a frame that does not list the shadow**: entity 1, handed back unenumerated in the next frame,
    /// is not drawn there — the engine draws only what its view enumerated — but its flag stays set, so the frame after,
    /// which lists it again on the same cell, redraws it.
    /// </remarks>
    [Test]
    public void Apply_AnOwedShadowNotListedThenListed_IsRedrawnWhenListed()
    {
        OwedShadowCells owed = new();

        owed.Owe(FrameOf(Listed(1, First, redraw: true)));

        Redraws(owed.Apply(FrameOf(Unlisted(1, First)))).ShouldBe([(1, false)]);
        owed.Count.ShouldBe(1);
        Redraws(owed.Apply(FrameOf(Listed(1, First)))).ShouldBe([(1, true)]);
    }

    /// <remarks>
    /// **A cell given to another shadow settles the debt**: entity 9 is handed entity 1's cell new (<c>UseTexture</c>) and
    /// redraws it, so entity 1 — unlisted, its stale cell the same — is not drawn over entity 9's image, and owes nothing
    /// after. Entity 2, moved to a cell of its own, settles too.
    /// </remarks>
    [Test]
    public void Apply_AnOwedCellAnotherShadowRedrawsOrAShadowOnANewCell_SettlesTheDebt()
    {
        OwedShadowCells owed = new();

        owed.Owe(FrameOf(Listed(1, First, redraw: true), Listed(2, Second, redraw: true)));

        ShadowFrame next = FrameOf(Listed(9, First, redraw: true), Listed(1, First), Listed(2, new ShadowCell(32, 0, 16, 16)));

        Redraws(owed.Apply(next)).ShouldBe([(9, true), (1, false), (2, false)]);
        owed.Count.ShouldBe(0);
    }
}
