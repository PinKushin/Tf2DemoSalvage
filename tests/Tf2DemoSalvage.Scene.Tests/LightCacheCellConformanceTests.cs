using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Where the engine's light cache evaluates a model's light: `engine.dll` `0x1801b6860`, under `r_lightcachecenter 1`
/// (its default, `0x18000ff90`). The point's cell is 32 × 32 × 128 (`LightcacheGet`, `0x1801b9cd0`); the entry is lit at
/// the cell's centre when that is open and a world trace (`MASK_OPAQUE`) reaches it, else at the centre at the point's
/// own height when a trace reaches that, else at the point.
/// </summary>
public sealed class LightCacheCellConformanceTests
{
    private static readonly Vector3 Point = new(10f, -10f, 100f);

    /// <remarks>x 10 is cell 0, centre 16; y −10 is cell −1, centre −16; z 100 is cell 0, centre 64.</remarks>
    [Test]
    public void Position_AnOpenCell_IsItsCentre()
    {
        LightCacheCell.Position(Point, static _ => false, static (_, _) => true).ShouldBe(new Vector3(16f, -16f, 64f));
    }

    [Test]
    public void Position_ACentreATraceCannotReach_TakesItAtThePointsHeight()
    {
        LightCacheCell.Position(Point, static _ => false, static (_, to) => to.Z > 99f)
            .ShouldBe(new Vector3(16f, -16f, 100f));
    }

    [Test]
    public void Position_NeitherReachable_IsThePointItself()
    {
        LightCacheCell.Position(Point, static _ => false, static (_, _) => false).ShouldBe(Point);
    }

    /// <remarks>A centre inside solid is not traced to at all; the point's height is tried next.</remarks>
    [Test]
    public void Position_ACentreInSolid_IsSkipped()
    {
        LightCacheCell.Position(Point, static at => at.Z < 65f, static (_, _) => true).ShouldBe(new Vector3(16f, -16f, 100f));
    }

    /// <remarks>x −40 is cell −2, whose centre is −48: the cell floors, it does not truncate toward zero.</remarks>
    [Test]
    public void Position_ANegativeCoordinate_FloorsItsCell()
    {
        LightCacheCell.Position(new Vector3(-40f, 0f, 0f), static _ => false, static (_, _) => true).X.ShouldBe(-48f);
    }
}
