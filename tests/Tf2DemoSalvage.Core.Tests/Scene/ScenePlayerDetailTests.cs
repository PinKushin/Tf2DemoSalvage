using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `ScenePlayer`'s rarely-changing half, held by reference so frames can share it (D217), must read exactly as the
/// inline fields it replaced.
/// </summary>
/// <remarks>
/// **Why it exists**: an 800-byte struct per player per frame was 1,498 MB of f12's 2,948 MB timeline, and most of it
/// was these fields, unchanged since the tick before. Sharing is only safe while nothing a reader can observe changes —
/// values, `with`, equality — which is what these hold.
/// </remarks>
public sealed class ScenePlayerDetailTests
{
    private static ScenePlayer Player() => new(1, 0f, 0f, 0f, 2, 125, 5);

    /// <remarks>**The copy-on-write**: a `with` that sets a detail member must not reach back into the original's.</remarks>
    [Test]
    public void With_ADetailMember_LeavesTheOriginalUnchanged()
    {
        ScenePlayer original = Player() with { MaxHealth = 150, CloakMeter = 40f };

        ScenePlayer changed = original with { MaxHealth = 175 };

        (original.MaxHealth, original.CloakMeter, changed.MaxHealth, changed.CloakMeter).ShouldBe((150, 40f, 175, 40f));
    }

    /// <remarks>
    /// **A default set explicitly equals one never set**, as it did when the field was inline — a detail equal to
    /// every default is stored as none.
    /// </remarks>
    [Test]
    public void Equals_ADefaultSetExplicitly_EqualsOneNeverSet()
    {
        (Player() with { Gravity = 0f, MaxHealth = null }).ShouldBe(Player());
    }

    /// <remarks>The control for the line above: a value that is not the default makes them differ.</remarks>
    [Test]
    public void Equals_ADetailMemberSet_DiffersFromOneNeverSet()
    {
        (Player() with { Gravity = 1f }).ShouldNotBe(Player());
    }

    [Test]
    public void SameValues_EqualListsInDifferentInstances_AreTheSame()
    {
        ScenePlayerDetail first = new() { Ammo = [1, 2], ItemChargeMeter = [3f], CloakMeter = 50f };
        ScenePlayerDetail second = new() { Ammo = [1, 2], ItemChargeMeter = [3f], CloakMeter = 50f };

        first.SameValues(second).ShouldBeTrue();
    }

    [TestCase(1)]
    [TestCase(2)]
    public void SameValues_OneValueDifferent_AreNotTheSame(int which)
    {
        ScenePlayerDetail first = new() { Ammo = [1, 2], CloakMeter = 50f };
        ScenePlayerDetail second = which == 1
            ? first with { Ammo = [1, 3] }
            : first with { CloakMeter = 51f };

        first.SameValues(second).ShouldBeFalse();
    }

    /// <remarks>
    /// **The output-level half**: through `DemoTimeline.Build`, a player whose `m_iHideHUD` is held across three ticks
    /// reads it at every frame AND every frame holds one instance — the sharing that is the memory saving.
    /// </remarks>
    [Test]
    public void Build_ADetailHeldAcrossTicks_IsOneInstanceReadAtEveryFrame()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfLocalData([10, 11, 12], null, (1, 5, 4)));
        ScenePlayer[] recorder = [.. timeline.Frames.Select(frame => frame.Players.Single(player => player.EntityIndex == 1))];

        recorder.Select(player => player.HideHud).ShouldBe([4, 4, 4]);
        recorder.Select(player => player.Detail).Distinct(ReferenceComparer.Instance).Count().ShouldBe(1);
    }

    /// <remarks>The control: a value that changes on the last tick is read there and is not the shared instance.</remarks>
    [Test]
    public void Build_ADetailChangingOnTheLastTick_IsReadAndNotShared()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfLocalData([10, 11, 12], 8, (1, 5, 4)));
        ScenePlayer[] recorder = [.. timeline.Frames.Select(frame => frame.Players.Single(player => player.EntityIndex == 1))];

        recorder.Select(player => player.HideHud).ShouldBe([4, 4, 8]);
        recorder.Select(player => player.Detail).Distinct(ReferenceComparer.Instance).Count().ShouldBe(2);
    }

    private sealed class ReferenceComparer : System.Collections.Generic.IEqualityComparer<ScenePlayerDetail>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(ScenePlayerDetail? x, ScenePlayerDetail? y) => ReferenceEquals(x, y);

        public int GetHashCode(ScenePlayerDetail obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
