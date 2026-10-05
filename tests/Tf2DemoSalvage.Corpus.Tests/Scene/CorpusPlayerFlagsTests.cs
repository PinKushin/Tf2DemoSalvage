using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `FL_FROZEN`'s bit as each era's writer declared it — a real-bytes question, since only a period client's
/// `dem_datatables` says which `const.h` it was built with (<see cref="PlayerFlags.Frozen"/>).
/// </summary>
/// <remarks>The 2009 POV specimen declares `m_fFlags` in 9 bits, the 2013 one in 11 (`schema` probe, 2026-10-05).</remarks>
public sealed class CorpusPlayerFlagsTests
{
    [TestCase("tf2-2009-build3862-pov-cp_badlands", 1 << 5, TestName = "FrozenFlag_The2009Specimen_IsBitFive")]
    [TestCase("tf2-2013-build1729296-pov-cp_badlands", 1 << 6, TestName = "FrozenFlag_The2013Specimen_IsBitSix")]
    public void FrozenFlag_ByEra(string demo, int expected) =>
        TimelineCache.For(Corpus.Demo(demo)).FrozenFlag.ShouldBe(expected);
}
