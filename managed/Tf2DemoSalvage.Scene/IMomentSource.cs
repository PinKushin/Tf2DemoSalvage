using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where the players and props at a moment come from.</summary>
/// <remarks>
/// **The third of these, and deliberately the same shape as the other two.** `IEyeSource` supplies
/// the first-person camera and `IViewmodelSource` the held weapon; both are implemented over a
/// `DemoTimeline` and set when a demo opens. This one supplies the scene's contents.
///
/// **It exists because a `DemoTimeline` cannot be constructed in a test.** Its constructor is
/// private and `Build` takes the bytes of a real file, so anything that samples one directly can
/// only be exercised by shipping a demo into the test project — which is why the sampling sat
/// untested inside `MainForm.ShowMoment` for as long as it did.
///
/// **Buffers are passed in rather than returned**, matching `DemoTimeline`'s own signatures: a
/// moment is rebuilt every frame while playing, and returning fresh lists would allocate two per
/// frame for the garbage collector to find again.
/// </remarks>
public interface IMomentSource
{
    /// <summary>Seconds per tick, as the recording server ran.</summary>
    /// <remarks>
    /// **A server setting rather than a constant.** A box left at its default runs 33 where a
    /// configured one runs 66, and interpolating at the wrong rate reads as a slow or fast server
    /// rather than as a defect.
    /// </remarks>
    public float IntervalPerTick { get; }

    /// <summary>Fills a buffer with the players at a moment, fraction included.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="into">The buffer to fill; cleared first.</param>
    public void PlayersAt(double tick, ICollection<ScenePlayer> into);

    /// <summary>Fills a buffer with the props at a moment, fraction included.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="into">The buffer to fill; cleared first.</param>
    /// <param name="viewEntity">
    /// <c>render-&gt;GetViewEntity()</c> — the one clause of <c>ShouldInterpolate</c> a recording cannot
    /// answer for itself (B385). The other three are the source's, because <c>IsVisible()</c> is
    /// leaf-system membership rather than anything the renderer decides.
    /// </param>
    public void PropsAt(
        double tick, ICollection<SceneProp> into, int? viewEntity = null);

    /// <summary>The round the game rules were in, or null when the demo does not say.</summary>
    /// <param name="tick">The moment being shown.</param>
    /// <returns><c>m_iRoundState</c>; <c>GR_STATE_TEAM_WIN</c> is 5.</returns>
    /// <remarks>
    /// **Asked because two drawing rules need it and neither can derive it.** A spawn's team wall
    /// is drawn to nobody once the round is won (<c>c_func_respawnroom.cpp:47</c>). Null is "the
    /// demo did not say" rather than a state — every pre-2009 era specimen carries no game rules
    /// entity at all.
    /// </remarks>
    public int? RoundStateAt(double tick);

    /// <summary>The player the demo was recorded from, or null for a SourceTV recording.</summary>
    /// <remarks>
    /// **Asked because the viewer of a demo IS its recorder, and one drawing rule turns on that**
    /// (B354). `CEconEntity::ShouldHideForVisionFilterFlags` (<c>econ_entity.cpp:1812</c>) hides a
    /// Pyroland item from anyone without Pyrovision, and the vision it asks about is the local
    /// player's — which during playback is whoever recorded the demo.
    ///
    /// **Not a tick function, unlike the round.** The recorder is a property of the file.
    /// </remarks>
    public int? Recorder { get; }

    /// <summary>Tells the source what an entity's model says about its pose parameters.</summary>
    /// <param name="entityIndex">The entity whose model has been resolved.</param>
    /// <param name="looping">Which of its pose parameters wrap, by index.</param>
    /// <remarks>
    /// **This is <c>C_BaseAnimating::OnNewModel</c>'s pose-parameter half** — the engine walks the
    /// studio header there and calls <c>m_iv_flPoseParameter.SetLooping( Pose.loop != 0.0f, i )</c>
    /// (<c>c_baseanimating.cpp:1130</c>), teaching the interpolator which elements may not be
    /// blended the short way. It has to be told rather than to look, because only the model knows
    /// and the interpolation happens where models cannot be opened.
    ///
    /// **The direction of the call is what makes it worth having on this interface.** Everything
    /// else here is the presenter ASKING the demo for a moment; this is the one fact that travels
    /// the other way, and it exists because a wrapping parameter interpolated the plain way sweeps
    /// the long way round — for a sentry crossing due south, 358 degrees backwards over an
    /// interpolation window.
    /// </remarks>
    /// <param name="staticProp">
    /// Whether the model carries <c>STUDIOHDR_FLAGS_STATIC_PROP</c>, which exempts it from the cycle
    /// history's reset on a new sequence (<c>c_baseanimating.cpp:4740</c>). It travels the same way and
    /// for the same reason: only the model says it.
    /// </param>
    public void OnNewModel(int entityIndex, IReadOnlyList<bool> looping, bool staticProp);
}

