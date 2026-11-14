using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// The sound channels currently playing, and what each should be attenuated and panned to from here.
/// </summary>
/// <remarks>
/// **A gain fixed at a sound's start is wrong for every moment after (B169).** A map's ambience is
/// started once and runs for the whole match — the owner: *"the hum will come and go in game depending
/// on if you are in spawn or not"* — and six hums on cp_process, started at the recording's first
/// tick, were inaudible for exactly that reason. The engine re-spatialises every channel every frame.
///
/// This remembers the channels and answers what they should be now. It deliberately holds no device
/// and no samples: given a listener it produces numbers, which is what makes it testable where a
/// sound card is not available.
///
/// **Every channel the sink hands a voice for, one-shots and music included** (B724): `SND_Spatialize` re-gains and
/// re-pans every channel every frame with its origin following its entity (<see cref="Follow"/>), and `S_AlterChannel`
/// matches any channel. A sink without handles cannot say when a one-shot ends, so there only loops are followed, by key.
/// The name is older than that.
///
/// **One entry per engine channel, in start order** (B545). `SND_PickDynamicChannel` (engine.dll FUN_18002a480)
/// hands a named channel's busy slot back, so a new loop there replaces the old, and gives every CHAN_AUTO start a
/// channel of its own, so two CHAN_AUTO loops on one entity are two entries. CHAN_STATIC takes a fresh slot too
/// (B415). `S_AlterChannel` acts on the first channel that matches, so lookups walk in start order.
/// </remarks>
public sealed class ActiveLoops
{
    private const int AutoChannel = SoundScript.AutoChannel;
    private const int StaticChannel = SoundScript.StaticChannel;

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
    /// <param name="Mono">Its starting mono weight (<see cref="SpeakerPan.Mono"/>), decided once at the start.</param>
    /// <param name="Radius">Its entity's model radius once the client has given one, else 0.</param>
    /// <param name="Speaker">Whether it carries `SND_SPEAKER`, which keeps it out of the sum (<see cref="SpeakerPan.Spatialize"/>).</param>
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
        int Voice,
        float Mono,
        float Radius = 0f,
        bool Speaker = false);

    private readonly List<Loop> _loops = [];

    /// <summary>What <see cref="GainsAt"/> answers in, every frame, so a frame allocates no list for it.</summary>
    private readonly List<(int Entity, int Channel, int Voice, float Gain)> _gains = [];

    /// <summary>What <see cref="PansAt"/> answers in, every frame, so a frame allocates no list for it.</summary>
    private readonly List<(int Voice, float Left, float Right)> _pans = [];

    /// <summary>How many loops are being followed.</summary>
    public int Count => _loops.Count;

    /// <summary>Starts following a looping sound gained by its entity and channel.</summary>
    /// <param name="sound">The sound, as the timeline recorded it.</param>
    public void Track(SceneSound sound) => Track(sound, 0);

    /// <summary>Starts following a looping sound the sink started as one voice, so its gain reaches that voice only.</summary>
    /// <param name="sound">The sound, as the timeline recorded it.</param>
    /// <param name="voice">The handle <see cref="IAudioSink.Play"/> returned, or 0.</param>
    /// <param name="stereo">Whether its wave has two channels, for its mono weight (<see cref="SpeakerPan.Mono"/>).</param>
    /// <remarks>
    /// A named channel replaces what it held. Without a handle a CHAN_AUTO or CHAN_STATIC loop cannot be told from
    /// the one before it either, so it replaces too.
    /// </remarks>
    /// <param name="radius">Its entity's model radius as the start found it, 0 for none yet.</param>
    public void Track(SceneSound sound, int voice, bool stereo = false, float radius = 0f)
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
            voice,
            SpeakerPan.Mono(sound.SoundLevel, sound.Name, stereo, SpeakerPan.IsLocalSource(sound.EntityIndex, ViewEntity)),
            radius,
            sound.ThroughSpeaker));
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

    /// <summary>The view entity, whose own sounds are spatialised ahead of the listener (<see cref="SpeakerPan.Spatialize"/>).</summary>
    public int? ViewEntity { get; set; }

    /// <summary>What a loop is this pass (<see cref="SpeakerPan.Traits"/>): local follows the view entity as it changes.</summary>
    private ChannelTraits TraitsOf(Loop loop) => SpeakerPan.Traits(loop.Entity, ViewEntity, loop.IsMusic, loop.Speaker);

    private float GainOf(Loop loop, (float X, float Y, float Z) listener, float musicVolume)
    {
        bool local = TraitsOf(loop).HasFlag(ChannelTraits.Local);

        return loop.Volume * SoundGain.AtDistance(loop.SoundLevel, SpeakerPan.Distance((loop.X, loop.Y, loop.Z), listener, local)) *
            SoundGain.LocalWeapon(local, loop.Channel) * SoundGain.Music(loop.IsMusic, musicVolume);
    }

    /// <summary>One loop's <see cref="SpeakerPan.Spatialize"/>.</summary>
    private (float Distance, float Left, float Right) Spatial(
        Loop loop, (float X, float Y, float Z) listener, (float X, float Y, float Z) right) =>
        SpeakerPan.Spatialize((loop.X, loop.Y, loop.Z), listener, right, loop.Mono, TraitsOf(loop), loop.Radius, Headphones);

    /// <summary>Whether the device is headphones (<see cref="SpeakerPan.IsHeadphones"/>), TF2's default.</summary>
    public bool Headphones { get; set; } = true;

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

    /// <summary>Moves every channel from a nonzero entity to where that entity now is (B724).</summary>
    /// <param name="originOf">
    /// Where an entity's sounds are now and its model's radius, or null to leave its channels where they were.
    /// </param>
    /// <remarks>
    /// `SND_Spatialize` (engine.dll FUN_180029440) takes the client's origin into the channel when the channel's bit 0
    /// of <c>+0x13c</c> is set, which <c>S_StartDynamicSound</c> sets for <c>bUpdatePositions</c> from a nonzero source
    /// (<c>0x18002e9d0</c>); <c>CL_DispatchSound</c> (FUN_18008caf0) starts every network sound with it. The world,
    /// entity 0, never moves; an entity the client has no origin for (dormant) keeps the last.
    /// </remarks>
    public void Follow(Func<int, (float X, float Y, float Z, float Radius)?> originOf)
    {
        ArgumentNullException.ThrowIfNull(originOf);

        for (int index = 0; index < _loops.Count; index++)
        {
            Loop loop = _loops[index];

            // The radius is asked for only while the channel has none (FUN_180029440 passes `+0x118` to the client when it
            // is 0), so once known it stays.
            if (loop.Entity != 0 && originOf(loop.Entity) is { } now)
            {
                _loops[index] = loop with
                {
                    X = now.X,
                    Y = now.Y,
                    Z = now.Z,
                    Radius = loop.Radius == 0f ? now.Radius : loop.Radius,
                };
            }
        }
    }

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
    /// <returns>
    /// One entry per tracked channel, in start order, naming its channel, its voice and its gain: a list this reuses,
    /// read before the next call.
    /// </returns>
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
        List<(int Entity, int Channel, int Voice, float Gain)> gains = _gains;
        gains.Clear();

        foreach (Loop loop in _loops)
        {
            gains.Add((loop.Entity, loop.Channel, loop.Voice, GainOf(loop, (x, y, z), musicVolume)));
        }

        return gains;
    }

    /// <summary>Each followed voice's ears from a listener: the device's pan of the channel on it (B724).</summary>
    /// <param name="listener">The listener, in world units.</param>
    /// <param name="right">The listener's right vector.</param>
    /// <returns>One entry per loop with a voice, in start order: a list this reuses, read before the next call.</returns>
    /// <remarks>
    /// `SND_Spatialize` re-pans every channel every frame. A loop gained by key has no voice to pan, so it keeps the
    /// pan it started with.
    /// </remarks>
    public IReadOnlyList<(int Voice, float Left, float Right)> PansAt((float X, float Y, float Z) listener, (float X, float Y, float Z) right)
    {
        List<(int Voice, float Left, float Right)> pans = _pans;
        pans.Clear();

        foreach (Loop loop in _loops)
        {
            if (loop.Voice == 0)
            {
                continue;
            }

            (_, float left, float rightEar) = Spatial(loop, listener, right);
            pans.Add((loop.Voice, left, rightEar));
        }

        return pans;
    }

    /// <summary>Each followed voice's origin, as <see cref="Follow"/> left it (B730).</summary>
    /// <returns>One entry per loop with a voice, in start order: a list this reuses, read before the next call.</returns>
    public IReadOnlyList<(int Voice, (float X, float Y, float Z) Origin)> Origins()
    {
        _origins.Clear();

        foreach (Loop loop in _loops)
        {
            if (loop.Voice != 0)
            {
                _origins.Add((loop.Voice, (loop.X, loop.Y, loop.Z)));
            }
        }

        return _origins;
    }

    /// <summary>What <see cref="Origins"/> answers in, every frame.</summary>
    private readonly List<(int Voice, (float X, float Y, float Z) Origin)> _origins = [];

    private int First(int entity, int channel, string name) =>
        _loops.FindIndex(loop => loop.Entity == entity && loop.Channel == channel && SoundName.SameSfx(loop.Name, name));
}
