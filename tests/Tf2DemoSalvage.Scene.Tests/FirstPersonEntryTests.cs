using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Whether the first-person view can be entered at a tick, and what to say about it.
/// </summary>
/// <remarks>
/// **This was the decision half of <c>MainForm.ToggleFirstPerson</c>** (B188, D90). What stays in
/// the window is the mode flag, the invalidate and the status bar; what left is the question of
/// whether there are any eyes to borrow and which kind they are.
///
/// **Refusing has to be visible, and that is the reason this returns text rather than a bool.** A
/// key that silently does nothing reads as a broken key — and the reason it can refuse is a real
/// property of the demo rather than a failure, so it deserves a sentence naming which case it is.
/// A view mapping a bool back to a sentence would be re-deriving what this already knows.
/// </remarks>
public sealed class FirstPersonEntryTests
{
    private const float Aspect = 16f / 9f;

    [Test]
    public void Enter_WithNoEyeSource_IsRefused()
    {
        // No demo open. Not an error — there is simply nothing to look through.
        SpectatorView spectator = new(NullLogger.Instance);

        FirstPersonEntry entry = spectator.Enter(0, Aspect);

        entry.Entered.ShouldBeFalse();
        entry.Status.ShouldNotBeNull("a refusal the user cannot see reads as a broken key");
    }

    [Test]
    public void Enter_WithNobodyAtThisTick_IsRefused()
    {
        // **The demo has players, just not here.** A recording can lose its subject mid-playback,
        // which is the case that makes this a property of the tick rather than of the demo.
        SpectatorView spectator = new(NullLogger.Instance) { Eyes = new Eyes(atTick: 5) };

        spectator.Enter(0, Aspect).Entered.ShouldBeFalse();
    }

    [Test]
    public void Enter_WithAPlayerAtThisTick_IsAllowed()
    {
        // The bystander for the test above: same source, same call, a tick that HAS somebody. If
        // this failed too, the refusal above would be measuring the fixture rather than the tick.
        SpectatorView spectator = new(NullLogger.Instance) { Eyes = new Eyes(atTick: 5) };

        FirstPersonEntry entry = spectator.Enter(5, Aspect);

        entry.Entered.ShouldBeTrue();
        entry.Status.ShouldBeNull("nothing refused, so there is nothing to tell the user about");
    }

    [Test]
    public void Enter_WithARecordedCamera_SaysItIsFollowingTheRecording()
    {
        // **The two allowed cases are not the same thing and the log must not blur them.** A POV
        // demo carries the recorder's own view; a SourceTV recording carries none and the viewer
        // picks somebody to spectate. Which one is in play decides whether a wrong-looking angle is
        // our bug or the recording's.
        SpectatorView spectator = new(NullLogger.Instance)
        {
            Eyes = new Eyes(atTick: 5, recorded: true),
        };

        spectator.Enter(5, Aspect).Message.ShouldContain("the recording's own camera");
    }

    [Test]
    public void Enter_WithNoRecordedCamera_SaysItIsSpectating()
    {
        // The control pair for the line above: identical call, one flag different, and the sentence
        // must change. An implementation that always said "spectating" would pass that test alone.
        SpectatorView spectator = new(NullLogger.Instance)
        {
            Eyes = new Eyes(atTick: 5, recorded: false),
        };

        spectator.Enter(5, Aspect).Message.ShouldContain("spectating");
    }

    /// <summary>An eye source with one player, at one tick only.</summary>
    private sealed class Eyes(int atTick, bool recorded = false) : IEyeSource
    {
        public int? RecorderEntityIndex => recorded ? 1 : null;

        public RecordedView? RecordedViewAt(int tick) =>
            recorded && tick == atTick
                ? new RecordedView((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false)
                : null;

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
            tick == atTick
                ? [new ScenePlayer(1, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 3)]
                : [];
    }
}
