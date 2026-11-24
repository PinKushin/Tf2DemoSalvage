using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`m_Shared.IsLoser()` as the recorder's prediction asks it, off a synthetic demo's signon ConVars (B823 review 3).</summary>
/// <remarks>
/// **`tf_always_loser.GetBool()` is `!!GetInt()`** (`convar.h:520`, `convar.cpp:791-802`): `nan` is MSVC's `0x80000000`, so
/// a loser, where C#'s `(int)` of a NaN is 0; `0.5` is 0. The round is running, so without the ConVar nobody is a loser —
/// the control. Through `RecorderPrediction.IsLoser`, the call site Fable and Sonnet asked to see fail.
/// </remarks>
public sealed class RecorderPredictionLoserTests
{
    private const float Interval = 0.015f;
    private const int Scout = 1;
    private const int RoundRunning = 4;
    private const int NoMatchGroup = -1;
    private const int OnGround = PlayerActivityState.OnGround;

    [TestCase("nan", true)]
    [TestCase("0.5", false)]
    [TestCase(null, false)]
    public void IsLoser_UnderTfAlwaysLoserText_ReadsItAsGetBool(string? sent, bool expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfGesturesSendingAlwaysLoser(
                Interval,
                SceneTeams.Red,
                Scout,
                (RoundRunning, SceneTeams.Unassigned, NoMatchGroup),
                sent,
                new SyntheticPlayer.GestureSnapshot(100, 0f, OnGround),
                new SyntheticPlayer.GestureSnapshot(101, 0f, OnGround)),
            client: new Core.Net.ClientInterp(0f, 0f, 66f));
        ScenePlayer recorder = timeline.Frames.Single(frame => frame.Tick == 101).Players.Single();

        RecorderPrediction.IsLoser(timeline, recorder, 101, timeline.ServerConVarsAt(101)).ShouldBe(expected);
    }
}
