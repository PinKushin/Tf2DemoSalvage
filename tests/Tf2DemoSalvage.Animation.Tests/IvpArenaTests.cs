using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The scratch a heap solve takes its arrays from (B708): every array as a new one would be — zeroed, exactly as long as
/// asked, and never handed out twice inside one mark.
/// </summary>
public sealed class IvpArenaTests
{
    [Test]
    public void Doubles_AfterTheReleasePastIt_IsTheSameArrayZeroed()
    {
        IvpArena arena = new();
        int mark = arena.Mark;
        double[] first = arena.Doubles(3);
        first[1] = 5d;
        arena.Release(mark);

        double[] again = arena.Doubles(3);

        again.ShouldBeSameAs(first);
        again.ShouldBe([0d, 0d, 0d]);
    }

    [Test]
    public void Doubles_TwiceInsideOneMark_AreTwoArrays()
    {
        IvpArena arena = new();
        int mark = arena.Mark;
        double[] first = arena.Doubles(3);
        arena.Release(mark);

        double[] reused = arena.Doubles(3);
        double[] second = arena.Doubles(3);

        reused.ShouldBeSameAs(first);
        second.ShouldNotBeSameAs(first);
    }

    [Test]
    public void Doubles_OfAnotherLength_IsNeverAnOldArray()
    {
        IvpArena arena = new();
        int mark = arena.Mark;
        double[] three = arena.Doubles(3);
        arena.Release(mark);

        double[] four = arena.Doubles(4);

        four.ShouldNotBeSameAs(three);
        four.Length.ShouldBe(4);
    }

    [Test]
    public void Release_ToALaterMark_KeepsWhatWasTakenBeforeIt()
    {
        IvpArena arena = new();
        double[] outer = arena.Doubles(2);
        int mark = arena.Mark;
        double[] inner = arena.Doubles(2);

        arena.Release(mark);

        arena.Mark.ShouldBe(1);
        arena.Doubles(2).ShouldBeSameAs(inner, "only the inner array was handed back");
        outer.ShouldNotBeSameAs(inner);
    }

    [Test]
    public void Zeros_WithNoArena_IsANewArrayOfZeros()
    {
        double[] zeros = IvpArena.Zeros(null, 2);

        zeros.ShouldBe([0d, 0d]);
    }
}
