using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A keyframe is interpolated from when its value APPLIED, not from when the packet arrived.
/// </summary>
/// <remarks>
/// **<c>OnLatchInterpolatedVariables</c> stamps a history entry with the entity's own clock**, not
/// with the packet: <c>GetLastChangeTime</c> returns <c>GetSimulationTime()</c> for every
/// simulation-latched variable, origin and angles among them (<c>c_baseentity.cpp:2806</c>). This
/// project stamped both with the packet tick, and on a SourceTV recording a player's simulation
/// tick differs from the packet's by four ticks on exactly half its updates — 60 ms of jitter on
/// the fastest things on screen, shared by nothing else (B273).
///
/// **The list is still keyed by arrival and that is deliberate.** Keying it by the applied time was
/// tried first: an entity that does not simulate keeps one simulation time for minutes, so every
/// state change it made collapsed onto a single tick, and `NoDrawTrackTests` caught an entity that
/// was hidden and never handed back. A pose carries more than the interpolated quantities.
/// </remarks>
public sealed class AppliedTimeTests
{
    [Test]
    public void At_AKeyframeThatAppliedBeforeItArrived_InterpolatesFromTheEarlierTime()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f }, appliedAt: 0);

        // Arrived at 20, but the entity says it applied at 10 — so by the time anything samples
        // between them, the whole move is already over.
        track.Add(20, new ScenePose { X = 100f }, appliedAt: 10);

        // **Sampled at 20, which is two conditions at once.** The keyframe must have ARRIVED — a
        // client cannot be pulled toward an update it has not received, which is the causality rule
        // `At` applies against the arrival tick — and the drawn moment, an interpolation delay
        // behind at tick 12, must be past the APPLIED time of 10.
        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBe(100f, "the value applied at tick 10 and the drawn moment is past it");
    }

    /// <remarks>
    /// **The control**: the same two keyframes with no lag must still be mid-move at that sample,
    /// because the value then applied at 20 rather than 10. Without this the test above passes
    /// against code that ignores the applied time and simply clamps early.
    /// </remarks>
    [Test]
    public void At_TheSameKeyframesWithNoLag_AreStillPartWayThrough()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f });
        track.Add(20, new ScenePose { X = 100f });

        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBeGreaterThan(0f, "the same sample must be PART WAY when nothing lagged");
        at.X.ShouldBeLessThan(100f);
    }

    /// <remarks>
    /// **The engine keeps a SEPARATE history per variable, stamped with its own clock** (B274).
    /// <c>GetLastChangeTime</c> returns <c>GetAnimTime()</c> for <c>LATCH_ANIMATION_VAR</c> — which
    /// for this project is exactly the cycle and the pose parameters — where the simulation clock
    /// serves origin and angles. Measured on the 2013 SourceTV foundry recording, the two disagree
    /// by more than eight ticks on 95.5% of the updates that carry both, so one clock cannot stand
    /// in for the other.
    ///
    /// Here the position finished moving at tick 10 while the animation did not reach its next
    /// stated cycle until 50. Sampling between them must show the move complete and the cycle
    /// part-way.
    /// </remarks>
    [Test]
    public void At_WhenTheTwoClocksDisagree_EachFieldFollowsItsOwn()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");

        track.Add(
            0, new ScenePose { X = 0f, Cycle = 0f }, appliedAt: 0, animationAppliedAt: 0);

        // **A cycle gap under half, deliberately.** `LoopingLerp` reads half a cycle or more as an
        // animation that wrapped past 1, so a fixture running 0 to 1 would be asserting the wrap
        // rather than the clock — and lands back on 0 however the interpolation is timed.
        track.Add(
            20,
            new ScenePose { X = 100f, Cycle = 0.4f },
            appliedAt: 10,
            animationAppliedAt: 50);

        // Drawn at 12: past the position's applied time of 10, less than a quarter of the way
        // through the animation's span of 0 to 50.
        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBe(100f, "the position applied at tick 10 and the drawn moment is past it");

        at.Cycle.ShouldBeGreaterThan(0f);
        at.Cycle.ShouldBeLessThan(
            0.2f, "the cycle is stated at tick 50 and the drawn moment is 12");
    }

    /// <remarks>
    /// **The control**: with both clocks equal, the cycle must reach the same place the position
    /// does. Without it the test above passes against code that simply holds the cycle back.
    /// </remarks>
    [Test]
    public void At_WhenTheTwoClocksAgree_TheCycleFollowsThePosition()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/door.mdl");

        track.Add(0, new ScenePose { X = 0f, Cycle = 0f }, appliedAt: 0, animationAppliedAt: 0);

        track.Add(
            20,
            new ScenePose { X = 100f, Cycle = 0.4f },
            appliedAt: 10,
            animationAppliedAt: 10);

        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBe(100f);
        at.Cycle.ShouldBe(0.4f, 1e-4f, "both clocks say tick 10, so the cycle arrived too");
    }

    /// <remarks>
    /// **A client-side-animated entity's cycle is never interpolated, and the engine enforces that
    /// structurally** (B276). `C_BaseAnimating::AddBaseAnimatingInterpolatedVars`
    /// (`c_baseanimating.cpp:887`):
    ///
    /// <code>
    /// int flags = LATCH_ANIMATION_VAR;
    /// if ( m_bClientSideAnimation )
    ///     flags |= EXCLUDE_AUTO_INTERPOLATE;
    /// AddVar( &amp;m_flCycle, &amp;m_iv_flCycle, flags, true );
    /// </code>
    ///
    /// and `AddVar` puts an `EXCLUDE_AUTO_INTERPOLATE` variable at the TAIL of the map, past
    /// `m_nInterpolatedEntries` — which is the bound `Interp_Interpolate` loops to, with
    /// `Assert( !( watcher->GetType() &amp; EXCLUDE_AUTO_INTERPOLATE ) )` inside it
    /// (`c_baseentity.cpp:6405`, `:875`). The client owns that cycle: it advances it every frame
    /// and treats what the wire says as a correction.
    ///
    /// This project interpolated it for every entity. It went unnoticed while the cycle was blended
    /// on the same pair as the position — wrong but smooth — and B274 made it visible by giving the
    /// cycle its own clock and its own fraction, which for a viewmodel is a different pair again.
    /// The owner saw it as viewmodel animation stopping.
    /// </remarks>
    [Test]
    public void At_ForAClientSideAnimatedEntity_DoesNotInterpolateTheCycle()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/weapons/v_rocketlauncher.mdl")
        {
            ClientSideAnimated = true,
        };

        track.Add(0, new ScenePose { X = 0f, Cycle = 0f });
        track.Add(20, new ScenePose { X = 100f, Cycle = 0.4f });

        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBeGreaterThan(0f, "the POSITION is still interpolated");
        at.X.ShouldBeLessThan(100f);

        at.Cycle.ShouldBe(
            0f,
            "the client owns this cycle and advances it itself; the wire value is a correction, " +
            "and blending two corrections invents a third the engine never had");
    }

    /// <remarks>
    /// **The spline's third sample is chosen on the CHANGETIME gap, not on arrival** (B278).
    /// `CInterpolatedVarArrayBase::GetInterpolationInfo` (`interpolatedvar.h:851`):
    ///
    /// <code>
    /// if ( !(m_fType &amp; INTERPOLATE_LINEAR_ONLY) &amp;&amp; varHistory.IsIdxValid(oldestindex) )
    /// {
    ///     pInfo->oldest = oldestindex;
    ///     float dt2 = older_change_time - oldest_change_time;
    ///     if ( dt2 > 0.0001f )
    ///         pInfo->m_bHermite = true;
    /// }
    /// </code>
    ///
    /// so a third entry that shares a changetime with the second gives LINEAR, not a spline.
    ///
    /// **B273 broke this by moving the interpolation onto applied times and leaving `Renormalise`
    /// measuring arrivals.** Two keyframes can now carry the same applied time — an entity that did
    /// not re-simulate between two packets — and the old check saw a positive arrival gap and
    /// splined through a zero-length interval, on a clock the rest of the arithmetic had left.
    /// </remarks>
    [Test]
    public void At_WhenTheThirdSampleSharesAnAppliedTime_DoesNotSpline()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        // Two arrivals, one applied time: the entity did not simulate between them.
        track.Add(0, new ScenePose { X = 0f }, appliedAt: 0);
        track.Add(10, new ScenePose { X = 10f }, appliedAt: 0);
        track.Add(20, new ScenePose { X = 100f }, appliedAt: 20);

        // **Sampled so the drawn moment lands BETWEEN the second and third**, which is the only
        // condition where the third sample is consulted at all. The first version of this asked at
        // tick 28, whose drawn moment of 20 is ON the last keyframe — so it returned early and
        // passed without ever reaching the spline.
        ScenePose at = track.At(23d)!.Value;

        // Linear from 10 to 100 over the applied span 0 to 20, three quarters through: 77.5.
        // Hermite through an older interval of zero length does not land there.
        at.X.ShouldBe(77.5f, 0.01f);
    }

    /// <remarks>
    /// **The model scale is not an interpolated variable at all** (B277). The engine's whole
    /// interpolated list is what `AddVar` registers — origin, angles, eye angles, velocity, view
    /// offset, punch, cycle, pose parameters, encoded controllers, flex weights, lean, shift, ragdoll
    /// position, the overlay layers — and `m_flModelScale` is not among them. A networked scale
    /// change SNAPS.
    ///
    /// **The client does ramp it, and that is a different mechanism entirely.**
    /// `C_BaseAnimating::SetModelScale( scale, change_duration )` (`c_baseanimating.cpp:6140`)
    /// creates a `MODELSCALE` data object holding a start, a goal and two times, and
    /// `UpdateModelScale` lerps between them — but that object exists only when game code asks for
    /// a duration. Receiving a new value over the wire assigns `m_flModelScale` directly.
    ///
    /// So blending it is inventing a ramp the recording never contained, which is the same shape as
    /// B276 one field along.
    /// </remarks>
    [Test]
    public void At_TheModelScale_IsNotInterpolated()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry1.mdl");

        track.Add(0, new ScenePose { X = 0f, Scale = 1f });
        track.Add(20, new ScenePose { X = 100f, Scale = 0.75f });

        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBeGreaterThan(0f, "the POSITION is interpolated");
        at.X.ShouldBeLessThan(100f);

        at.Scale.ShouldBe(
            1f,
            "m_flModelScale has no interpolator; a networked change snaps, and the only ramp the " +
            "client has is a MODELSCALE data object game code creates with an explicit duration");
    }

    /// <remarks>
    /// **The control**: an entity the SERVER animates — a door, a building — takes its cycle off
    /// the wire as an ordinary interpolated variable, and must still be blended. Without this the
    /// test above passes against code that stopped interpolating the cycle for everything.
    /// </remarks>
    [Test]
    public void At_ForAServerAnimatedEntity_StillInterpolatesTheCycle()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door_slide_door.mdl");

        track.Add(0, new ScenePose { X = 0f, Cycle = 0f });
        track.Add(20, new ScenePose { X = 100f, Cycle = 0.4f });

        ScenePose at = track.At(20d)!.Value;

        at.Cycle.ShouldBeGreaterThan(0f);
        at.Cycle.ShouldBeLessThan(0.4f);
    }

    /// <remarks>
    /// **A repeat records the applied time of the RESTATEMENT**, which is what keeps the hold
    /// interval on the same clock as the endpoints. Mixing an arrival tick into that fraction is
    /// the class of defect this whole entry is about, one level down.
    /// </remarks>
    [Test]
    public void At_ARepeatedPose_HoldsUntilTheRestatementsAppliedTime()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f }, appliedAt: 0);

        // Restated unchanged, arriving at 40 and applying at 30: the entity was still there then.
        track.Add(40, new ScenePose { X = 0f }, appliedAt: 30);
        track.Add(50, new ScenePose { X = 100f }, appliedAt: 50);

        // Drawn at 42, between the restatement's applied time of 30 and the move's of 50.
        ScenePose at = track.At(50d)!.Value;

        // Without the hold record the fraction would run from tick 0 and read 80 units along;
        // from the restatement it is halfway, and the spline puts it near the middle.
        at.X.ShouldBeGreaterThan(20f);
        at.X.ShouldBeLessThan(80f);
    }
}
