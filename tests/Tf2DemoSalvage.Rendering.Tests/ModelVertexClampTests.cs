namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The B597 control: <c>TF2VIEW_MODEL_VERTS</c> caps how many vertices each model draw submits, so a GPU timestamp
/// pass can be split into vertex work and per-draw cost (docs/measurements/2026-10-30-b597-gpu-split.md).
/// </summary>
[TestFixture]
public sealed class ModelVertexClampTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("three")]
    [TestCase("-1")]
    [TestCase("3.5")]
    public void Parse_NothingUsable_IsOff(string? text)
    {
        ModelVertexClamp.Parse(text).ShouldBeNull();
    }

    [TestCase("0", 0)]
    [TestCase("3", 3)]
    [TestCase(" 3 ", 3)]
    [TestCase("1200", 1200)]
    public void Parse_AWholeCount_IsThatCount(string text, int expected)
    {
        ModelVertexClamp.Parse(text).ShouldBe(expected);
    }

    [Test]
    public void Apply_Off_ReturnsTheBatchesOwnCount()
    {
        ModelVertexClamp.Apply(681, null).ShouldBe(681);
    }

    [TestCase(681, 3, 3)]
    [TestCase(681, 0, 0)]
    [TestCase(2, 3, 2)]
    [TestCase(3, 3, 3)]
    public void Apply_ACap_NeverRaisesTheCountAndNeverExceedsTheCap(int count, int cap, int expected)
    {
        ModelVertexClamp.Apply(count, cap).ShouldBe(expected);
    }
}
