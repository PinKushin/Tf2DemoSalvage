using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// When a corpse stops being drawn — <c>C_TFRagdoll::ClientThink</c>'s fade, per corpse (B315).
/// </summary>
/// <remarks>
/// **The rule is not "a corpse lasts <c>cl_ragdoll_fade_time</c>", and reading only where the timer
/// is set says that it is.** `CreateTFRagdoll` ends with
/// <c>StartFadeOut( cl_ragdoll_fade_time.GetFloat() )</c> (`c_tf_player.cpp:869`), the convar
/// defaults to 15, and that looks like the answer. The think is where it lives:
///
/// <code>
/// if ( IsRagdollVisible() )
/// {
///     …
///     StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f );
///     return;
/// }
///
/// if ( m_fDeathTime &lt; gpGlobals-&gt;curtime ) { EndFadeOut(); return; }
/// </code>
///
/// `c_tf_player.cpp:1532-1553`, with `StartFadeOut` at `:1624` writing
/// <c>m_fDeathTime = gpGlobals-&gt;curtime + fDelay</c>. Read-from-source. The timer is re-armed at a
/// THIRD of the convar on every think the corpse is visible, and the branch RETURNS before the
/// expiry test — so a corpse being looked at never expires, and one that has left view goes 4.95
/// seconds later.
///
/// **Why the viewer needs this and not merely the entity's lifetime.** The server keeps one ragdoll
/// per player and removes it only when that player next dies (`UTIL_Remove`,
/// `tf_player.cpp:15602`), so an entity-lifetime window puts far more bodies on the map than TF2
/// ever shows: 57 simultaneously undeleted against a twelve-player roster, measured on
/// `serveme-627619-stv-2026-08-07` with the `corpses` probe.
///
/// **Visibility is asked of the previous frame, which is the engine's own arrangement rather than
/// an approximation of it** — the same argument `EntityModels.PosedEntities` already makes for the
/// interpolation list: `IsVisible()` reports the last render, and the cull runs after the view while
/// sampling runs before it.
/// </remarks>
/// <param name="intervalPerTick">
/// Seconds per tick, so a corpse's creation can be placed on the playback clock.
/// </param>
public sealed class RagdollFade(float intervalPerTick)
{
    /// <summary>
    /// How long a corpse nobody ever looks at lasts — <c>cl_ragdoll_fade_time</c>'s default.
    /// </summary>
    /// <remarks>
    /// `ConVar cl_ragdoll_fade_time( "cl_ragdoll_fade_time", "15", FCVAR_CLIENTDLL );`,
    /// `c_tf_player.cpp:514`. A default rather than a constant
    /// (`docs/memory/a-default-is-not-a-constant.md`) — a player may set it, and a demo records
    /// nothing about what theirs was, so the default is the only defensible value.
    /// </remarks>
    public const float NeverSeenSeconds = 15f;

    /// <summary>How long a corpse lasts after leaving view.</summary>
    /// <remarks>
    /// **`* 0.33f`, carried rather than rounded to 5.** 15 × 0.33 is 4.95. Writing 5 would be
    /// tidying up an engine constant, which is the same class of change as rounding a clamp — and
    /// the multiplication is what the engine writes, twice (`:1537` and `:1544`).
    /// </remarks>
    public const float AfterLeavingViewSeconds = NeverSeenSeconds * 0.33f;

    /// <summary>Seconds per tick, as the recording server ran.</summary>
    /// <remarks>
    /// Published so a caller working in ticks can reach the playback clock without holding a second
    /// copy of the rate — <c>docs/memory/one-camera-or-the-cull-lies.md</c> is the same rule about a
    /// value derived twice.
    /// </remarks>
    public float IntervalPerTick => intervalPerTick;

    /// <summary>Whether this corpse has expired and must not be drawn.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <param name="seconds">Playback time now.</param>
    /// <param name="visible">Whether it was visible on the previous frame.</param>
    /// <returns>True once it is gone, and it does not come back.</returns>
    /// <remarks>
    /// **Expiry is permanent because `EndFadeOut` destroys the entity** — `ClearRagdoll`,
    /// `SetRenderMode( kRenderNone )`, `DestroyBoneAttachments` (`:1634-1640`). A corpse that
    /// returned when the camera turned back towards it would flicker every time a viewer panned
    /// across a spot where somebody had died. Only <see cref="Rewound"/> brings one back, and only
    /// because this project can seek where the client could not (D131).
    /// </remarks>
    public bool Gone(SceneRagdoll corpse, double seconds, bool visible)
    {
        (int, int) key = (corpse.EntityIndex, corpse.Serial);

        if (_expired.Contains(key))
        {
            return true;
        }

        // **`StartFadeOut( cl_ragdoll_fade_time.GetFloat() )` at the end of `CreateTFRagdoll`, so
        // the clock starts when the corpse was CREATED and not when this viewer first asked about
        // it.** Seeding it from the first call instead would hand a fresh fifteen seconds to every
        // corpse the moment you scrubbed to it, which is a thing the engine cannot do and would
        // make a body's lifetime depend on where the viewer had been looking.
        if (!_deathTimes.TryGetValue(key, out double deathTime))
        {
            deathTime = (corpse.FirstTick * intervalPerTick) + NeverSeenSeconds;
            _deathTimes[key] = deathTime;
        }

        if (visible)
        {
            _deathTimes[key] = seconds + AfterLeavingViewSeconds;

            // **The engine's `return`, and it is the whole reason a watched corpse persists.**
            // Removing it does NOT redden `Gone_ForACorpseWatchedThroughout_IsNeverTrue` — watching
            // from before the timer expires re-arms it ahead of the clock on every call, so the
            // stale check never fires. `Gone_ForACorpseFirstSeenAfterItsUnseenTimer_IsFalse` is the
            // one that fails, and it is the only one that does.
            return false;
        }

        if (deathTime >= seconds)
        {
            return false;
        }

        _expired.Add(key);

        return true;
    }

    /// <summary>Forgets every corpse, because the clock jumped backwards.</summary>
    /// <remarks>
    /// **The one place this and the engine part company, and D131 names why**: state that survives
    /// across frames is wrong the moment the clock runs backwards, which is a case the client does
    /// not have. `DemoTimeline.PropsAt` rebuilds everything on a rewind for the same reason. Without
    /// it, scrubbing back past a death shows a map missing every body it showed the first time.
    /// </remarks>
    public void Rewound()
    {
        _deathTimes.Clear();
        _expired.Clear();
    }

    /// <summary>
    /// When each corpse expires, keyed by slot AND serial.
    /// </summary>
    /// <remarks>
    /// **The serial is not decoration.** Corpses reuse entity slots briskly, so a key of the index
    /// alone would hand a fresh corpse its predecessor's timer — and, once that had run, expire it
    /// on the frame it appeared.
    /// </remarks>
    private readonly Dictionary<(int Index, int Serial), double> _deathTimes = [];

    private readonly HashSet<(int Index, int Serial)> _expired = [];
}
