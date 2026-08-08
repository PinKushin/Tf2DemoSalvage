using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Shape tests for <see cref="PacketEntitiesMessage"/>. The corpus tests exercise header
/// decoding; these pin the derived properties consumers branch on, which no corpus assertion
/// happens to touch.
/// </summary>
public sealed class PacketEntitiesMessageTests
{
    private static PacketEntitiesMessage Message(bool isDelta, int? deltaFromTick) => new(
        MaxEntries: 2048,
        IsDelta: isDelta,
        DeltaFromTick: deltaFromTick,
        BaselineIndex: false,
        UpdatedEntries: 12,
        LengthBits: 96,
        UpdateBaseline: false);

    [Fact]
    public void IsFullSnapshot_IsTheInverseOfIsDelta()
    {
        // Both polarities, so neither a dropped negation nor a constant survives.
        Message(isDelta: false, deltaFromTick: null).IsFullSnapshot.ShouldBeTrue();
        Message(isDelta: true, deltaFromTick: 500).IsFullSnapshot.ShouldBeFalse();
    }
}
