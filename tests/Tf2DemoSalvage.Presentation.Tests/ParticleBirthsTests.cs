using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class ParticleBirthsTests
{
    [Test]
    public void Since_TwoTalliesOverTwoSeconds_NamesTheBusiestFirstPerSecond()
    {
        Dictionary<string, long> start = new() { ["smoke"] = 10, ["sparks"] = 4 };
        Dictionary<string, long> now = new() { ["smoke"] = 30, ["sparks"] = 4, ["blood"] = 6, ["fire"] = 50 };

        ParticleBirths.Since(start, now, 2d, 2).ShouldBe("particles born a second: 38 in all; fire 25, smoke 10");
    }

    [Test]
    public void Since_NoSeconds_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ParticleBirths.Since(new Dictionary<string, long>(), new Dictionary<string, long>(), 0d, 1));
    }
}
