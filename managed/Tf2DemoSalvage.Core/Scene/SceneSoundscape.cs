using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Which soundscape a listener is standing in, and where its sounds are placed.</summary>
/// <param name="Index">
/// The position in the client's soundscape list, from <c>m_audio.soundscapeIndex</c>. **-1 is the
/// engine's "none"** — <c>CEnvSoundscape</c> starts there (<c>soundscape.cpp:105</c>) — and is a
/// real value rather than an absence.
/// </param>
/// <param name="PositionBits">
/// <c>m_audio.localBits</c>: "if bits 0,1,2,3 are set then position 0,1,2,3 are valid/used". A slot
/// that is not set is unused, which is a different thing from a slot deliberately at the origin.
/// </param>
/// <param name="Positions">
/// The eight <c>m_audio.localSound</c> slots, in order. A soundscape's <c>"position" "3"</c> names
/// slot three of these — which is how ONE soundscape covers a whole map: `Gorge.Inside` places
/// seven copies of two machine hums at slots 0 to 6.
/// </param>
/// <param name="EntityIndex">
/// <c>m_audio.entIndex</c>, the <c>env_soundscape</c> that set it. Kept because it is what the
/// engine's own `soundscape_dumpclient` prints, so a capture from a running client can be compared
/// against a decode of the same moment.
/// </param>
/// <remarks>
/// **This is private per-player data and most demos do not have it.** `m_audio` sits in `DT_Local`,
/// which reaches the wire through
/// `SendPropDataTable( "localdata", 0, DT_LocalPlayerExclusive, SendProxy_SendLocalDataTable )`
/// (<c>player.cpp:8199</c>), and that proxy is `pRecipients->SetOnly( objectID - 1 )` — one
/// recipient, the player who owns the entity.
///
/// So a point-of-view recording carries the recorder's soundscape and a SourceTV recording carries
/// nobody's. That is a fact about the format rather than a limitation of this reader, and it is why
/// a SourceTV demo needs the map's own `env_soundscape` entities instead (B173).
/// </remarks>
public readonly record struct SceneSoundscape(
    int Index,
    int PositionBits,
    IReadOnlyList<(float X, float Y, float Z)?> Positions,
    int EntityIndex)
{
    /// <summary>Whether a position slot carries a value the engine considers used.</summary>
    /// <param name="slot">0 to 7.</param>
    /// <returns>Whether <c>localBits</c> marks it valid.</returns>
    public bool HasPosition(int slot) =>
        slot is >= 0 and < 8 && (PositionBits & (1 << slot)) != 0;

    /// <inheritdoc />
    /// <remarks>
    /// **A record struct holding a list compares that list by REFERENCE**, so the generated equality
    /// would call two identical samples different and record a keyframe every tick. The sampler
    /// stores only on change, so this is load-bearing rather than tidiness.
    /// </remarks>
    public bool Equals(SceneSoundscape other) =>
        Index == other.Index &&
        PositionBits == other.PositionBits &&
        EntityIndex == other.EntityIndex &&
        Positions.SequenceEqual(other.Positions);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, PositionBits, EntityIndex);
}
