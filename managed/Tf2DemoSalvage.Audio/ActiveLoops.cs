using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// The looping sounds currently playing, and what each should be attenuated to from here.
/// </summary>
/// <remarks>
/// **A one-shot is spatialised once and a loop cannot be (B169).** A gunshot lasts under a second,
/// so the gain computed when it started is still right when it ends. A map's ambience is started
/// once and runs for the whole match — the owner: *"the hum will come and go in game depending on
/// if you are in spawn or not"* — so a gain fixed at the moment it began is wrong for every moment
/// after. Six hums on cp_process, all started at the recording's first tick, were inaudible for
/// exactly that reason.
///
/// This remembers the few sounds that need re-attenuating and answers what they should be now. It
/// deliberately holds no device and no samples: given a listener position it produces numbers, which
/// is what makes it testable where a sound card is not available.
///
/// **Only loops are tracked, because only loops can be wrong.** Adding one-shots would mean walking
/// hundreds of entries a frame to recompute values that are about to stop mattering.
/// </remarks>
public sealed class ActiveLoops
{
    /// <summary>What a tracked loop needs for its gain to be recomputed.</summary>
    /// <param name="Volume">The sound's own volume, before distance.</param>
    /// <param name="SoundLevel">Its <c>SNDLVL</c>, which decides how far it carries.</param>
    /// <param name="X">Where it is, in world units.</param>
    /// <param name="Y">Where it is.</param>
    /// <param name="Z">Where it is.</param>
    private readonly record struct Loop(float Volume, int SoundLevel, float X, float Y, float Z);

    /// <summary>Keyed the way the engine keys a playing sound: one per entity per channel.</summary>
    private readonly Dictionary<(int Entity, int Channel), Loop> _loops = [];

    /// <summary>How many loops are being followed.</summary>
    public int Count => _loops.Count;

    /// <summary>Starts following a looping sound.</summary>
    /// <param name="sound">The sound, as the timeline recorded it.</param>
    /// <remarks>
    /// **Replaces any loop already on that entity's channel**, which is the same rule the sink
    /// applies to the sound itself: a named channel holds one at a time, so the new one displaces
    /// the old rather than joining it.
    /// </remarks>
    public void Track(SceneSound sound)
    {
        _loops[(sound.EntityIndex, sound.Channel)] =
            new Loop(sound.Volume, sound.SoundLevel, sound.OriginX, sound.OriginY, sound.OriginZ);
    }

    /// <summary>Stops following whatever was on a channel.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>Whether anything was being followed there.</returns>
    public bool Forget(int entity, int channel) => _loops.Remove((entity, channel));

    /// <summary>Forgets everything, for a seek or a new demo.</summary>
    /// <remarks>
    /// A seek silences the sink, so the loops it was following are no longer playing. Keeping them
    /// would mean re-attenuating voices that no longer exist and, worse, never restarting the ones
    /// that should now be running.
    /// </remarks>
    public void Clear() => _loops.Clear();

    /// <summary>What every tracked loop should be attenuated to, from a listener's position.</summary>
    /// <param name="x">The listener, in world units.</param>
    /// <param name="y">The listener.</param>
    /// <param name="z">The listener.</param>
    /// <returns>One entry per tracked loop, naming its channel and its gain.</returns>
    /// <remarks>
    /// **Valve's curve, the same one the sound was started with.** <see cref="SoundGain.AtDistance"/>
    /// against the sound's own <c>SNDLVL</c>, times its volume — so a loop re-attenuated here and a
    /// one-shot spatialised at <c>Play</c> agree by construction rather than by two implementations
    /// happening to match.
    ///
    /// Returns a gain of zero rather than dropping the entry when a loop is out of range. The sound
    /// is still playing and still needs to be told it is inaudible; omitting it would leave the sink
    /// holding the last audible value, which is the bug this type exists to fix.
    /// </remarks>
    public IEnumerable<(int Entity, int Channel, float Gain)> GainsAt(float x, float y, float z)
    {
        foreach (((int entity, int channel), Loop loop) in _loops)
        {
            float dx = loop.X - x;
            float dy = loop.Y - y;
            float dz = loop.Z - z;

            float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            yield return (entity, channel, loop.Volume * SoundGain.AtDistance(loop.SoundLevel, distance));
        }
    }
}
