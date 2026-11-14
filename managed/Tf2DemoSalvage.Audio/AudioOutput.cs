using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Microsoft.Extensions.Logging;

using Silk.NET.OpenAL;

using Tf2DemoSalvage.Core.Net;

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
    /// <remarks>
    /// `Sound` names what it plays, as the engine's `ch->sfx` does. `Id` is the voice's own handle, as the engine's channel
    /// index is: two CHAN_AUTO sounds on one entity are two channels (`SND_PickDynamicChannel`, engine.dll FUN_18002a480,
    /// never matches a CHAN_AUTO request to a busy channel). **Two sources, one an ear** (B724): the engine keeps a volume
    /// per speaker and OpenAL one gain per source, so `Left` plays the sound's left channel alone and `Right` its right,
    /// started together, each at its own speaker volume. `Level` is its volumes and fade, made new by every
    /// <see cref="Play"/>, so nothing carries from an earlier sound. `Slot` is its dynamic channel, 0..63, or -1 for a
    /// static one (B730); `FromLocalPlayer` is whether its source is the local player, which the steal spares. `Delayed` is
    /// the channel's flag 0x20 at +0x13c, set by a start with a delay and kept for the channel's life, and `FromServer` its
    /// flag 0x40, `params.fromserver` (B732).
    /// </remarks>
    private readonly record struct Voice(
        uint Left,
        uint Right,
        uint LeftBuffer,
        uint RightBuffer,
        int Entity,
        int Channel,
        SoundSample Sound,
        int Id,
        Level Level,
        int Slot = -1,
        bool FromLocalPlayer = false,
        bool Delayed = false,
        bool FromServer = false)
    {
        /// <summary>Whether this voice plays <paramref name="sample"/>'s sfx.</summary>
        /// <remarks>
        /// **By the cache's sfx key** (finding 80): a WAV's float memory is empty, so every WAV matched on memory, and an
        /// evicted sound decodes again to new memory. A sample from no cache has only its memory to go by.
        /// </remarks>
        public bool Plays(SoundSample sample) =>
            Sound.Key is not null || sample.Key is not null
                ? string.Equals(Sound.Key, sample.Key, StringComparison.Ordinal)
                : Sound.Samples.Equals(sample.Samples) && Sound.Pcm16.Equals(sample.Pcm16);
    }

    /// <summary>The next voice handle; 0 is reserved for "nothing started".</summary>
    private int _nextVoice = 1;

    /// <summary>The voices in <see cref="_playing"/> by handle, so a per-voice gain is one lookup, not a scan.</summary>
    private readonly Dictionary<int, Voice> _byId = [];

    /// <summary>One voice's level: each ear's speaker volume, what they are spatialised from, and the voice's fade.</summary>
    /// <remarks>
    /// **A volume for each ear, each its own integer and its own ramp**, as the engine keeps one per speaker
    /// (<c>FUN_1800214a0</c>, <c>FUN_180021670</c>). The target of each is the channel's gain × that ear's share × 255,
    /// so a new gain or a new pan re-aims both.
    /// </remarks>
    private sealed class Level
    {
        public SpeakerVolume Left { get; } = new();

        public SpeakerVolume Right { get; } = new();

        /// <summary>The channel's gain: the sound's volume × its distance gain × the mixer's scales.</summary>
        public float Gain { get; set; }

        /// <summary>Each ear's share, from <see cref="SpeakerPan"/>.</summary>
        public (float Left, float Right) Pan { get; set; }

        /// <summary>The voice mixer's fade, applied to the source's samples, not the channel's volume (FUN_1800590b0).</summary>
        public float Fade { get; set; } = 1f;

        /// <summary>The channel's origin, `ch->origin`, moved as its entity is (<see cref="MoveVoice"/>); null when unsaid.</summary>
        public (float X, float Y, float Z)? Origin { get; set; }

        /// <summary>Aims both ears at the gain and pan held: at once before the first frame, ramped after.</summary>
        public void Aim()
        {
            Left.Aim(Spatialised(Pan.Left, Gain));
            Right.Aim(Spatialised(Pan.Right, Gain));
        }

        /// <summary>One speaker's volume on the engine's 0..255 scale, before it is made an integer.</summary>
        /// <param name="share">That speaker's share, from <see cref="SpeakerPan"/>.</param>
        /// <param name="gain">The channel's own gain.</param>
        /// <remarks>
        /// Master volume × <c>snd_gain</c> × the distance gain × the mixer's scales × the speaker's share, clamped to
        /// 0..255 (engine.dll <c>FUN_18001e0b0</c> at <c>0x18001ee40</c>) and made the speaker's integer target by
        /// <c>FUN_1800214a0</c> (<see cref="SpeakerVolume.Quantize"/>), before the paint ducks it: the ceiling is 255 in
        /// each ear. Master <c>volume</c> is applied after the mix and <c>voice_scale</c> in the voice's samples, so
        /// neither is under it.
        /// </remarks>
        private static float Spatialised(float share, float gain) => gain * share * 255f;
    }

    /// <summary>The engine's speech ducker, stepped by <see cref="Update"/> (B719).</summary>
    private readonly SpeechDucker _ducker = new();

    /// <summary>The voice overdrive, stepped by <see cref="VoiceIdle"/> (B549).</summary>
    private readonly VoiceOverdrive _overdrive = new();

    /// <summary>
    /// What a non-voice channel's volume is multiplied by: the smaller of the speech duck's and the overdrive's scales, over
    /// 256 below unity (<c>FUN_180041fb0</c>, <c>0x180042032-0x18004204e</c>), else 1.
    /// </summary>
    private float DuckGain
    {
        get
        {
            int scale = Math.Min(_ducker.Scale, _overdrive.Scale);

            return scale < SpeechDucker.Unity ? scale * (1f / SpeechDucker.Unity) : 1f;
        }
    }

    /// <summary>
    /// <c>CHAN_AUTO</c>, <c>soundflags.h</c>: the engine allocates a free channel rather than a
    /// named one, so these overlap instead of replacing each other.
    /// </summary>
    private const int AutoChannel = SoundScript.AutoChannel;

    /// <summary><c>CHAN_STATIC</c>: every sound on it gets a slot of its own.</summary>
    private const int StaticChannel = SoundScript.StaticChannel;

    /// <summary>The static slots, 0x40 to `MAX_CHANNELS` 0x80 (engine.dll FUN_18002f4c0).</summary>
    public const int StaticChannels = 64;

    /// <summary>The voice in each dynamic channel, 0 for a free one (B730).</summary>
    private readonly int[] _slots = new int[DynamicChannelPick.Channels];

    /// <summary>The dynamic channels as <see cref="DynamicChannelPick"/> weighs them; reused by every start.</summary>
    private readonly BusyChannel?[] _busy = new BusyChannel?[DynamicChannelPick.Channels];

    /// <summary>The delay-started channels a pick passed over, which the new start frees when it begins; reused (B732).</summary>
    private readonly int[] _waiting = new int[DynamicChannelPick.Channels];

    /// <summary>The engine's mixer rate, `shm->m_rate` (+0x18), which painted time counts in.</summary>
    private const int MixRate = 44100;

    /// <summary>The mixer's clock for a speaker: real time, as `paintedtime` follows the device.</summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Frames a loopback has rendered, which is its mixer's clock.</summary>
    private long _rendered;

    /// <summary>A loopback's frame rate; 0 on a speaker.</summary>
    private int _loopbackRate;

    /// <summary>`CLOCK_SYNC_SERVER`'s latch, for starts timed on the server's clock (B732).</summary>
    private readonly ServerSoundClock _serverClock = new();

    /// <summary>Voices waiting out a positive delay, by handle: the painted time each starts at (B732).</summary>
    private readonly Dictionary<int, long> _startAt = [];

    /// <summary>
    /// Delay-started voices a later start on their channel named, by handle: the painted time they are freed at, `ch +0xa8`
    /// (B732); 0 is unset, as the mixer's `!= 0` guard reads it.
    /// </summary>
    private readonly Dictionary<int, long> _freeAt = [];


    private bool _disposed;

    /// <summary>Where an ALC call that fails without throwing is reported.</summary>
    private readonly ILogger _log;

    private AudioOutput(AL al, ALContext alc, Device* device, Context* context, ILogger log)
    {
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        _log = log;
    }

    /// <summary>Opens the default output, or answers null when there is none.</summary>
    /// <param name="log">
    /// Where an ALC call that fails by return value is reported. **Those fail silently otherwise**:
    /// a context that never became current leaves every later AL call failing with no exception,
    /// so the viewer would run "with sound" and play nothing.
    /// </param>
    /// <returns>The output, or <c>null</c> when no device could be opened.</returns>
    /// <remarks>
    /// Every failure path answers null rather than throwing, including the native library being
    /// absent: `Silk.NET.OpenAL.Soft.Native` ships `openal32.dll`, but a trimmed deployment or an
    /// unusual RID can still leave it missing, and that is not a reason to refuse to show a demo.
    /// </remarks>
    public static AudioOutput? TryCreate(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            // **The shipped library is loaded by absolute path first, and without this a machine
            // that HAS it still fails** (B217). The package puts it under
            // `runtimes/<rid>/native/`, which .NET's native probing does not reach for a load
            // issued by `Silk.NET.OpenAL` rather than by this assembly — measured headless on
            // `mutation-box`, where `libopenal.so` sat in the output and `AL.GetApi()` reported
            // "Could not load from any of the possible library names".
            //
            // Ignored on failure rather than checked: a machine with OpenAL installed system-wide
            // needs nothing from us, and one with neither is handled by the catches below.
            _ = NativeLibraryResolver.TryLoadOpenAl();

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
                CloseDevice(alc, device, log);
                return null;
            }

            if (!alc.MakeContextCurrent(context))
            {
                ReleaseUncurrent(alc, device, context, log, "alcMakeContextCurrent");
                return null;
            }

            return new AudioOutput(al, alc, device, context, log);
        }
        catch (DllNotFoundException)
        {
            // Reported by the caller rather than here: a viewer that cannot open an audio device
            // should say so once at startup rather than per sound, and the caller's line does.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            // **What Silk.NET actually throws when the library is absent, and it was not caught**
            // (B217). Its loader reports *"Could not load from any of the possible library names"*
            // as a `FileNotFoundException`, not the `DllNotFoundException` the two catches above
            // expect — so on a machine with no OpenAL at all this method THREW rather than handing
            // back null, and the viewer died at startup instead of running without sound.
            //
            // Never seen on a developer's machine, because every one of them has an audio stack.
            // Found by running these tests on a headless box, which is the same reason CI is the
            // only place the no-TF2 path gets exercised
            // (`docs/memory/ci-is-the-machine-without-tf2.md`).
            return null;
        }
    }

    /// <summary>How many sounds are still playing.</summary>
    public int Playing => _playing.Count;

    /// <summary>The interleaved stereo a sound is converted into before OpenAL copies it; reused (finding 80).</summary>
    private short[] _stereo = [];

    /// <summary>Plays one sound, already mixed and already attenuated.</summary>
    /// <param name="sample">The decoded sound.</param>
    /// <param name="leftPan">The left ear's share of the image, from <see cref="SpeakerPan"/> by the device's law.</param>
    /// <param name="rightPan">The right ear's share.</param>
    /// <param name="gain">
    /// Valve's distance attenuation, from <see cref="SoundGain.AtDistance"/> times the sound's own
    /// volume. Each ear's speaker volume is gain × that ear's pan × 255; either can change while the
    /// sound plays — <see cref="SetVoiceGain"/>, <see cref="SetVoicePan"/>.
    /// </param>
    /// <param name="pitch">Playback rate, 1 being unshifted.</param>
    /// <param name="entity">Which entity is making it, which with the channel forms the key.</param>
    /// <param name="channel">
    /// The channel from <c>soundflags.h</c>. A named one holds a single sound per entity, so
    /// starting on a busy one replaces what is there; <c>CHAN_AUTO</c> overlaps by design.
    /// </param>
    /// <param name="place">Where it is heard from, for the channel steal (B730); default at the listener.</param>
    /// <returns>The voice's handle, for <see cref="SetVoiceGain"/>; 0 when nothing was started.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// **The gains arrive already computed and are applied per ear**, not handed to OpenAL as a
    /// position. That is what keeps Valve's falloff authoritative — see the type remarks.
    ///
    /// A mono source plays in both ears, each at its own volume, which is where the panning happens.
    /// A stereo source's left channel goes to the left ear and its right to the right, each at that
    /// ear's volume, as the engine paints a stereo channel.
    /// </remarks>
    public int Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain = 1f,
        float pitch = 1f,
        int entity = 0,
        int channel = AutoChannel,
        SoundPlace place = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sample.SampleCount == 0 || sample.SampleRate <= 0 || sample.Channels <= 0)
        {
            return Refuse(StartRefusal.Unplayable);
        }

        // **A dynamic sound takes the channel `SND_PickDynamicChannel` gives it** (B730, engine.dll FUN_18002a480): its own
        // busy (source, channel) slot, which is how the engine cuts a player's previous voice line off with their next and
        // replaces a door's looping move sound rather than layering it; else a free one of 64; else one stolen. CHAN_AUTO
        // never matches, so those overlap. With none to give, nothing starts, as `S_StartDynamicSound` starts nothing.
        //
        // **CHAN_STATIC is exempt, read out of `engine.dll`** (B415): `S_StartStaticSound` takes the first free slot from
        // `MAX_DYNAMIC_CHANNELS` on and never compares the source or the channel. Only an explicit stop ends a static
        // sound. A soundscape loop is static in the engine (`EmitAmbientSound`, and `CHAN_STATIC`, `c_soundscape.cpp:1170`)
        // and keyed here by its own channel, which it replaces.
        // **Each ear is its own source, playing its own channel of the sound at unit gain** (B724): the engine paints a
        // channel into each speaker at that speaker's volume, and OpenAL has one gain a source. So the left source's buffer
        // carries the sound's left channel (a mono sound's only one) with silence on the right, the right source the
        // reverse, and each ear's speaker volume is that source's gain, live: a new pan is a new pair of gains, where a pan
        // baked into the samples could not move (B169).
        // **Into a buffer this output keeps** (finding 80): OpenAL copies on `BufferData`, so the interleaved samples are
        // scratch, and a new array per sound started was 90 MB of short[] per 20 s of f12. Grown, never shrunk; `Play`
        // is not reentrant, as the voices list beside it already requires.
        // **Before a channel is taken**, so a sound with nothing to play steals no one's channel.
        if (_stereo.Length < sample.FrameCount * 2)
        {
            _stereo = new short[sample.FrameCount * 2];
        }

        if (ToStereo16(sample, 1f, 0f, _stereo) == 0)
        {
            return Refuse(StartRefusal.Unplayable);
        }

        // The mix has run up to now: what it would have freed or begun by now, it has (B732).
        Mix();

        int slot = -1;
        bool dynamic = IsDynamic(entity, channel);
        float delay = place.Delay;

        if (dynamic)
        {
            slot = PickSlot(sample, entity, channel, place, delay);

            if (slot < 0)
            {
                return Refuse(StartRefusal.NoDynamicChannel);
            }

            // The channel the pick named is the new sound's, as the engine's is once picked; stopped now so its two
            // sources are free for the new one's — unless it plays this same loop for this source and channel, which
            // `S_StartDynamicSound` leaves playing and starts nothing (FUN_18002e6e0, 0x18002e8a4-0x18002e8df; B732).
            if (_slots[slot] != 0)
            {
                Voice held = _byId[_slots[slot]];

                if (held.Sound.Loops && held.Entity == entity && held.Channel == channel && held.Plays(sample))
                {
                    return Refuse(StartRefusal.LoopKept);
                }

                Free(_playing.IndexOf(held));
            }
        }
        else
        {
            // A soundscape loop is keyed by its own channel, which it replaces.
            if (channel != StaticChannel)
            {
                Stop(entity, channel);
            }

            if (StaticVoices() >= StaticChannels)
            {
                // **Sixty-four static channels, and a start with none free plays nothing** (B550). `S_StartStaticSound`
                // (engine.dll FUN_18002f4c0) takes the first channel from 0x40 whose sfx is null, else grows
                // `total_channels`, and at 0x80 says "total_channels == MAX_CHANNELS" and returns. A soundscape loop is one
                // of them (`EmitAmbientSound` is static), so the 64 dynamic and 64 static voices hold 256 sources at most,
                // what any device gives. A skip back that replays a long demo's static sounds meets this.
                return Refuse(StartRefusal.NoStaticChannel);
            }
        }

        // **A speaker's start whose wave loops plays nothing, and its channel is already taken** (B724). Both starts test
        // `SND_SPEAKER` (bit 3 of ch +0x13c) with the source's IsLooped only after the channel is theirs and its wave is
        // loaded, and free it: "Speaker ignored looping sound" (FUN_18002e6e0, 0x18002ec0a; FUN_18002f4c0). In the dynamic
        // start that is after `SND_PickDynamicChannel` (FUN_18002a480) and the free of what the picked channel held
        // (FUN_180045cc0, past the loop keep at 0x18002e8a4-0x18002e8df), so what the entity's channel was playing has
        // stopped; a kept loop returned before it. Before the delay, which the engine reads inside the branch this skips.
        if (place.Speaker && sample.Loops)
        {
            return Refuse(StartRefusal.SpeakerLoop);
        }

        // **The delay, in the source's samples** (B732): `S_StartDynamicSound` `(int)(delay × rate)` (FUN_18002e6e0,
        // 0x18002efaa), `S_StartStaticSound` `(int)(pitch × 0.01 × rate × delay)` (FUN_18002f4c0). Above 0 the channel
        // waits that many samples silent at its mix rate; below 0 it starts that far in, wrapped when it loops, and a skip
        // of the whole wave or more starts nothing — after the channel was taken, as the engine frees it then.
        int delaySamples = (int)((dynamic ? 1f : pitch) * sample.SampleRate * delay);
        int skip = 0;

        if (delay < 0f)
        {
            skip = sample.Loops ? -delaySamples % sample.FrameCount : -delaySamples;

            if (skip >= sample.FrameCount)
            {
                return Refuse(StartRefusal.SkippedPastTheEnd);
            }
        }

        // Flag 0x20: any delay on a dynamic channel; only a positive one on a static (0x18002efbc, 0x18002f05d, 0x18002fbf3).
        bool delayed = delay > 0f || (delay < 0f && dynamic);

        Level level = new() { Gain = gain, Pan = (leftPan, rightPan), Origin = place.Origin };
        level.Aim();

        uint left = Source(sample, pitch, Heard(sample, level.Left, level), out uint leftBuffer);
        _ = ToStereo16(sample, 0f, 1f, _stereo);
        uint right = Source(sample, pitch, Heard(sample, level.Right, level), out uint rightBuffer);

        // An ear the library could not give a source would be half a sound, never reclaimed: neither plays.
        if (left == 0 || right == 0)
        {
            Release(new Voice(left, right, leftBuffer, rightBuffer, entity, channel, sample, 0, level));

            return Refuse(StartRefusal.NoSource);
        }

        if (skip > 0)
        {
            _al.SetSourceProperty(left, SourceInteger.SampleOffset, skip);
            _al.SetSourceProperty(right, SourceInteger.SampleOffset, skip);
        }

        int id = NextVoice();

        // The wait is `delaySamples` output samples in the pass of the source's own rate (FUN_18004f0c0 pays mixer +0x2c;
        // FUN_180042870 mixes 11025, 22050 and 44100 apart), so delaySamples / rate seconds, painted at 44100. Pitch does not
        // stretch it; a static start's count is already scaled by pitch above, so a static one waits pitch × delay.
        // The voice holds its channel meanwhile, unstarted, and the mix begins it (`Mix`).
        // ponytail: begun by the first `Mix` at or past its time, so up to a frame late; sample-exact needs a device clock.
        if (delaySamples > 0)
        {
            _startAt[id] = Painted + ((long)delaySamples * MixRate / sample.SampleRate);
        }
        else
        {
            StartBoth(left, right);
        }

        Voice started = new(
            left, right, leftBuffer, rightBuffer, entity, channel, sample, id, level, slot, place.FromLocalPlayer, delayed, place.FromServer);
        _playing.Add(started);
        _byId[id] = started;

        if (slot >= 0)
        {
            _slots[slot] = id;
        }

        Refusal = StartRefusal.None;

        return id;
    }

    /// <summary>Why the last start that answered 0 played nothing (B732).</summary>
    public StartRefusal Refusal { get; private set; }

    /// <summary>Starts nothing, and says why: here, and at Debug.</summary>
    private int Refuse(StartRefusal why)
    {
        Refusal = why;

        // Guarded: a kept loop is a common refusal, and the line is built only when someone reads it.
        if (_log.IsEnabled(LogLevel.Debug))
        {
            _log.LogDebug("{Message}", $"a sound was not started: {why}");
        }

        return 0;
    }

    /// <summary>Starts a voice's two ears in one call, so they begin on the same sample: `alSourcePlayv` starts them together.</summary>
    private void StartBoth(uint left, uint right)
    {
        uint* both = stackalloc uint[] { left, right };
        _al.SourcePlay(2, both);
    }

    /// <summary>The mixer's painted time in samples at <see cref="MixRate"/>: rendered frames on a loopback, real time on a speaker.</summary>
    private long Painted => _loopbackRate > 0 ? _rendered * MixRate / _loopbackRate : PaintedAt(_clock.Elapsed.TotalSeconds);

    /// <summary>Seconds of mixing as painted samples, a long so 13.5 hours does not wrap it (B732).</summary>
    internal static long PaintedAt(double seconds) => (long)(seconds * MixRate);

    /// <summary>
    /// The channel list's latch rule (FUN_180040420), once a host frame from <see cref="Update"/>, `S_Update`'s place: after
    /// the frame's sounds are dispatched, as `_Host_RunFrame` (FUN_1801a5eb0) runs the client step, whose
    /// `CL_DispatchSound` asks the clock, before its "_Host_RunFrame_Sound" scope calls FUN_180030870, whose paint runs this
    /// (B732).
    /// </summary>
    /// <param name="frameTime">The host frame's real length; past 0.1 s it was clamped, which drops the latch.</param>
    private void DropServerLatch(float frameTime)
    {
        if (_serverClock.Latched && (frameTime > 0.1f || !HoldsServerLatch()))
        {
            _serverClock.Reset();
        }
    }

    /// <summary>
    /// What the mix does on its way to now (B732): frees each delay-started channel a later start named once its time is
    /// reached (`MIX_MixChannelsToPaintbuffer`, FUN_180040ae0 at 0x180040ef1: `ch+0xa8 != 0 &amp;&amp; ch+0xa8 &lt;= the
    /// paint's end`), and begins each voice whose wait is over.
    /// </summary>
    /// <remarks>Removing from a dictionary while enumerating it is allowed from .NET Core 3 on, so nothing is copied.</remarks>
    private void Mix()
    {
        long painted = Painted;

        foreach ((int voice, long at) in _freeAt)
        {
            if (at != 0 && at <= painted && _byId.TryGetValue(voice, out Voice due))
            {
                Free(_playing.IndexOf(due));
            }
        }

        foreach ((int voice, long at) in _startAt)
        {
            if (at <= painted && _byId.TryGetValue(voice, out Voice due))
            {
                StartBoth(due.Left, due.Right);
                _startAt.Remove(voice);
            }
        }
    }

    /// <summary>
    /// Whether a live channel holds the server clock's latch: one started with a delay (0x20) by the server (0x40), whose
    /// source is not -2 and which is not CHAN_VOICE, CHAN_VOICE2 or a sentence (FUN_180040420, 0x1800407e3-0x18004082f).
    /// </summary>
    /// <remarks>
    /// Sentences never reach this sink. A channel whose mixer is not ready (vtable +0x60, 0x18004064a) is dropped from the
    /// list before this test, so a sound waiting on its data holds nothing. **The frame rule:** the same list drops the
    /// latch when `host_frametime_unbounded &gt; host_frametime` (`comiss`/`ja`, 0x180040911-0x180040920), which
    /// `Host_FilterTime` (FUN_1801a1920) makes so when a real frame runs past 0.1 s, at any timescale.
    /// **Not ported:** the drop while the game is paused (sound services +0x78, the `jnz` at 0x18004090f).
    /// </remarks>
    private bool HoldsServerLatch()
    {
        foreach (Voice voice in _playing)
        {
            if (voice.Delayed && voice.FromServer && voice.Entity != -2 &&
                voice.Channel is not (SoundScript.VoiceChannel or SoundScript.VoiceChannel2) && IsPlaying(voice))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc cref="IAudioSink.DelayFor"/>
    /// <remarks>
    /// <see cref="ServerSoundClock"/> on this mixer's clock, at least 1e-6 (engine.dll FUN_18008caf0,
    /// 0x18008cd92-0x18008cd9c). The latch is as the last frame's <see cref="Update"/> left it.
    /// </remarks>
    public float DelayFor(ServerSoundTime time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float delay = _serverClock.Delay(time, Painted, MixRate);

        // 0x358637bd, 1e-6 (0x18008cd9c): no wait, but a delay-started channel.
        return delay <= 0f ? 1e-6f : delay;
    }

    /// <summary>Whether a start takes a dynamic channel: neither CHAN_STATIC nor the soundscape's, which is static.</summary>
    private static bool IsDynamic(int entity, int channel) =>
        channel != StaticChannel && entity != SoundscapeSystem.SoundscapeEntity;

    /// <summary>
    /// The dynamic channel a start takes, <see cref="DynamicChannelPick"/> over the 64 as they are now; -1 for none. A
    /// voice that has ended is a free channel, as the engine's mixer frees a channel it finishes.
    /// </summary>
    private int PickSlot(SoundSample sample, int entity, int channel, SoundPlace place, float delay)
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            _busy[slot] = null;

            // `Find` frees a voice that has ended, which clears its slot.
            if (_slots[slot] == 0 || Find(_slots[slot]) is not { } voice)
            {
                continue;
            }

            _al.GetSourceProperty(voice.Left, GetSourceInteger.SampleOffset, out int played);

            _busy[slot] = new BusyChannel(
                voice.Entity,
                voice.Channel,
                voice.Plays(sample),
                voice.FromLocalPlayer,

                // `(int)max(0, the speaker volumes)`: the current ones, ramping, as S_Update's ducker reads them too.
                (int)Math.Max(0f, Math.Max(voice.Level.Left.Current, voice.Level.Right.Current)),
                (uint)Math.Max(0, voice.Sound.FrameCount - played),
                voice.Sound.SampleRate,
                voice.Sound.Loops,
                place.DistanceSquared(voice.Level.Origin),
                voice.Delayed);
        }

        int picked = DynamicChannelPick.Pick(
            _busy,
            new ChannelRequest(entity, channel, place.FromLocalPlayer, sample.Loops, place.DistanceSquared(place.Origin)),
            place.VoiceSteal ?? DefaultVoiceSteal,
            _waiting,
            out int waiting);

        // **Each delay-started channel of the sound's own is freed when the new sound starts** (B732): `ch +0xa8` set to
        // `(uint)(long)(delay × 44100f + (float)paintedtime)` unless it holds an earlier one, compared unsigned
        // (FUN_18002a480, 0x18002a725-0x18002a82f). Kept as a long, whose low 32 bits are the engine's.
        if (waiting > 0)
        {
            long at = (long)((delay * MixRate) + (float)Painted);

            foreach (int held in _waiting.AsSpan(0, waiting))
            {
                int voice = _slots[held];

                if (!_freeAt.TryGetValue(voice, out long was) || was == 0 || (uint)at < (uint)was)
                {
                    _freeAt[voice] = at;
                }
            }
        }

        return picked;
    }

    /// <summary>For a test: the painted time a voice waiting out its delay starts at, or null for one started or gone (B732).</summary>
    internal long? StartsAt(int voice) => _startAt.TryGetValue(voice, out long at) ? at : null;

    /// <summary>For a test: where a voice's left ear is in its samples, read back from the device; null for a voice gone.</summary>
    internal int? OffsetOf(int voice)
    {
        if (!_byId.TryGetValue(voice, out Voice found))
        {
            return null;
        }

        _al.GetSourceProperty(found.Left, GetSourceInteger.SampleOffset, out int offset);

        return offset;
    }

    /// <summary>`voice_steal`'s declared default, "2", for a start that names no setting.</summary>
    private static readonly int DefaultVoiceSteal = (int)EngineConVars.ByName("voice_steal").Number;

    /// <summary>Moves a voice's channel to where its entity now is, as `SND_Spatialize` moves `ch->origin` (B730).</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="origin">Where its entity is.</param>
    public void MoveVoice(int voice, (float X, float Y, float Z) origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_byId.TryGetValue(voice, out Voice found))
        {
            found.Level.Origin = origin;
        }
    }

    /// <summary>One ear's source, playing what <see cref="_stereo"/> holds now, not yet started.</summary>
    private uint Source(SoundSample sample, float pitch, float gain, out uint buffer)
    {
        buffer = _al.GenBuffer();
        uint source = _al.GenSource();

        fixed (short* data = _stereo)
        {
            _al.BufferData(buffer, BufferFormat.Stereo16, data, sample.FrameCount * 2 * sizeof(short), sample.SampleRate);
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

        // The ear's speaker volume over 255, at most 1, so OpenAL's default AL_MAX_GAIN of 1 never clamps it. A sound
        // started while speech ducks starts ducked, as the paint scales every channel it mixes (B719).
        _al.SetSourceProperty(source, SourceFloat.Gain, gain);

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

        return source;
    }

    /// <summary>
    /// One ear's gain as the device plays it: that ear's current volume over 255 (B724), times the source's fade, a
    /// non-voice source scaled by the duck (B719), never negative.
    /// </summary>
    /// <param name="sound">What the channel plays, for whether it is a voice source.</param>
    /// <param name="ear">That ear's speaker volume.</param>
    /// <param name="level">The channel's fade.</param>
    /// <remarks>
    /// The floor is taken after the duck, as the paint clamps each speaker volume to 0..255 after the duck's multiply
    /// (<c>FUN_180041fb0</c>, <c>0x180042347-</c>): an unbounded negative <c>snd_ducktovolume</c> silences.
    /// </remarks>
    private float Heard(SoundSample sound, SpeakerVolume ear, Level level)
    {
        float duck = sound.IsVoiceSource ? 1f : DuckGain;

        return Math.Max(0f, ear.Current / 255f * level.Fade * duck);
    }

    /// <summary>The device gains for one voice, from its level now.</summary>
    private bool Hear(Voice voice) =>
        Set(
            voice,
            SourceFloat.Gain,
            Heard(voice.Sound, voice.Level.Left, voice.Level),
            Heard(voice.Sound, voice.Level.Right, voice.Level));

    /// <summary>Aims one voice's volumes at a new gain: at once before its first frame, by the frame's ramp after (B724).</summary>
    private bool Regain(Voice voice, float gain)
    {
        voice.Level.Gain = gain;

        return Reaim(voice);
    }

    /// <summary>
    /// Re-aims a voice at its level's gain and pan. Only a first pass moves the volume the device plays; after it the
    /// next <see cref="Update"/> steps toward the new target and hears it then.
    /// </summary>
    private bool Reaim(Voice voice)
    {
        voice.Level.Aim();

        return !(voice.Level.Left.FirstPass || voice.Level.Right.FirstPass) || Hear(voice);
    }

    /// <summary>
    /// One frame of <c>S_Update</c> over what plays: each channel's volume stepped toward its target (B724), the voice
    /// sources measured, the speech duck stepped (B719), and every voice re-gained.
    /// </summary>
    /// <param name="ducking">The ConVars.</param>
    /// <param name="frameTime">The frame's length in seconds: the real frame time, as the sound services report it.</param>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// `S_Update` (engine.dll <c>0x180030ce0-0x180030d69</c>) spatialises every active channel, which steps its volumes
    /// (<see cref="SpeakerVolume.Step"/>), and then, for one whose source is a voice source, counts it and keeps the
    /// largest of its current speaker volumes, <c>(int)max(0, …)</c>: a speech channel being turned down ducks by where
    /// its ramp is, not where it is going. With <c>snd_spatialize_roundrobin</c> at its default "0" every channel is
    /// spatialised every frame; above it, a channel past its first pass only every 2^n frames (<c>0x180030d74-</c>), not
    /// ported. A voice's pan is the device's law (<see cref="SpeakerPan"/>), re-panned each pass (<see cref="SetVoicePan"/>):
    /// every channel the sound presenter follows, proximity voice chat among them, and every soundscape loop (B731). A
    /// non-proximity voice is at the ears, where no turn moves its pan.
    /// </remarks>
    public void Update(SpeechDucking ducking, float frameTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int speech = 0;
        int loudest = 0;

        // The paint's latch rule, after the frame's dispatches (B732).
        DropServerLatch(frameTime);

        foreach (Voice voice in _playing)
        {
            SpeakerVolume left = voice.Level.Left;
            SpeakerVolume right = voice.Level.Right;
            float wasLeft = left.Current;
            float wasRight = right.Current;
            left.Step(frameTime);
            right.Step(frameTime);

            if (left.Current.CompareTo(wasLeft) != 0 || right.Current.CompareTo(wasRight) != 0)
            {
                _ = Hear(voice);
            }

            // maxss over the channel's speaker volumes (0x180030d0e-0x180030d51), then cvttss2si.
            if (voice.Sound.IsVoiceSource && IsPlaying(voice))
            {
                speech++;
                loudest = Math.Max(loudest, (int)Math.Max(0f, Math.Max(left.Current, right.Current)));
            }
        }

        float duck = DuckGain;
        _ducker.Update(ducking, speech, loudest, frameTime);
        RehearDucked(duck);
    }

    /// <summary>
    /// One frame of <c>Voice_Idle</c>'s overdrive (B549): stepped by the host frame time, and every non-voice channel
    /// re-gained when the duck it plays under moved.
    /// </summary>
    /// <param name="overdriving">The ConVars.</param>
    /// <param name="assigned">How many voice channels are in use, their pad included.</param>
    /// <param name="frameTime">The host frame time: demo time, nothing while paused.</param>
    /// <param name="enabled"><c>voice_enable</c>: off, the scale is unity and the ramp is left where it was.</param>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    public void VoiceIdle(VoiceOverdriving overdriving, int assigned, float frameTime, bool enabled = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float duck = DuckGain;

        if (enabled)
        {
            _overdrive.Idle(overdriving, assigned, frameTime);
        }
        else
        {
            _overdrive.Disable();
        }

        RehearDucked(duck);
    }

    /// <summary>Re-gains every non-voice channel when the duck moved from <paramref name="was"/>.</summary>
    private void RehearDucked(float was)
    {
        if (DuckGain.CompareTo(was) == 0)
        {
            return;
        }

        foreach (Voice voice in _playing)
        {
            if (!voice.Sound.IsVoiceSource)
            {
                _ = Hear(voice);
            }
        }
    }

    /// <summary>The voice source's own fade, applied to its samples rather than its channel's volume, so never ramped.</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="fade">The fade, 1 for none.</param>
    /// <returns>Whether that voice is still playing.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    /// <remarks>
    /// The voice mixer fades the last <c>voice_fadeouttime</c> of what is buffered in the samples it hands the channel
    /// (<c>Voice_GetOutputData</c>, engine.dll 11191711 <c>FUN_1800590b0</c>: each 16-bit sample × <c>1 − i / (n − 1)</c>,
    /// <c>n</c> set by <c>Voice_Idle</c> <c>FUN_180059380</c>), so the fade is neither ramped by the channel's volume nor
    /// measured by the ducker, which reads the channel's volumes.
    /// </remarks>
    public bool SetVoiceFade(int voice, float fade)
    {
        if (Find(voice) is not { } found)
        {
            return false;
        }

        found.Level.Fade = fade;

        return Hear(found);
    }

    /// <summary>The volumes a voice's channel holds and is moving to in each ear, as the last frame left them (B724).</summary>
    internal (SpeakerVolume Left, SpeakerVolume Right) VolumesOf(int voice) => (_byId[voice].Level.Left, _byId[voice].Level.Right);

    /// <summary>Re-pans one voice, as each frame's spatialisation does: both ears re-aimed at the gain held (B724).</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="leftPan">The left ear's share, from <see cref="SpeakerPan"/>.</param>
    /// <param name="rightPan">The right ear's share.</param>
    /// <returns>Whether that voice is still playing.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    public bool SetVoicePan(int voice, float leftPan, float rightPan)
    {
        if (Find(voice) is not { } found)
        {
            return false;
        }

        found.Level.Pan = (leftPan, rightPan);

        return Reaim(found);
    }

    /// <summary>The handles of the voices held, finished or not, for an instrument.</summary>
    internal IReadOnlyCollection<int> Voices => _byId.Keys;

    /// <summary>The gains the device holds for a voice's two ears, read back from it: what the duck left them at.</summary>
    internal (float Left, float Right) GainsOf(int voice)
    {
        _al.GetSourceProperty(_byId[voice].Left, SourceFloat.Gain, out float left);
        _al.GetSourceProperty(_byId[voice].Right, SourceFloat.Gain, out float right);

        return (left, right);
    }

    /// <summary>The louder of a voice's two device gains.</summary>
    internal float GainOf(int voice)
    {
        (float left, float right) = GainsOf(voice);

        return Math.Max(left, right);
    }

    /// <summary>Re-gains one voice, as the engine's mixer gains one channel (B545).</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="gain">The gain.</param>
    /// <returns>Whether that voice is still playing.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    public bool SetVoiceGain(int voice, float gain) =>
        Find(voice) is { } found && Regain(found, gain);

    /// <summary>Changes one voice's rate, as `S_AlterChannel` changes one channel's pitch.</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="pitch">The rate, 1 unshifted.</param>
    /// <returns>Whether that voice is still playing.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    public bool SetVoicePitch(int voice, float pitch) =>
        Find(voice) is { } found && Set(found, SourceFloat.Pitch, pitch, pitch);

    /// <summary>The next free handle: never 0, which means "nothing started", and never one still sounding after a wrap.</summary>
    private int NextVoice()
    {
        int id;

        do
        {
            id = _nextVoice;
            _nextVoice = _nextVoice == int.MaxValue ? 1 : _nextVoice + 1;
        }
        while (_byId.ContainsKey(id));

        return id;
    }

    /// <summary>A voice still sounding, or null; one that has finished is reaped here, so "alive" means audible.</summary>
    /// <remarks>
    /// A finished source stays in <see cref="_playing"/> until <see cref="Reclaim"/>, and answering for it would make an
    /// `SND_CHANGE_VOL` on ended music alter nothing instead of starting it, as `S_AlterChannel` matching nothing does.
    /// </remarks>
    private Voice? Find(int voice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_byId.TryGetValue(voice, out Voice found))
        {
            return null;
        }

        if (IsPlaying(found))
        {
            return found;
        }

        Free(_playing.IndexOf(found));

        return null;
    }

    /// <summary>How many static channels are taken: <c>CHAN_STATIC</c> and soundscape voices that have not ended.</summary>
    private int StaticVoices()
    {
        int taken = 0;

        foreach (Voice voice in _playing)
        {
            if (!IsDynamic(voice.Entity, voice.Channel) && IsPlaying(voice))
            {
                taken++;
            }
        }

        return taken;
    }

    /// <summary>Whether a voice is still a channel: playing, paused or not yet started. Only a stopped source has ended.</summary>
    private bool IsPlaying(Voice voice)
    {
        // The two ears start together, at one pitch, from buffers of one length, so they end together.
        _al.GetSourceProperty(voice.Left, GetSourceInteger.SourceState, out int state);

        return state != (int)SourceState.Stopped;
    }

    /// <summary>One property on both ears, applied by the mixer at the same update.</summary>
    /// <remarks>
    /// **Between a suspend and a process**, which OpenAL Soft makes deferred updates: two plain sets can straddle a mixer
    /// update, and a pitch applied to one ear a period before the other would leave the two a few samples apart for the
    /// rest of a loop.
    /// </remarks>
    private bool Set(Voice voice, SourceFloat property, float left, float right)
    {
        _alc.SuspendContext(_context);
        _al.SetSourceProperty(voice.Left, property, left);
        _al.SetSourceProperty(voice.Right, property, right);
        _alc.ProcessContext(_context);

        return true;
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
    /// **The gain only; the pan is <see cref="SetVoicePan"/>'s**, by handle (B724). A voice gained by
    /// key here keeps the pan it started with.
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

            _ = Regain(voice, gain);
            found = true;
        }

        return found;
    }

    /// <summary>Changes a playing sound's rate — `SND_CHANGE_PITCH`, which `S_AlterChannel` applies in place.</summary>
    /// <param name="entity">The entity the sound belongs to.</param>
    /// <param name="channel">The channel it is playing on.</param>
    /// <param name="pitch">The rate, 1 unshifted.</param>
    /// <returns>Whether a voice was found and updated.</returns>
    /// <exception cref="ObjectDisposedException">The output has been disposed.</exception>
    public bool SetPitch(int entity, int channel, float pitch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool found = false;

        foreach (Voice voice in _playing)
        {
            if (voice.Entity != entity || voice.Channel != channel)
            {
                continue;
            }

            _ = Set(voice, SourceFloat.Pitch, pitch, pitch);
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

            Free(index);
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

        Mix();

        int freed = 0;

        for (int index = _playing.Count - 1; index >= 0; index--)
        {
            if (IsPlaying(_playing[index]))
            {
                continue;
            }

            Free(index);
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

        foreach (Voice voice in _playing)
        {
            Release(voice);
        }

        _playing.Clear();
        _byId.Clear();
        _startAt.Clear();
        _freeAt.Clear();
        Array.Clear(_slots);
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
        if (sample.FrameCount <= 0)
        {
            return [];
        }

        short[] stereo = new short[sample.FrameCount * 2];
        _ = ToStereo16(sample, leftGain, rightGain, stereo);
        return stereo;
    }

    /// <summary>The same conversion written into a caller's buffer, at least two shorts a frame (finding 80).</summary>
    /// <param name="sample">The decoded sound.</param>
    /// <param name="leftGain">Gain for the left ear.</param>
    /// <param name="rightGain">Gain for the right ear.</param>
    /// <param name="stereo">Where to write; reused, so starting a sound allocates no array.</param>
    /// <returns>How many shorts were written, from the start of <paramref name="stereo"/>.</returns>
    internal static int ToStereo16(SoundSample sample, float leftGain, float rightGain, Span<short> stereo)
    {
        ReadOnlySpan<float> samples = sample.Samples.Span;
        ReadOnlySpan<short> pcm = sample.Pcm16.Span;
        bool held16 = !sample.Pcm16.IsEmpty;
        int frames = sample.FrameCount;

        if (frames <= 0)
        {
            return 0;
        }

        // A WAV held as 16-bit is read back as the float the float path stored: exactly s / 32768 (SoundSample.Pcm16).
        float At(ReadOnlySpan<float> asFloat, ReadOnlySpan<short> asShort, int index) => held16 ? asShort[index] / 32768f : asFloat[index];

        for (int frame = 0; frame < frames; frame++)
        {
            // A mono source feeds both ears and the two gains do the panning; a stereo source
            // already carries its own image and each side is only scaled.
            float left = At(samples, pcm, frame * sample.Channels);
            float right = sample.Channels > 1 ? At(samples, pcm, (frame * sample.Channels) + 1) : left;

            stereo[frame * 2] = ToPcm(left * leftGain);
            stereo[(frame * 2) + 1] = ToPcm(right * rightGain);
        }

        return frames * 2;
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
    int IAudioSink.Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel) =>
        Play(sample, leftPan, rightPan, gain, pitch, entity, channel);

    /// <inheritdoc/>
    int IAudioSink.PlayAt(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel,
        SoundPlace place) =>
        Play(sample, leftPan, rightPan, gain, pitch, entity, channel, place);

    /// <inheritdoc/>
    void IAudioSink.MoveVoice(int voice, (float X, float Y, float Z) origin) => MoveVoice(voice, origin);

    /// <inheritdoc/>
    bool IAudioSink.HandsOutVoices => true;

    /// <inheritdoc/>
    bool IAudioSink.SetVoiceGain(int voice, float gain) => SetVoiceGain(voice, gain);

    /// <inheritdoc/>
    bool IAudioSink.SetVoicePitch(int voice, float pitch) => SetVoicePitch(voice, pitch);

    /// <inheritdoc/>
    bool IAudioSink.SetVoicePan(int voice, float leftPan, float rightPan) => SetVoicePan(voice, leftPan, rightPan);

    /// <inheritdoc/>
    bool IAudioSink.SetGain(int entity, int channel, float gain) => SetGain(entity, channel, gain);

    /// <inheritdoc/>
    bool IAudioSink.SetPitch(int entity, int channel, float pitch) => SetPitch(entity, channel, pitch);

    /// <inheritdoc/>
    void IAudioSink.Silence(int entity, int channel) => Stop(entity, channel);

    /// <inheritdoc/>
    int IAudioSink.Silence(int entity, int channel, SoundSample sample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The first match in start order, and only it — `S_AlterChannel` returns after acting on one channel
        // (FUN_18002aa20: `if ((flags & 0x200) == 0) { result = 1; goto done; }` after the first hit).
        // A voice that has finished is no channel to the engine — its mixer freed it — so it is reaped here, not matched.
        for (int index = 0; index < _playing.Count;)
        {
            Voice voice = _playing[index];

            if (voice.Entity != entity || voice.Channel != channel || !voice.Plays(sample))
            {
                index++;
                continue;
            }

            bool playing = IsPlaying(voice);
            Free(index);

            if (playing)
            {
                return voice.Id;
            }
        }

        return 0;
    }

    /// <summary>Stops a voice, releases its source and buffer, and forgets its handle.</summary>
    private void Free(int index)
    {
        Voice voice = _playing[index];
        Release(voice);

        _playing.RemoveAt(index);
        _byId.Remove(voice.Id);
        _startAt.Remove(voice.Id);
        _freeAt.Remove(voice.Id);

        if (voice.Slot >= 0)
        {
            _slots[voice.Slot] = 0;
        }
    }

    /// <summary>Stops a voice's two sources and deletes them and their buffers.</summary>
    private void Release(Voice voice)
    {
        // **Detached before the buffer is deleted**, which OpenAL requires: a buffer still attached to a source is in
        // use, and deleting it is an error the driver reports through alGetError rather than by failing the call.
        foreach ((uint source, uint buffer) in (ReadOnlySpan<(uint, uint)>)[(voice.Left, voice.LeftBuffer), (voice.Right, voice.RightBuffer)])
        {
            if (source != 0)
            {
                _al.SourceStop(source);
                _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                _al.DeleteSource(source);
            }

            _al.DeleteBuffer(buffer);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **The listener's gain, which OpenAL applies to the whole mix** — where Quake's `S_TransferPaintBuffer`, which the
    /// closed engine descends from, scales the painted buffer by `volume` (interpolated lineage, not read). One call
    /// covers sounds already playing, as a master volume must. Read back from the device, never from a field.
    /// </remarks>
    public float MasterGain
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _al.GetListenerProperty(ListenerFloat.Gain, out float gain);

            return gain;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _al.SetListenerProperty(ListenerFloat.Gain, value < 0f ? 0f : value);
        }
    }

    /// <summary>A device that renders into memory rather than a speaker (`ALC_SOFT_loopback`), for a test that reads the mix.</summary>
    /// <param name="rate">Frames a second.</param>
    /// <param name="log">Where an ALC call that fails by return value is reported, as for <see cref="TryCreate"/>.</param>
    /// <returns>The output, or null when the library has no loopback.</returns>
    /// <remarks>
    /// **Its context is made current for this THREAD only** (`ALC_EXT_thread_local_context`, which OpenAL Soft has), so
    /// a test that opens one does not take the process's context from a viewer's output; without the extension the
    /// process's context is taken and the previous one put back on dispose. Failures answer null as <see cref="TryCreate"/>
    /// does, so a test ignores rather than errors.
    /// </remarks>
    internal static AudioOutput? TryCreateLoopback(int rate, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            // Ignored for TryCreate's reason.
            _ = NativeLibraryResolver.TryLoadOpenAl();

            // OpenAL Soft by name: the loopback is its extension, and a system router need not export it.
            AL al = AL.GetApi(soft: true);
            ALContext alc = ALContext.GetApi(soft: true);
            delegate* unmanaged<byte*, Device*> open =
                (delegate* unmanaged<byte*, Device*>)alc.Context.GetProcAddress("alcLoopbackOpenDeviceSOFT");
            delegate* unmanaged<Device*, void*, int, void> render =
                (delegate* unmanaged<Device*, void*, int, void>)alc.Context.GetProcAddress("alcRenderSamplesSOFT");
            delegate* unmanaged<Context*, byte> setThreadContext =
                (delegate* unmanaged<Context*, byte>)alc.Context.GetProcAddress("alcSetThreadContext");

            Device* device = open is null || render is null ? null : open(null);

            if (device is null)
            {
                return null;
            }

            // ALC_FORMAT_CHANNELS_SOFT stereo, ALC_FORMAT_TYPE_SOFT float, ALC_FREQUENCY (alext.h).
            int* attributes = stackalloc int[] { 0x1990, 0x1501, 0x1991, 0x1406, 0x1007, rate, 0 };
            Context* context = alc.CreateContext(device, attributes);

            if (context is null)
            {
                CloseDevice(alc, device, log);
                return null;
            }

            Context* previous;
            delegate* unmanaged<Context*> getThreadContext =
                (delegate* unmanaged<Context*>)alc.Context.GetProcAddress("alcGetThreadContext");

            if (setThreadContext is not null && getThreadContext is not null)
            {
                previous = getThreadContext();

                // ALC_TRUE is 1; a failure leaves the thread's context as it was.
                if (setThreadContext(context) == 0)
                {
                    ReleaseUncurrent(alc, device, context, log, "alcSetThreadContext");
                    return null;
                }
            }
            else
            {
                setThreadContext = null;
                previous = alc.GetCurrentContext();

                if (!alc.MakeContextCurrent(context))
                {
                    ReleaseUncurrent(alc, device, context, log, "alcMakeContextCurrent");
                    return null;
                }
            }

            return new AudioOutput(al, alc, device, context, log)
            {
                _render = render,
                _loopbackRate = rate,
                _setThreadContext = setThreadContext,
                _getThreadContext = getThreadContext,
                _previous = previous,
            };
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            // Silk.NET's "could not load" (B217), as in TryCreate.
            return null;
        }
    }

    /// <summary>`alcRenderSamplesSOFT`, resolved once by <see cref="TryCreateLoopback"/>; null on a speaker.</summary>
    private delegate* unmanaged<Device*, void*, int, void> _render;

    /// <summary>`alcSetThreadContext` when a loopback took its thread's context with it; null otherwise.</summary>
    private delegate* unmanaged<Context*, byte> _setThreadContext;

    /// <summary>`alcGetThreadContext`, beside <see cref="_setThreadContext"/>.</summary>
    private delegate* unmanaged<Context*> _getThreadContext;

    /// <summary>The context (the thread's, or the process's without the extension) before this one took it, put back on dispose.</summary>
    private Context* _previous;

    /// <summary>Renders the next frames of a loopback device's mix, interleaved stereo (`alcRenderSamplesSOFT`).</summary>
    internal float[] Render(int frames)
    {
        if (_render is null)
        {
            throw new InvalidOperationException("only a loopback output renders into memory");
        }

        float[] mix = new float[frames * 2];
        int done = 0;

        // **Split at each start or free that falls due inside it** (B732), with the frees and starts made there, as the
        // engine's mixer meets a channel's time within its paint; a block rendered whole would begin a waiting voice a block
        // late. The latch rule is the host frame's (`Update`), not the block's.
        while (done < frames)
        {
            Mix();

            int chunk = frames - done;

            if (NextDue() is { } due)
            {
                chunk = (int)Math.Clamp((long)Math.Ceiling((due - Painted) * (double)_loopbackRate / MixRate), 1, chunk);
            }

            fixed (float* into = &mix[done * 2])
            {
                _render(_device, into, chunk);
            }

            _rendered += chunk;
            done += chunk;
        }

        Mix();

        return mix;
    }

    /// <summary>For a test: moves a loopback's clock on without rendering, as hours of play would.</summary>
    internal void SkipRendered(long frames) => _rendered += frames;

    /// <summary>The earliest start or free still ahead of the painted time, or null for none.</summary>
    private long? NextDue()
    {
        long painted = Painted;
        long? next = null;

        foreach (long at in _startAt.Values)
        {
            if (at > painted && (next is not { } soonest || at < soonest))
            {
                next = at;
            }
        }

        foreach (long at in _freeAt.Values)
        {
            if (at > painted && (next is not { } soonest || at < soonest))
            {
                next = at;
            }
        }

        return next;
    }

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

        // Put back what was found only while this one is still current: a newer output that took the context since
        // keeps it. **Per thread**: a loopback disposed on a thread other than the one that opened it leaves that
        // thread's context alone, since `alcSetThreadContext` only reaches the calling thread.
        if (_setThreadContext is not null)
        {
            if (_getThreadContext() == _context && _setThreadContext(_previous) == 0)
            {
                // A failed set leaves the current context unchanged (the ALC rules), so this output's own context is still current.
                _log.LogError("alcSetThreadContext could not put back the context this output found; this output's context stays current on that thread");
            }
        }
        else if (_alc.GetCurrentContext() == _context && !_alc.MakeContextCurrent(_previous))
        {
            // A failed make-current leaves the current context unchanged (the ALC rules), so this output's own context is still current.
            _log.LogError("alcMakeContextCurrent could not put back the context this output found; this output's context stays current in the process");
        }

        _alc.DestroyContext(_context);
        CloseDevice(_alc, _device, _log);

        _al.Dispose();
        _alc.Dispose();

        _disposed = true;
    }

    /// <summary>Closes a device, reporting a refusal rather than dropping it.</summary>
    /// <remarks>
    /// `alcCloseDevice` answers false while the device still has contexts, or for a device it does not know. Nothing
    /// can be done about it at this point, but a device left open keeps the endpoint busy, and the line says why.
    /// </remarks>
    private static void CloseDevice(ALContext alc, Device* device, ILogger log)
    {
        if (!alc.CloseDevice(device))
        {
            log.LogWarning("alcCloseDevice refused to close the audio device; it stays open until the process exits");
        }
    }

    /// <summary>Reports a context that could not be made current, and releases it and its device.</summary>
    /// <remarks>
    /// **The failure path, not a warning.** No context being current makes every later AL call fail without an
    /// exception, so an output returned from here would look open and play nothing. Null instead, which the caller
    /// already reports as "no audio device", and the line here says which call refused.
    /// </remarks>
    private static void ReleaseUncurrent(ALContext alc, Device* device, Context* context, ILogger log, string call)
    {
        log.LogError("{Call} refused the context just created; audio output is not opened", call);
        alc.DestroyContext(context);
        CloseDevice(alc, device, log);
    }
}
