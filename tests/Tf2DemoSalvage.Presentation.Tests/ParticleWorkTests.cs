namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class ParticleWorkTests
{
    [Test]
    public void Since_TwoReadingsOverFourFrames_GivesEachCounterPerFrame()
    {
        ParticleWork start = new(Alive: 100, Steps: 50, Slept: 4, Gathered: 80);
        ParticleWork end = new(Alive: 140, Steps: 86, Slept: 8, Gathered: 110);

        end.Since(start, 4).ShouldBe("particle work a frame: 10 effects alive, 9 steps, 1 asleep, 7.5 handed to the draw");
    }
}
