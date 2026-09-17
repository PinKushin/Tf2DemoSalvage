using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The side a time-of-impact search is handed from the running simulation (B369, D172).</summary>
public sealed class IvpSimulationSearchSideTests
{
    /// <remarks>
    /// **Slot 0 of the search's motion cache is the side's own matrix** — the object's cache matrix at now, the cache object's
    /// `+0x40` — not the core's matrix, which here sits somewhere else entirely. Found by the paired `.phy` drop: a mid-PSI search
    /// read a rocking crate's corners where its PSI had begun.
    /// </remarks>
    [Test]
    public void Searchable_ASideAtNow_StartsItsMotionAtTheSidesMatrix()
    {
        IvpMatrix atNow = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (1d, 2d, 3d));
        IvpRigidBody core = new() { CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (9d, 9d, 9d)) };
        IvpLedgeSide side = new(
            [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            atNow,
            (1d, 2d, 3d));

        IvpSearchSide searched = IvpSimulation.Searchable(side, core);

        searched.Motion.Current.ShouldBe(atNow);
        searched.Motion.At(0, 0d).ShouldBe(atNow);
    }
}
