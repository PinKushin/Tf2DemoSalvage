using System;
using System.Collections.Generic;

using Silk.NET.OpenAL;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Plays finished stereo samples. It knows nothing about distance, direction or the world.
/// </summary>
/// <remarks>
/// **Deliberately stupid, and that is the whole of D80.** This project already implements Valve's
/// spatialisation — <see cref="SoundGain"/> and <see cref="SoundAttenuation"/>, written against the
/// SDK and against measured engine constants. OpenAL has a distance model of its own and applies it
/// per source by default, so letting it help would either double-attenuate or quietly substitute
/// its curve for Valve's.
///
/// Neither failure looks like an error: sound comes out, it falls off with distance, and it is
/// wrong in a way nobody can hear as a defect. So every source here is listener-relative at the
/// origin, where any inverse-distance model computes a gain of exactly one, and the only
/// spatialisation in the program stays the one that can be compared against Valve's.
///
/// **Null when there is no device, rather than throwing.** The same shape as
/// <c>OffscreenTarget.TryCreate</c> and for the same reason: the measurement boxes have no sound
/// card, CI has no sound card, and a viewer on a machine with audio disabled should draw silently
/// rather than refuse to start.
/// </remarks>
public sealed unsafe class AudioOutput : IAudioSink, IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;

    /// <summary>Sources that have been handed a buffer, with the buffer to free after them.</summary>
    /// <remarks>
    /// **Keyed by entity and channel as well, because Source's channels are exclusive.** A named
    /// channel plays one sound at a time per entity: a new one replaces what is there, and
    /// <c>SND_STOP</c> silences it. Without that a looping door plays its whole file instead of
    /// being cut when the door arrives — the owner heard exactly that: "gate sounds are either
    /// playing too slow or just playing too long".
    /// </remarks>
    private readonly List<Voice> _playing = [];

    /// <summary>One sound in flight.</summary>
    private readonly record struct Voice(uint Source, uint Buffer, int Entity, int Channel);

    /// <summary>
    /// <c>CHAN_AUTO</c>, <c>soundflags.h</c>: the engine allocates a free channel rather than a
    /// named one, so these overlap instead of replacing each other.
    /// </summary>
    private const int AutoChannel = 0;

    private bool _disposed;

    private AudioOutput(AL al, ALContext alc, Device* device, Context* context)
    {
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
    }

    /// <summary>Opens the default output, or answers null when there is none.</summary>
    /// <returns>The output, or <c>null</c> when no device could be opened.</returns>
    /// <remarks>
    /// Every failure path answers null rather than throwing, including the native library being
    /// absent: `Silk.NET.OpenAL.Soft.Native` ships `openal32.dll`, but a trimmed deployment or an
    /// unusual RID can still leave it missing, and that is not a reason to refuse to show a demo.
    /// </remarks>
    public static AudioOutput? TryCreate()
    {
        try
        {
            AL al = AL.GetApi();
            ALContext alc = ALContext.GetApi();

            Device* device = alc.OpenDevice(string.Empty);

            if (device is null)
            {
                return null;
            }

            Context* context = alc.CreateContext(device, null);

            if (context is null)
            {
                alc.CloseDevice(device);
                return null;
            }

            alc.MakeContextCurrent(context);

            return new AudioOutput(al, alc, device, context);
        }
        catch (DllNotFoundException)
        {
            // Reported by the caller rather than here: this type has no log, and a viewer that
            // cannot open an audio device should say so once at startup rather than per sound.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>How many sounds are still playing.</summary>
    public int Playing => _playing.Count;

    /// <summary>Plays one sound, already mixed and already attenuated.</summary>
    /// <param name="sample">The decoded sound.</param>
    /// <param name="leftPan">The left ear's share of the image, from <see cref="SoundGain.Pan"/>.</param>
    /// <param name="rightPan">The right ear's share.</param>
    /// <param name="gain">
    /// Valve's distance attenuation, from <see cref="SoundGain.AtDistance"/> times the sound's own
    /// volume. Kept separate from the pan because this one can be updated while the sound plays —
    /// see <see cref="SetGain"/> — and a loop is unlistenable without that.
    /// </param>
    /// <param name="pitch">Playback rate, 1 being unshifted.</param>
    /// <param name="entity">Which entity is making it, which with the channel forms the key.</param>
    /// <param name="channel">
    /// The channel from <c>soundflags.h</c>. A named one holds a single sound per entity, so
    /// starting on a busy one replaces what is there; <c>CHAN_AUTO</c> overlaps by design.
    /// </param>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// **The gains arrive already computed and are applied to the SAMPLES**, not handed to OpenAL
    /// as a position. That is what keeps Valve's falloff authoritative — see the type remarks.
    ///
    /// A mono source is spread across both ears by those two gains, which is where the panning
    /// happens. A stereo source keeps its own channels and is scaled by them, because a stereo
    /// sound in Source is already a finished mix — music, and some ambience — and re-panning it
    /// would be inventing a position it does not have.
    /// </remarks>
    public void Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain = 1f,
        float pitch = 1f,
        int entity = 0,
        int channel = AutoChannel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sample.Samples.Length == 0 || sample.SampleRate <= 0 || sample.Channels <= 0)
        {
            return;
        }

        // **A named channel holds one sound per entity, so starting on a busy one replaces it.**
        // That is how the engine cuts a player's previous voice line off with their next, and how a
        // door's looping move sound is replaced rather than layered. CHAN_AUTO is exempt by
        // definition: the engine allocates a free channel for those, so they are meant to overlap.
        Stop(entity, channel);

        // **The PAN is baked into the samples; the distance gain is not.** Once a buffer is
        // uploaded its samples cannot change, so anything baked in is fixed for the life of the
        // sound. That was fine while every sound was a one-shot lasting under a second, and wrong
        // the moment loops arrived: a hum started across the map kept that distance's gain for the
        // whole match, and walking up to it could not make it louder (B169).
        //
        // Splitting them puts the half that must follow the listener — the scalar attenuation —
        // where it can be set per frame, and leaves the stereo image where it costs nothing.
        short[] stereo = ToStereo16(sample, leftPan, rightPan);

        if (stereo.Length == 0)
        {
            return;
        }

        uint buffer = _al.GenBuffer();
        uint source = _al.GenSource();

        fixed (short* data = stereo)
        {
            _al.BufferData(
                buffer,
                BufferFormat.Stereo16,
                data,
                stereo.Length * sizeof(short),
                sample.SampleRate);
        }

        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);

        // **Listener-relative at the origin, which is how the distance model is neutralised.**
        // Every inverse-distance curve OpenAL offers computes a gain of one at zero distance, so
        // this holds whatever model the driver defaults to — a stronger guarantee than setting the
        // model explicitly, which a later context change could undo.
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        _al.SetSourceProperty(source, SourceVector3.Position, 0f, 0f, 0f);

        // **Pitch is the engine's, expressed as a rate.** TF2 sends a percentage where 100 is
        // unshifted; the caller has already divided. Clamped only against nonsense, because a
        // demo's pitch is data and refusing an unusual one would silence a sound the game played.
        _al.SetSourceProperty(source, SourceFloat.Pitch, pitch > 0f ? pitch : 1f);

        // **Valve's distance attenuation, and the only part of the mix that can still change.**
        // OpenAL applies this as a plain scalar over the whole source, which is exactly what a
        // distance curve is — so the composed result per ear is `pan x gain`, the same product that
        // used to be baked into the samples, with the second factor now live. OpenAL still computes
        // no attenuation of its own: the source sits at the listener's own position (above), where
        // every distance model returns one.
        _al.SetSourceProperty(source, SourceFloat.Gain, gain < 0f ? 0f : gain);

        // **A looping sound runs until its channel is stopped, which is what makes ambience work.**
        // Six machine hums start at the beginning of cp_process and are meant to run for the whole
        // match; played once each, the map is silent seconds later (B169). The two halves meet
        // exactly here: SND_STOP is what ends them, and B168 now honours it.
        //
        // **Whole-buffer looping, which is an approximation where the loop start is not zero.**
        // Valve returns to the `cue ` point rather than to the beginning, and OpenAL's AL_LOOPING
        // has no way to say that — a sound with an intro would replay the intro. Seamless ambience,
        // which is what this is for, loops from zero and is exact.
        _al.SetSourceProperty(source, SourceBoolean.Looping, sample.Loops);

        _al.SourcePlay(source);

        _playing.Add(new Voice(source, buffer, entity, channel));
    }

    /// <summary>Re-attenuates a sound that is already playing.</summary>
    /// <param name="entity">The entity the sound belongs to.</param>
    /// <param name="channel">The channel it is playing on.</param>
    /// <param name="gain">Valve's distance attenuation for the listener's current position.</param>
    /// <returns>Whether a voice was found and updated.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// **This is what makes a loop follow the listener, and its absence is what B169 was.** A sound
    /// is spatialised once when it starts, which is correct for a one-shot lasting under a second
    /// and useless for ambience meant to run for a whole match: the owner reported the map's hums
    /// as inaudible, and the reason was that each one kept whatever gain the camera's position
    /// implied at the instant it began.
    ///
    /// **Only the scalar moves; the stereo image does not.** The pan is baked into the samples at
    /// <see cref="Play"/> and cannot be changed without re-uploading the buffer, so a loop orbited
    /// by the listener keeps the left/right balance it started with. Distance is the part that
    /// makes ambience appear and disappear, and it is the part that is now live — the remaining
    /// error is second-order and stated rather than hidden.
    /// </remarks>
    public bool SetGain(int entity, int channel, float gain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool found = false;

        foreach (Voice voice in _playing)
        {
            if (voice.Entity != entity || voice.Channel != channel)
            {
                continue;
            }

            _al.SetSourceProperty(voice.Source, SourceFloat.Gain, gain < 0f ? 0f : gain);
            found = true;
        }

        return found;
    }

    /// <summary>Silences whatever an entity is playing on a channel.</summary>
    /// <param name="entity">The entity the sound belongs to.</param>
    /// <param name="channel">The channel, from <c>soundflags.h</c>.</param>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// **This is <c>SND_STOP</c>, and dropping it is audible.** Measured on
    /// <c>movement-test-pov-cp_process</c>: 15 of its 89 sounds are stops, and they name exactly
    /// the looping ones — <c>doors/door_metal_rusty_move</c> five times against eight starts,
    /// <c>metal_box_scrape_rough_loop</c> four, <c>)ambient/machine_hum</c> six. Those loops are
    /// meant to end when the door stops moving; unstopped they run the length of their file.
    ///
    /// **CHAN_AUTO cannot be stopped this way and that is Valve's design, not a gap here.** The
    /// engine picked the channel, so nothing names it afterwards; sounds sent on it are the
    /// fire-and-forget ones that end on their own.
    /// </remarks>
    public void Stop(int entity, int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel == AutoChannel)
        {
            return;
        }

        for (int index = _playing.Count - 1; index >= 0; index--)
        {
            Voice voice = _playing[index];

            if (voice.Entity != entity || voice.Channel != channel)
            {
                continue;
            }

            _al.SourceStop(voice.Source);
            _al.SetSourceProperty(voice.Source, SourceInteger.Buffer, 0);
            _al.DeleteSource(voice.Source);
            _al.DeleteBuffer(voice.Buffer);

            _playing.RemoveAt(index);
        }
    }

    /// <summary>Frees anything that has finished playing.</summary>
    /// <returns>How many sounds were reclaimed.</returns>
    /// <remarks>
    /// **Called by the owner rather than on a timer**, because this type has no thread of its own.
    /// A viewer already has a frame loop and that is the natural place; a sound that finishes
    /// between calls simply stays allocated until the next one, which costs a handle and not a
    /// glitch.
    /// </remarks>
    public int Reclaim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int freed = 0;

        for (int index = _playing.Count - 1; index >= 0; index--)
        {
            (uint source, uint buffer, _, _) = _playing[index];

            _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);

            if (state == (int)SourceState.Playing)
            {
                continue;
            }

            // **Detached before the buffer is deleted**, which OpenAL requires: a buffer still
            // attached to a source is in use, and deleting it is an error the driver reports
            // through alGetError rather than by failing the call.
            _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
            _al.DeleteSource(source);
            _al.DeleteBuffer(buffer);

            _playing.RemoveAt(index);
            freed++;
        }

        return freed;
    }

    /// <summary>Stops everything at once, for a seek or a pause.</summary>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// **A seek has to silence the world, not fade it.** Sounds are scheduled from the timeline by
    /// tick, so after a jump the ones in flight belong to a moment the viewer is no longer at.
    /// Letting them finish would play the old place over the new one.
    /// </remarks>
    public void StopAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach ((uint source, uint buffer, _, _) in _playing)
        {
            _al.SourceStop(source);
            _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
            _al.DeleteSource(source);
            _al.DeleteBuffer(buffer);
        }

        _playing.Clear();
    }

    /// <summary>Interleaves a sample to stereo 16-bit, applying the two gains.</summary>
    /// <param name="sample">The decoded sound.</param>
    /// <param name="leftGain">Gain for the left ear.</param>
    /// <param name="rightGain">Gain for the right ear.</param>
    /// <returns>Interleaved stereo, left first.</returns>
    /// <remarks>
    /// **Clamped before conversion, not after.** A gain above one is legitimate — Valve's own
    /// volume and the distance curve can combine past unity on a close, loud sound — and letting a
    /// float past 1.0 wrap through the short conversion turns a loud sound into a burst of noise.
    /// Saturating is what the engine's mixer does with the same overflow.
    /// </remarks>
    internal static short[] ToStereo16(SoundSample sample, float leftGain, float rightGain)
    {
        ReadOnlySpan<float> samples = sample.Samples.Span;
        int frames = sample.FrameCount;

        if (frames <= 0)
        {
            return [];
        }

        short[] stereo = new short[frames * 2];

        for (int frame = 0; frame < frames; frame++)
        {
            // A mono source feeds both ears and the two gains do the panning; a stereo source
            // already carries its own image and each side is only scaled.
            float left = samples[frame * sample.Channels];
            float right = sample.Channels > 1 ? samples[(frame * sample.Channels) + 1] : left;

            stereo[frame * 2] = ToPcm(left * leftGain);
            stereo[(frame * 2) + 1] = ToPcm(right * rightGain);
        }

        return stereo;
    }

    private static short ToPcm(float value) =>
        (short)(Math.Clamp(value, -1f, 1f) * short.MaxValue);

    /// <inheritdoc/>
    /// <remarks>
    /// **Explicit, so this class keeps the surface it already had.** <see cref="IAudioSink"/> exists
    /// for callers that schedule sound without owning a device (B188); implementing it explicitly
    /// means no existing call site changes, the optional arguments on <see cref="Play"/> stay where
    /// callers expect them, and the interface can avoid CA1716's objection to a member named
    /// <c>Stop</c> without this class having to rename anything.
    /// </remarks>
    void IAudioSink.Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel) =>
        Play(sample, leftPan, rightPan, gain, pitch, entity, channel);

    /// <inheritdoc/>
    bool IAudioSink.SetGain(int entity, int channel, float gain) => SetGain(entity, channel, gain);

    /// <inheritdoc/>
    void IAudioSink.Silence(int entity, int channel) => Stop(entity, channel);

    /// <inheritdoc/>
    void IAudioSink.SilenceAll() => StopAll();

    /// <inheritdoc/>
    int IAudioSink.Reclaim() => Reclaim();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAll();

        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);

        _al.Dispose();
        _alc.Dispose();

        _disposed = true;
    }
}
