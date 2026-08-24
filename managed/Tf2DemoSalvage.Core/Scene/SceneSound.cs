namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One sound the recording plays, and when.</summary>
/// <param name="Tick">The tick the server sent it on.</param>
/// <param name="Name">
/// The resolved <c>soundprecache</c> entry, or empty when the number named nothing. Empty rather
/// than null because an unresolved sound is still a sound that happened — dropping it would make
/// "we cannot name it" indistinguishable from "the demo was silent here".
/// </param>
/// <param name="SoundNumber">The index as it arrived, kept so an unresolved one can be reported.</param>
/// <param name="EntityIndex">Which entity made it, which is where it is heard from.</param>
/// <param name="Channel">
/// <c>CHAN_*</c> — a channel plays one sound at a time, so a new one on a busy channel replaces
/// what was there. That is how a player's voice line cuts off their previous one.
/// </param>
/// <param name="Volume">0..1 as the server sent it, before attenuation.</param>
/// <param name="SoundLevel">
/// <c>SNDLVL_*</c> in decibels, which is what decides how far it carries. Valve's own falloff reads
/// this rather than a radius.
/// </param>
/// <param name="Pitch">100 is unshifted; the engine sends a percentage.</param>
/// <param name="DelaySeconds">
/// How long after the tick it should start. Sent for sounds the server schedules ahead, and
/// negative for one already in progress when the listener arrived.
/// </param>
/// <param name="OriginX">Where it is in the world, when the sound carries its own position.</param>
/// <param name="OriginY">Where it is in the world.</param>
/// <param name="OriginZ">Where it is in the world.</param>
/// <param name="IsAmbient">Whether it plays at a fixed volume regardless of distance.</param>
/// <param name="IsStop">Whether this stops a playing sound rather than starting one.</param>
/// <param name="FromSignon">
/// Whether it arrived in the signon block rather than in a packet — the map's ambience, already
/// looping when the recording began.
/// <para>
/// **Its stated tick is not when it should be heard, and that is measured.** On
/// <c>movement-test-pov-cp_process</c> six <c>)ambient/machine_hum.wav</c> arrive from the signon
/// stamped tick 4654, while every packet sound runs from tick 30 upward — so the signon carries the
/// SERVER's clock at the moment recording started and the packets carry the recording's own. Played
/// at 4654 the map's hum would begin seventy seconds in, and sorting by the stated tick would put
/// the whole opening minute after it.
/// </para>
/// </param>
/// <remarks>
/// **A flat record per sound rather than a track per entity**, which is the opposite of how props
/// are carried and deliberately so. A prop persists and is interpolated between keyframes; a sound
/// is an instant — it happens at a tick and is then the player's business, not the timeline's.
/// Modelling it as state to be sampled would mean asking "what sound is entity 12 at tick 4000",
/// which is not a question the format can answer.
///
/// **Ordered by tick across the whole recording**, so playback walks forward with a cursor rather
/// than searching. Seeking backwards is a binary search on the same list.
/// </remarks>
public readonly record struct SceneSound(
    int Tick,
    string Name,
    int SoundNumber,
    int EntityIndex,
    int Channel,
    float Volume,
    int SoundLevel,
    int Pitch,
    float DelaySeconds,
    float OriginX,
    float OriginY,
    float OriginZ,
    bool IsAmbient = false,
    bool IsStop = false,
    bool FromSignon = false);
