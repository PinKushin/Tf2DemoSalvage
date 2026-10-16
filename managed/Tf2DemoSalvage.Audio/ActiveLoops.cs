using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
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
///
/// **One entry per engine channel, in start order** (B545). `SND_PickDynamicChannel` (engine.dll FUN_18002a330)
/// hands a named channel's busy slot back, so a new loop there replaces the old, and gives every CHAN_AUTO start a
/// channel of its own, so two CHAN_AUTO loops on one entity are two entries. CHAN_STATIC takes a fresh slot too
/// (B415). `S_AlterChannel` acts on the first channel that matches, so lookups walk in start order.
/// </remarks>
public sealed class ActiveLoops
{
    private const int AutoChannel = 0;
    private const int StaticChannel = 6;

    /// <summary>What a tracked loop needs for its gain to be recomputed.</summary>
    /// <param name="Entity">The entity it plays on.</param>
    /// <param name="Channel">The channel it plays on.</param>
    /// <param name="Volume">The sound's own volume, before distance.</param>
    /// <param name="SoundLevel">Its <c>SNDLVL</c>, which decides how far it carries.</param>
    /// <param name="X">Where it is, in world units.</param>
    /// <param name="Y">Where it is.</param>
    /// <param name="Z">Where it is.</param>
    /// <param name="Name">What it plays, which `S_AlterChannel` matches as well as the channel.</param>
    /// <param name="IsMusic"><see cref="SoundGain.IsMusic"/>, decided once at the start as the engine sets its bit once.</param>
    /// <param name="Voice">The sink's handle for the voice, or 0 to gain it by entity and channel.</param>
    private readonly record struct Loop(
        int Entity,
        int Channel,
        float Volume,
        int SoundLevel,
        float X,
        float Y,
        float Z,
        string Name,
        bool IsMusic,
        int Voice);

    private readonly List<Loop> _loops = [];

    /// <summary>How many loops are being followed.</summary>
    public int Count => _loops.Count;

    /// <summary>Starts following a looping sound gained by its entity and channel.</summary>
    /// <param name="sound">The sound, as the timeline recorded it.</param>
    public void Track(SceneSound sound) => Track(sound, 0);

    /// <summary>Starts following a looping sound the sink started as one voice, so its gain reaches that voice only.</summary>
    /// <param name="sound">The sound, as the timeline recorded it.</param>
    /// <param name="voice">The handle <see cref="IAudioSink.Play"/> returned, or 0.</param>
    /// <remarks>
    /// A named channel replaces what it held. Without a handle a CHAN_AUTO or CHAN_STATIC loop cannot be told from
    /// the one before it either, so it replaces too.
    /// </remarks>
    public void Track(SceneSound sound, int voice)
    {
        if (voice == 0 || (sound.Channel != AutoChannel && sound.Channel != StaticChannel))
        {
            _loops.RemoveAll(loop => loop.Entity == sound.EntityIndex && loop.Channel == sound.Channel);
        }

        _loops.Add(new Loop(
            sound.EntityIndex,
            sound.Channel,
            sound.Volume,
            sound.SoundLevel,
            sound.OriginX,
            sound.OriginY,
            sound.OriginZ,
            sound.Name,
            SoundGain.IsMusic(sound.Name, sound.SoundLevel),
            voice));
    }

    /// <summary>`S_AlterChannel`: finds the first loop playing a sound on its entity's channel, and takes its new volume if it changes one.</summary>
    /// <param name="sound">A sound carrying `SND_CHANGE_VOL` or `SND_CHANGE_PITCH`.</param>
    /// <param name="voice">The matched loop's sink handle, or 0 when it is gained by key.</param>
    /// <returns>Whether that loop was playing — when it was not, the engine starts the sound instead.</returns>
    public bool Alter(SceneSound sound, out int voice)
    {
        int index = First(sound.EntityIndex, sound.Channel, sound.Name);
        voice = 0;

        if (index < 0)
        {
            return false;
        }

        if (sound.ChangesVolume)
        {
            _loops[index] = _loops[index] with { Volume = sound.Volume };
        }

        voice = _loops[index].Voice;
        return true;
    }

    /// <summary>What the first loop on an entity's channel is attenuated to from a listener, or null when none plays there.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="listener">The listener, in world units.</param>
    /// <param name="musicVolume">`snd_musicvolume`, for a music loop (<see cref="SoundGain.Music"/>, B545).</param>
    /// <returns>The gain.</returns>
    public float? GainAt(int entity, int channel, (float X, float Y, float Z) listener, float musicVolume)
    {
        int index = _loops.FindIndex(loop => loop.Entity == entity && loop.Channel == channel);

        return index < 0 ? null : GainOf(_loops[index], listener, musicVolume);
    }

    /// <summary>What the loop on a voice is attenuated to from a listener, or null when none is followed there.</summary>
    /// <param name="voice">The sink's handle.</param>
    /// <param name="listener">The listener, in world units.</param>
    /// <param name="musicVolume">`snd_musicvolume`.</param>
    /// <returns>The gain.</returns>
    public float? GainOfVoice(int voice, (float X, float Y, float Z) listener, float musicVolume)
    {
        int index = voice == 0 ? -1 : _loops.FindIndex(loop => loop.Voice == voice);

        return index < 0 ? null : GainOf(_loops[index], listener, musicVolume);
    }

    private static float GainOf(Loop loop, (float X, float Y, float Z) listener, float musicVolume)
    {
        float dx = loop.X - listener.X;
        float dy = loop.Y - listener.Y;
        float dz = loop.Z - listener.Z;

        return loop.Volume * SoundGain.AtDistance(loop.SoundLevel, MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz))) *
               SoundGain.Music(loop.IsMusic, musicVolume);
    }

    /// <summary>Stops following whatever was on a channel.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>Whether anything was being followed there.</returns>
    public bool Forget(int entity, int channel) =>
        _loops.RemoveAll(loop => loop.Entity == entity && loop.Channel == channel) > 0;

    /// <summary>Stops following the first loop on a channel playing the named sound — a `SND_STOP` naming it (B416).</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="name">The sound the stop names.</param>
    /// <returns>Whether that loop was being followed.</returns>
    public bool Forget(int entity, int channel, string name)
    {
        int index = First(entity, channel, name);

        if (index < 0)
        {
            return false;
        }

        _loops.RemoveAt(index);
        return true;
    }

    /// <summary>Stops following the loop on one voice — one the sink says has stopped or been replaced.</summary>
    /// <param name="voice">The sink's handle.</param>
    /// <returns>Whether that voice was being followed.</returns>
    public bool ForgetVoice(int voice) => voice != 0 && _loops.RemoveAll(loop => loop.Voice == voice) > 0;

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
    /// <param name="musicVolume">`snd_musicvolume`, for a music loop (<see cref="SoundGain.Music"/>, B545).</param>
    /// <returns>One entry per tracked loop, in start order, naming its channel, its voice and its gain.</returns>
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
    public IReadOnlyList<(int Entity, int Channel, int Voice, float Gain)> GainsAt(float x, float y, float z, float musicVolume)
    {
        List<(int Entity, int Channel, int Voice, float Gain)> gains = new(_loops.Count);

        foreach (Loop loop in _loops)
        {
            gains.Add((loop.Entity, loop.Channel, loop.Voice, GainOf(loop, (x, y, z), musicVolume)));
        }

        return gains;
    }

    private int First(int entity, int channel, string name) =>
        _loops.FindIndex(loop => loop.Entity == entity && loop.Channel == channel && SoundName.SameSfx(loop.Name, name));
}