/// <summary>A demo's timeline, as a moment source.</summary>
/// <param name="timeline">The decoded demo.</param>
/// <remarks>
/// **The production implementation, and it is a pass-through by design** — the same shape as
/// `TimelineEyes` and `TimelineViewmodels`. Everything interesting is in `DemoTimeline`; this only
/// names which of its many questions the scene asks.
/// </remarks>
public sealed class TimelineMoments(DemoTimeline timeline) : IMomentSource
{
    /// <summary>
    /// Where the class table a corpse's model is derived from comes from, or null to draw none.
    /// </summary>
    /// <remarks>
    /// **A corpse is the one thing in the scene whose model is not in the demo** — `DT_TFRagdoll` is
    /// `NOBASE` and sends no model index, so the client derives the path from `m_iClass` through the
    /// game's own `scripts/playerclasses/*.txt` (`c_tf_player.cpp:689-696`). That needs the install,
    /// which `DemoTimeline` deliberately does not have.
    ///
    /// **Read per call rather than captured, because the install arrives on its own schedule.**
    /// `PlayerAppearances` exists for exactly this and already says why: *"Two settable properties,
    /// because there are genuinely two lifecycles"* — the demo can open before the archives or after
    /// them. Taking `Game.Classes` once, here, would cache whichever half happened to be missing at
    /// construction, and a demo opened first would show no corpses for its whole life.
    ///
    /// **Null draws nothing, and that is the honest answer rather than a fallback.** With no game
    /// folder there is no way to know which model a class wears — guessing `models/player/spy.mdl`
    /// from the class name would be this project inventing a path that the era it is decoding may
    /// not even use.
    /// </remarks>
    /// <remarks>
    /// **A supplier rather than the holder, which is Interface Segregation doing real work here.**
    /// What this needs is "a class table when one exists"; taking `PlayerAppearances` would drag in
    /// the weapon roles, the timeline and the log, and would make every test of the corpse wiring
    /// require a TF2 install to construct a `GameContent`. Production passes
    /// <c>() =&gt; appearances.Game?.Classes?.Model</c>, which is the same per-call read.
    /// </remarks>
    public Func<Func<int, string?>?>? ClassModels { get; set; }

    /// <summary>
    /// Where the econ item schema comes from, for the two wearable skips that need it.
    /// </summary>
    /// <remarks>
    /// **A supplier, and read per call, for the same reason <see cref="ClassModels"/> is** — the
    /// archives open on their own schedule and a demo opened first would otherwise keep a null
    /// schema for its whole life, quietly drawing cosmetics the engine drops.
    /// </remarks>
    public Func<ItemSchema?>? Items { get; set; }

    /// <summary>Which entities the cull accepted last frame, for the corpse fade only (B385).</summary>
    /// <remarks>
    /// **A different question from the interpolation list, and separating them is the point.** This was
    /// the same set — `MomentScene.PosedEntities` — serving both, and only one of the two ever wanted
    /// it. `ShouldInterpolate` asks `IsVisible()`, which is leaf-system membership and involves no
    /// frustum at all; the corpse fade asks `C_TFRagdoll::IsRagdollVisible`, which is
    /// <c>engine-&gt;IsBoxInViewCluster</c> and then <c>engine-&gt;CullBox</c> around a ±1 box at the
    /// corpse's origin (`c_tf_player.cpp:1350`). A frustum is exactly what that one needs, so it keeps
    /// getting one and the sampler stops getting one.
    ///
    /// **Still the PREVIOUS frame's cull, which the engine's is not** — `IsRagdollVisible` runs live in
    /// `ClientThink`. Filed as the remaining divergence in B385 rather than papered over: a corpse in
    /// its last second could expire one frame late.
    ///
    /// **A supplier read per call, for the same reason <see cref="ClassModels"/> is.** Null means no
    /// renderer has reported yet, and every corpse then fades on the long unseen timer, which is the
    /// answer a headless caller should get.
    /// </remarks>
    public Func<IReadOnlySet<int>?>? InView { get; set; }

