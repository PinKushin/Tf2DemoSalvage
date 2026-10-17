using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A trimmed history gives back spare capacity only past half (D217 step 4), so it does not regrow every checkpoint.</summary>
public sealed class ListCapacityTests
{
    /// <remarks>**The boundary**: 10 slots holding 5 is exactly half spare and stays; holding 4 shrinks to 4.</remarks>
    [Test]
    public void TrimIfSparse_AtAndPastHalfSpare_KeepsThenShrinks()
    {
        List<int> half = new(10) { 1, 2, 3, 4, 5 };
        List<int> sparse = new(10) { 1, 2, 3, 4 };

        half.TrimIfSparse();
        sparse.TrimIfSparse();

        (half.Capacity, sparse.Capacity, sparse.Count).ShouldBe((10, 4, 4));
    }
}
