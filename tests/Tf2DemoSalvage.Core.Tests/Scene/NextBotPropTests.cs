using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A NextBot's prop carries its class's no-IK flag out of the timeline (B739).</summary>
/// <remarks>
/// **The wiring half**: `NextBotIkConformanceTests` holds the schema rule; this holds that the timeline applies it to the
/// entity's class and the prop the scene draws carries it, which every pose site then reads.
/// </remarks>
public sealed class NextBotPropTests
{
    [Test]
    public void PropsAt_AnEntityWhoseClassIsANextBot_IsMarkedNoIk()
    {
        Prop(SyntheticProp.NextBotDemo()).NoIk.ShouldBeTrue();
    }

    [Test]
    public void PropsAt_AnOrdinaryPropClass_KeepsItsIk()
    {
        // The control: the same entity and tables, without the `baseclass` link to `DT_NextBot`.
        Prop(SyntheticProp.Demo((1, 0, 0))).NoIk.ShouldBeFalse();
    }

    private static SceneProp Prop(byte[] demo)
    {
        DemoTimeline timeline = DemoTimeline.Build(demo);
        List<SceneProp> drawn = [];
        timeline.PropsAt(1d, drawn);

        return drawn.Find(prop => prop.EntityIndex == SyntheticProp.PropEntityIndex)
            .ShouldNotBeNull("the fixture's prop is drawn at tick 1");
    }
}
