namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary><c>GetBloomAmount</c>, `viewpostprocess.cpp:1421-1468` (B514).</summary>
/// <remarks>
/// <c>currentBloomAmount = GetCurrentBloomScale() * rate + ( 1 - rate ) * currentBloomAmount</c>, rate
/// <c>mat_bloomamount_rate</c> 0.05, from a static of one; the goal is the controller's scale when it set one, else
/// <c>mat_bloomscale</c> 1; zero when bloom is off, with the static untouched.
/// </remarks>
public sealed class BloomAmountConformanceTests
{
    [Test]
    public void Next_TheMapsHalfScale_WalksFivePercentOfTheGapPerFrame()
    {
        BloomAmount bloom = new();

        bloom.Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: true, custom: 0.5f).ShouldBe(0.975f, 1e-6f);
        bloom.Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: true, custom: 0.5f).ShouldBe(0.95125f, 1e-6f);
        bloom.Settled.ShouldBeFalse();
    }

    [Test]
    public void Next_AfterTwoHundredFrames_IsSettledAtTheGoal()
    {
        BloomAmount bloom = new();
        float amount = 0f;

        for (int frame = 0; frame < 200; frame++)
        {
            amount = bloom.Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: true, custom: 0.5f);
        }

        amount.ShouldBe(0.5f, 0.002f);
        bloom.Settled.ShouldBeTrue();
    }

    [Test]
    public void Next_NoControllerScale_IsTheCvarsOne()
    {
        BloomAmount bloom = new();

        bloom.Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: false, custom: 0.5f).ShouldBe(1f);
        bloom.Settled.ShouldBeTrue();
    }

    /// <remarks>
    /// `viewpostprocess.cpp:1460-1463`: under `HDR_TYPE_NONE` — `mat_hdr_level 1` — the amount is multiplied by
    /// `mat_non_hdr_bloom_scalefactor`, ".3" (`:66`), after the walk, so the static still walks the unscaled amount.
    /// </remarks>
    [Test]
    public void Next_UnderNoHdr_IsScaledByTheNonHdrFactorAndTheStaticIsNot()
    {
        BloomAmount bloom = new();

        bloom.Next(enabled: true, Content.Bsp.HdrType.None, useCustom: true, custom: 0.5f).ShouldBe(0.975f * 0.3f, 1e-6f);
        bloom.Current.ShouldBe(0.975f, 1e-6f);
    }

    [Test]
    public void Next_UnderIntegerHdr_IsUnscaled() =>
        new BloomAmount().Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: false, custom: 0f).ShouldBe(1f);

    /// <remarks>Off returns zero and leaves the static where it was, so turning it back on resumes the walk.</remarks>
    [Test]
    public void Next_Disabled_IsZeroAndKeepsTheStatic()
    {
        BloomAmount bloom = new();

        bloom.Next(enabled: true, Content.Bsp.HdrType.IntegerHdr, useCustom: true, custom: 0.5f);
        bloom.Next(enabled: false, Content.Bsp.HdrType.IntegerHdr, useCustom: true, custom: 0.5f).ShouldBe(0f);
        bloom.Current.ShouldBe(0.975f, 1e-6f);
    }
}
