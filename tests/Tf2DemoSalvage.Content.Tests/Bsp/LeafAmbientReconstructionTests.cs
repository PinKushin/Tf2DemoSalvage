using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Reconstructing a leaf's ambient light the way <c>Mod_LeafAmbientColorAtPos</c> does.
/// </summary>
/// <remarks>
/// **Transcribed from vrad, which publishes the function this project believed was closed.**
/// <c>utils/vrad/leaf_ambient_lighting.cpp</c>:
///
/// <code>
/// // do an inverse squared distance weighted average of the samples to reconstruct
/// // the original function
/// float dist = (list[i].pos - pos).LengthSqr();
/// float factor = 1.0f / (dist + 1.0f);
/// totalFactor += factor;
/// for ( int j = 0; j &lt; 6; j++ )
///     pOut[j] += list[i].cube[j] * factor;
/// ...
/// pOut[i] *= (1.0f / totalFactor);
/// </code>
///
/// **The compiler relies on the reader doing this, which is what makes nearest-sample wrong rather
/// than merely approximate.** <c>CompressAmbientSampleList</c> deletes any sample whose value this
/// reconstruction already predicts to within 3 in gamma space — so the sample set a map ships is
/// deliberately sparse, kept only because interpolating it reproduces the original function. Taking
/// the nearest of a set thinned on that assumption reads back whichever survivor happened to be
/// closest.
///
/// Measured cost of getting it wrong, cp_process_f12: leaf 2843 holds 16 samples, and nearest-sample
/// returns 0.1027 at z=772 and 0.4141 ninety-six units above it. Its mirror-image leaf on the other
/// side of a symmetric map returns 0.3936 at both. One capture point drew dark and its opposite
/// number did not.
/// </remarks>
public sealed class LeafAmbientReconstructionTests
{
    /// <summary>A cube whose every face is one grey value, so weights are readable in the result.</summary>
    private static AmbientCube Grey(float value) =>
        new((value, value, value), (value, value, value), (value, value, value),
            (value, value, value), (value, value, value), (value, value, value));

    /// <summary>
    /// A leaf one unit on a side, so a sample's stored fraction is also its world offset.
    /// </summary>
    /// <remarks>
    /// Sample positions are fractions of the leaf's bounding box, so a unit box makes the
    /// arithmetic in these tests readable: a sample written as 3 sits at world 3. That is outside
    /// the box, which a real map never does — but the weighting is a function of distance alone,
    /// and keeping the distances legible is what lets the expected values be computed by hand
    /// rather than copied from a run of the code under test.
    /// </remarks>
    private static AmbientSamples Leaf(params AmbientSample[] samples) =>
        new(samples, (0f, 0f, 0f, 1f, 1f, 1f));

    [Test]
    public void OneSample_IsReturnedWhole()
    {
        // factor cancels against totalFactor when there is only one, whatever the distance.
        AmbientSamples leaf = Leaf(new AmbientSample(Grey(0.5f), 100f, 0f, 0f));

        leaf.At(0f, 0f, 0f).PositiveZ.Red.ShouldBe(0.5f, 0.0001f);
    }

    [Test]
    public void TwoSamples_AreWeightedByInverseSquaredDistance()
    {
        // Predicted by hand from Valve's formula rather than from a run of this code:
        //   near: dist^2 = 9,   factor = 1/10   = 0.1
        //   far:  dist^2 = 99^2 = 9801, factor = 1/9802 ~= 0.000102
        //   value = (1.0*0.1 + 0.0*0.000102) / (0.1 + 0.000102) = 0.998981...
        AmbientSamples leaf = Leaf(
            new AmbientSample(Grey(1f), 3f, 0f, 0f),
            new AmbientSample(Grey(0f), 99f, 0f, 0f));

        leaf.At(0f, 0f, 0f).PositiveZ.Red.ShouldBe(0.99898f, 0.0002f);
    }

    [Test]
    public void ASampleAtThePosition_DoesNotDominateCompletely()
    {
        // **The `+ 1` in `1 / (dist + 1)` is the whole point of this test.** It keeps the weight
        // finite at zero distance, so a sample sitting exactly on the query point is weighted 1
        // rather than infinitely: the answer stays a blend. Dropping that term - the obvious
        // "simplification" when transcribing - turns this into a division by zero and, guarded,
        // into nearest-sample all over again.
        //
        //   at:   dist^2 = 0,   factor = 1
        //   away: dist^2 = 1,   factor = 0.5
        //   value = (1.0*1 + 0.0*0.5) / 1.5 = 0.6667
        AmbientSamples leaf = Leaf(
            new AmbientSample(Grey(1f), 0f, 0f, 0f),
            new AmbientSample(Grey(0f), 1f, 0f, 0f));

        leaf.At(0f, 0f, 0f).PositiveZ.Red.ShouldBe(0.66667f, 0.0001f);
    }

    [Test]
    public void EveryFaceIsWeightedIndependently()
    {
        // The loop runs over all six faces. A transcription that blended one face and copied the
        // rest would pass every test above, since those use grey cubes throughout.
        AmbientCube first = new(
            (1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f), (1f, 1f, 0f), (1f, 0f, 1f), (0f, 1f, 1f));

        AmbientSamples leaf = Leaf(
            new AmbientSample(first, 0f, 0f, 0f),
            new AmbientSample(Grey(0f), 1f, 0f, 0f));

        AmbientCube blended = leaf.At(0f, 0f, 0f);

        // Each face keeps its own colour, scaled by the same 1 / 1.5.
        blended.PositiveX.Red.ShouldBe(0.66667f, 0.0001f);
        blended.PositiveX.Green.ShouldBe(0f, 0.0001f);
        blended.NegativeX.Green.ShouldBe(0.66667f, 0.0001f);
        blended.PositiveZ.Blue.ShouldBe(0.66667f, 0.0001f);
        blended.NegativeY.Red.ShouldBe(0.66667f, 0.0001f);
        blended.NegativeY.Blue.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void NoSamples_AreUnlit()
    {
        // totalFactor would be zero, and dividing by it produces NaN rather than black - a value
        // that propagates silently through every later multiply.
        AmbientSamples leaf = Leaf();

        AmbientCube empty = leaf.At(0f, 0f, 0f);

        empty.PositiveZ.Red.ShouldBe(0f);
        float.IsNaN(empty.PositiveZ.Red).ShouldBeFalse();
    }
}
