using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The builder's output lists accept only edits at their tail — the invariant snapshot marks stand on (D217).</summary>
public sealed class TailListTests
{
    private static TailList<int> Three() => [1, 2, 3];

    /// <remarks>**The edits the builder makes**: append, replace the last, drop the last.</remarks>
    [Test]
    public void Edit_AtTheTail_IsAllowed()
    {
        TailList<int> list = Three();

        list[^1] = 30;
        list.RemoveAt(list.Count - 1);
        list.Add(4);

        list.ShouldBe([1, 2, 4]);
    }

    /// <remarks>**What `choreography` did**: a write behind the last element.</remarks>
    [Test]
    public void Replace_BeforeTheLast_Throws()
    {
        TailList<int> list = Three();

        Should.Throw<InvalidOperationException>(() => list[0] = 10);
    }

    /// <remarks>**A removal behind the last element**, which would shift every mark after it.</remarks>
    [Test]
    public void RemoveAt_BeforeTheLast_Throws()
    {
        TailList<int> list = Three();

        Should.Throw<InvalidOperationException>(() => list.RemoveAt(1));
    }
}
