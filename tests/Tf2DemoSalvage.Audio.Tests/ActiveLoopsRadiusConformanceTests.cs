using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// A channel's model radius (B724): `SND_Spatialize` (engine.dll FUN_180029440) asks the client for it only while the
/// channel's is 0, and inside it the channel turns mono, so a sound 10 units to the right inside a 48 radius is 0.9 in
/// each ear (`SpeakerPan.InsideRadius` weight 1) where without one it is all in the right ear.
/// </summary>
public sealed class ActiveLoopsRadiusConformanceTests
{
    private const int Entity = 3;
    private const int Voice = 7;

    /// <summary>A listener at the origin looking down +X, whose right is −Y.</summary>
    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    private static SceneSound Beep() => new(1, "npc/beep.wav", 1, Entity, 0, 1f, 75, 100, 0f, 0f, -10f, 0f);

    private static (float Left, float Right) PanOf(ActiveLoops loops)
    {
        (int voice, float left, float right) = loops.PansAt((0f, 0f, 0f), Right).ShouldHaveSingleItem();
        voice.ShouldBe(Voice);

        return (left, right);
    }

    [Test]
    public void PansAt_AChannelWithNoRadius_IsWhereItStands()
    {
        ActiveLoops loops = new();
        loops.Track(Beep(), Voice);

        PanOf(loops).ShouldBe((0f, 1f));
    }

    [Test]
    public void PansAt_AChannelStartedWithARadius_IsMonoInsideIt()
    {
        ActiveLoops loops = new();
        loops.Track(Beep(), Voice, radius: 48f);

        PanOf(loops).ShouldBe((0.9f, 0.9f));
    }

    [Test]
    public void Follow_AChannelWithNoRadiusYet_TakesTheOneGiven()
    {
        ActiveLoops loops = new();
        loops.Track(Beep(), Voice);

        loops.Follow(static entity => entity == Entity ? (0f, -10f, 0f, 48f) : null);

        PanOf(loops).ShouldBe((0.9f, 0.9f));
    }

    [Test]
    public void Follow_AChannelWithARadius_KeepsItWhateverIsGivenAfter()
    {
        ActiveLoops loops = new();
        loops.Track(Beep(), Voice, radius: 48f);

        loops.Follow(static entity => entity == Entity ? (0f, -10f, 0f, 0f) : null);

        PanOf(loops).ShouldBe((0.9f, 0.9f), "asked only while 0, so a later 0 does not undo it");
    }
}