    /// <summary>Where a model's <c>break</c> pieces come from — its gib list (B371).</summary>
    /// <remarks>
    /// **A supplier read per call, for the same reason <see cref="ClassModels"/> is**: the model
    /// carrying the list is opened on the archives' own schedule, so a source bound once would hold
    /// an empty answer for the life of the demo. Null until the renderer sets it, which is the
    /// state in which a gibbed corpse draws nothing at all.
    /// </remarks>
    public Func<string, IReadOnlyList<PhysicsBreakPiece>>? Gibs { get; set; }


    /// <inheritdoc />
    public float IntervalPerTick => timeline.IntervalPerTick;

    /// <inheritdoc />
    public void PlayersAt(double tick, ICollection<ScenePlayer> into) =>
        timeline.PlayersAt(tick, into);

    /// <inheritdoc />
    public void PropsAt(
        double tick, ICollection<SceneProp> into, int? viewEntity = null)
    {
        timeline.PropsAt(tick, into, viewEntity);

        // **A rewind forgets every corpse's timer**, for the reason `DemoTimeline.PropsAt`
        // rebuilds its whole sample on one: state carried across frames is wrong the moment the
        // clock runs backwards, and a corpse expired on the way forward would otherwise be
        // missing from a scrub back past its own death (D131).
        bool rewound = tick < _lastTick;

        // **Recorded whether or not there are corpses to fade** (B386). It was assigned inside the
        // `ClassModels` branch, so a caller without a class list left it at zero for ever — and
        // `OnNewModel` now uses it to say WHICH occupant of a reused slot the model resolved for.
        // A tick that never advances would send every stamp to whichever track happens to be alive
        // at zero, silently.
        _lastTick = tick;

        // **After, because `PropsAt` clears the buffer first.** Corpses are not prop tracks and
        // never reach that walk — see `RagdollProps` for why the layering puts them here.
        if (ClassModels?.Invoke() is { } classes)
        {
            if (rewound)
            {
                _fade.Rewound();
            }

            // **`interpolate` IS the previous frame's visible set** — the interface says so, and
            // both uses come from the same place in the engine: `g_InterpolationList` membership
            // and `IsRagdollVisible` are each gated on what the last render could see. Reusing it
            // is not a shortcut but the avoidance of a second copy that the presenter would have to
            // remember to set, which is precisely the wiring this project has shipped unset three
            // times. Null is the first frame, where treating every corpse as unseen is right: their
            // timers have only just started.
            RagdollProps.Fill(
                timeline.Corpses,
                tick,
                classes,
                into,
                _fade,
                InView?.Invoke(),
                Items?.Invoke(),
                Gibs,
                timeline.IntervalPerTick);
        }
    }

    /// <inheritdoc />
    public int? RoundStateAt(double tick) => timeline.RoundStateAt(tick);

    /// <inheritdoc />
    public int? Recorder => timeline.RecorderEntityIndex;

    /// <inheritdoc />
    public void OnNewModel(int entityIndex, IReadOnlyList<bool> looping, bool staticProp)
    {
        // **The occupant of that slot at the frame being drawn, not whichever was written last**
        // (B386). An edict index does not name a track: the engine recycles slots, so
        // `TrackFor(entityIndex)` hands back the LAST object to hold the index and a model resolving
        // mid-match stamped a track that will not exist for another twenty thousand ticks. The same
        // lookup produced B375's invisible rocket trails and B386's probe reporting a player for a
        // door — it fails by answering, never by throwing.
        //
        // **No fallback to the index-only lookup when nothing is alive.** A model resolves during a
        // frame, so if no occupant of the slot covers that frame then nothing of that entity is being
        // drawn and there is no track this belongs to — a guess would be the failure again with an
        // extra step (`docs/memory/fallbacks-do-not-make-guesses-safe.md`).
        if (timeline.TrackFor(entityIndex, _lastTick) is { } track)
        {
            // **`_lastTick` is the engine's `curtime` here**, and a resize seeds its replacement entries
            // with it: `SetMaxCount` wipes and `Reset()` stamps `gpGlobals->curtime`
            // (`interpolatedvar.h:749`). A model resolves during a frame, so the frame being drawn is the
            // moment it happened.
            track.OnNewModel(looping, (int)Math.Floor(_lastTick), staticProp);
        }
    }

    /// <summary>When each corpse expires — <c>C_TFRagdoll::ClientThink</c>'s rule.</summary>
    private readonly RagdollFade _fade = new(timeline.IntervalPerTick);

    /// <summary>The last tick asked for, so a rewind can be noticed.</summary>
    private double _lastTick;
}
