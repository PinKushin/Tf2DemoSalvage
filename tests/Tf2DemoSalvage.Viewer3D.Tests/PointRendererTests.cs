using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Renders points into an offscreen texture and inspects the pixels that come back.
/// </summary>
/// <remarks>
/// **This is how rendering gets tested here, and it needs neither a window nor a desktop.** The
/// renderer draws into a texture, the texture is copied to a staging resource, and the CPU reads
/// the bytes — so "a dot appears where this entity is" becomes an exact assertion about a
/// numbered pixel rather than something a person has to look at.
///
/// A screenshot of the desktop would have been the obvious alternative and is far worse: it
/// depends on DPI scaling, on window position, on nothing overlapping the window, and on the
/// display's colour handling. None of those are properties of the code under test.
///
/// **Falls back to the WARP software adapter**, which means these run on a machine with no GPU —
/// including a CI runner. They are skipped only if even WARP is unavailable, which would be a
/// broken machine rather than an ordinary one.
/// </remarks>
public sealed class PointRendererTests
{
    private const int Size = 64;

    private OffscreenTarget _target = null!;

    [SetUp]
    public void CreateTarget()
    {
        OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("No Direct3D 11 device is available, not even WARP.");
        }

        // Assert.Ignore throws, so the compiler already knows this is not null.
        _target = target;
    }

    [TearDown]
    public void DisposeTarget() => _target?.Dispose();

    [Test]
    public void APointDrawnAtTheCentreLandsInTheMiddleOfTheTarget()
    {
        // The claim the whole viewer rests on: normalised device coordinates put a thing where
        // the camera said it should be. Asserted on named pixels, so a mirrored Y or a swapped
        // axis fails here rather than looking subtly wrong on screen.
        _target.Clear(0f, 0f, 0f);
        _target.Draw([new ScenePoint(0f, 0f, 1f, 0f, 0f)]);

        _target.PixelAt(Size / 2, Size / 2).ShouldBe((255, 0, 0));
    }

    [Test]
    public void NothingIsDrawnWhereNoPointIs()
    {
        // The control. Without it, a renderer that filled the entire target with the point colour
        // would pass the test above - and "everything is red" is a real failure mode for a
        // pass-through shader with a broken vertex buffer.
        _target.Clear(0f, 0f, 0f);
        _target.Draw([new ScenePoint(0f, 0f, 1f, 0f, 0f)]);

        _target.PixelAt(2, 2).ShouldBe((0, 0, 0));
        _target.PixelAt(Size - 3, Size - 3).ShouldBe((0, 0, 0));
    }

    [Test]
    public void PositiveYIsTowardsTheTopOfTheImage()
    {
        // Direct3D's clip space has +Y up while an image's rows run downwards, so a point at
        // y = +0.5 must appear in the UPPER half. Getting this wrong mirrors the map, and on a
        // symmetric TF2 map that is nearly invisible - which is exactly why it is asserted.
        _target.Clear(0f, 0f, 0f);
        _target.Draw([new ScenePoint(0f, 0.5f, 0f, 1f, 0f)]);

        int quarterFromTop = Size / 4;
        _target.PixelAt(Size / 2, quarterFromTop).ShouldBe((0, 255, 0));
        _target.PixelAt(Size / 2, Size - quarterFromTop).ShouldBe((0, 0, 0));
    }

    [Test]
    public void EachPointKeepsItsOwnColour()
    {
        // Two points, two colours: a renderer that read the colour from the wrong vertex, or held
        // one colour in a constant buffer, would draw both the same and pass a single-point test.
        _target.Clear(0f, 0f, 0f);
        _target.Draw(
        [
            new ScenePoint(-0.5f, 0f, 1f, 0f, 0f),
            new ScenePoint(0.5f, 0f, 0f, 0f, 1f),
        ]);

        _target.PixelAt(Size / 4, Size / 2).ShouldBe((255, 0, 0));
        _target.PixelAt(Size * 3 / 4, Size / 2).ShouldBe((0, 0, 255));
    }

    [Test]
    public void DrawingNoPointsLeavesTheTargetAsItWas()
    {
        _target.Clear(0f, 0f, 1f);
        _target.Draw([]);

        _target.PixelAt(Size / 2, Size / 2).ShouldBe((0, 0, 255));
    }

    /// <summary>The clip-space Y that lands on the centre of a given pixel row.</summary>
    /// <remarks>
    /// **A line aimed at y = 0 is aimed at a pixel BOUNDARY**, not a pixel. With 64 rows the
    /// centre of the image falls between rows 31 and 32, and which one a rasteriser fills there is
    /// a matter of fill rules rather than of the code under test. Aiming at a row's centre makes
    /// the expected pixel unambiguous - the first version of these tests failed for exactly this
    /// reason and the code was correct.
    /// </remarks>
    private static float NdcYForRow(int row) => 1f - (2f * (row + 0.5f) / Size);

    [Test]
    public void AHorizontalLineIsDrawnAcrossTheTarget()
    {
        // The map is drawn as edges, so this is the primitive the whole overhead view rests on.
        // Measured at three points along it rather than one, because a line that rendered as a
        // single stray pixel would satisfy a single-pixel check.
        const int row = 20;

        _target.Clear(0f, 0f, 0f);
        _target.DrawLines([((-0.9f, NdcYForRow(row)), (0.9f, NdcYForRow(row)))], 1f, 1f, 1f);

        _target.PixelAt(Size / 4, row).ShouldBe((255, 255, 255));
        _target.PixelAt(Size / 2, row).ShouldBe((255, 255, 255));
        _target.PixelAt(Size * 3 / 4, row).ShouldBe((255, 255, 255));
    }

    [Test]
    public void ALineDoesNotFillTheTarget()
    {
        // The control: without it, a renderer that drew a filled quad instead of a line would
        // pass every point sampled along the line itself.
        const int row = 20;

        _target.Clear(0f, 0f, 0f);
        _target.DrawLines([((-0.9f, NdcYForRow(row)), (0.9f, NdcYForRow(row)))], 1f, 1f, 1f);

        _target.PixelAt(Size / 2, row + 8).ShouldBe((0, 0, 0));
        _target.PixelAt(Size / 2, row - 8).ShouldBe((0, 0, 0));
    }

    [Test]
    public void SegmentsAreIndependentRatherThanOnePolyline()
    {
        // A line STRIP would join the end of one segment to the start of the next, drawing edges
        // that do not exist - across a map that means a web of lines between unrelated walls.
        _target.Clear(0f, 0f, 0f);
        _target.DrawLines(
        [
            ((-0.9f, NdcYForRow(16)), (-0.1f, NdcYForRow(16))),
            ((0.1f, NdcYForRow(48)), (0.9f, NdcYForRow(48))),
        ], 1f, 1f, 1f);

        // The join a strip would draw runs diagonally through the middle of the target.
        _target.PixelAt(Size / 2, Size / 2).ShouldBe((0, 0, 0));
    }

    [Test]
    public void ManyPointsGrowTheBufferWithoutLosingAny()
    {
        // The vertex buffer grows in powers of two, and the first and last point are the two a
        // resize is most likely to drop.
        List<ScenePoint> points = [];
        for (int i = 0; i < 200; i++)
        {
            points.Add(new ScenePoint(0f, 0f, 0f, 0f, 0f));
        }

        points[0] = new ScenePoint(-0.5f, 0f, 1f, 0f, 0f);
        points[^1] = new ScenePoint(0.5f, 0f, 0f, 1f, 0f);

        _target.Clear(0f, 0f, 0f);
        _target.Draw(points);

        _target.PixelAt(Size / 4, Size / 2).ShouldBe((255, 0, 0));
        _target.PixelAt(Size * 3 / 4, Size / 2).ShouldBe((0, 255, 0));
    }
}
