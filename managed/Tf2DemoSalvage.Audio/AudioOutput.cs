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
public sealed unsafe class AudioOutput : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;

    /// <summary>Sources that have been handed a buffer, with the buffer to free after them.</summary>
    private readonly List<(uint Source, uint Buffer)> _playing = [];

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
    /// <param name="leftGain">Gain for the left ear, from <see cref="SoundGain.Pan"/>.</param>
    /// <param name="rightGain">Gain for the right ear.</param>
    /// <param name="pitch">Playback rate, 1 being unshifted.</param>
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
    public void Play(SoundSample sample, float leftGain, float rightGain, float pitch = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sample.Samples.Length == 0 || sample.SampleRate <= 0 || sample.Channels <= 0)
        {
            return;
        }

        short[] stereo = ToStereo16(sample, leftGain, rightGain);

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
        _al.SetSourceProperty(source, SourceFloat.Gain, 1f);

        _al.SourcePlay(source);

        _playing.Add((source, buffer));
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
            (uint source, uint buffer) = _playing[index];

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

        foreach ((uint source, uint buffer) in _playing)
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
