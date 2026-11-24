using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`m_Shared.IsLoser()` as the recorder's prediction asks it, off a synthetic demo's signon ConVars (B823 review 3).</summary>
/// <remarks>
/// **`tf_always_loser.GetBool()` is `!!GetInt()`** (`convar.h:520`, `convar.cpp:791-802`): `nan` is MSVC's `0x80000000`, so
/// a loser, where C#'s `(int)` of a NaN is 0; `0.5` is 0. The round is running, so without the ConVar nobody is a loser —
/// the control. Through `RecorderPrediction.IsLoserAt`, the step the packet path calls to set
/// <see cref="PredictedPlayer.IsLoser"/> — the value `TfGameMovement` reads for the loser's duck crop
/// (`TfGameMovement.cs`, `_speedCropped &amp;&amp; _player.IsLoser`) — from the timeline, the packet's tick and the command
/// tick whose ConVars it reads (B823 review 4, Fable).
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

        RecorderPrediction.IsLoserAt(timeline, recorder, packetTick: 101, commandTick: 101).ShouldBe(expected);
    }

    /// <remarks>
    /// **The ConVars are the command tick's, the rules the packet's** (B823 review 5, Fable, Sonnet, Grok): the packet path
    /// read `ServerConVarsAt(tick)` with `tick` the tick whose commands are re-run, and the game rules at `packet.Tick`
    /// (RecorderPrediction.cs at 180494a01: `:304` `ServerConVars serverConVars = _timeline.ServerConVarsAt(tick);`, `:359`
    /// `IsLoser = IsLoser(recorder, packet.Tick, serverConVars)`), so the extraction moved those reads and changed neither.
    /// Here `tf_always_loser 1` arrives in tick 102's packet: a packet at 101 re-running 102's commands is a loser, and the
    /// same packet asked for 101's is not.
    /// </remarks>
    [TestCase(101, 102, true)]
    [TestCase(101, 101, false)]
    public void IsLoserAt_AConVarArrivingBetweenThePacketAndTheCommand_IsTheCommandTicks(int packetTick, int commandTick, bool expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfGesturesSendingAlwaysLoserAt(
                Interval,
                SceneTeams.Red,
                Scout,
                (RoundRunning, SceneTeams.Unassigned, NoMatchGroup),
                "1",
                atTick: 102,
                new SyntheticPlayer.GestureSnapshot(100, 0f, OnGround),
                new SyntheticPlayer.GestureSnapshot(101, 0f, OnGround),
                new SyntheticPlayer.GestureSnapshot(102, 0f, OnGround)),
            client: new Core.Net.ClientInterp(0f, 0f, 66f));
        ScenePlayer recorder = timeline.Frames.Single(frame => frame.Tick == 101).Players.Single();

        RecorderPrediction.IsLoserAt(timeline, recorder, packetTick, commandTick).ShouldBe(expected);
    }
}
