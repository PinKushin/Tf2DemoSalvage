namespace Tf2DemoSalvage.Audio;

/// <summary>Somewhere to send sound, so a system that schedules it needs no device.</summary>
/// <remarks>
/// **Three methods, because three is all the soundscape system uses.** Narrow on purpose per the
/// interface-segregation rule: <see cref="AudioOutput"/> also opens a device, reclaims finished
/// voices, stops everything and disposes, and none of that is any business of a caller deciding
/// which loop should be playing.
///
/// **Why it exists at all: <see cref="SoundscapeSystem"/> could be moved out of the window and
/// still not be testable.** The move made it a game system; this makes it an ASSERTABLE one. A
/// concrete <see cref="AudioOutput"/> needs OpenAL and a real device, so a test of "does a fade
/// that finished get stopped" would have needed a sound card — which is how logic ends up untested
/// while looking extracted (B188, B184).
///
/// The engine's own split is the same shape: <c>C_SoundscapeSystem</c> decides what should be
/// playing and calls through <c>enginesound</c>, which is an interface
/// (<c>IEngineSound</c>) rather than the mixer itself.
///
/// **One thread.** Every call comes from the viewer's frame loop (<c>ViewerSession</c>'s frame calls
/// <c>PlaySounds</c>, which steps <c>SoundPresenter</c> and the soundscape), and the settings a pass reads are set
/// from that same UI thread, so no implementation locks. A caller on another thread would need one.
/// </remarks>
public interface IAudioSink
{
    /// <summary>Starts a sound on an entity's channel.</summary>
    /// <param name="sample">The decoded samples.</param>
    /// <param name="leftPan">Left channel scale.</param>
    /// <param name="rightPan">Right channel scale.</param>
    /// <param name="gain">Volume.</param>
    /// <param name="pitch">Playback rate.</param>
    /// <param name="entity">Which entity it belongs to.</param>
    /// <param name="channel">Which channel of that entity.</param>
    /// <returns>The voice's own handle, for <see cref="SetVoiceGain"/>; 0 when nothing was started.</returns>
    /// <remarks>
    /// From a sink whose <see cref="HandsOutVoices"/> is true, 0 means the start was refused and nothing plays; from one
    /// that never hands them out, 0 is all it ever answers and the sound is addressed by entity and channel.
    /// </remarks>
    public int Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel);

    /// <summary>Starts a sound heard from a place, which the channel steal weighs (B730).</summary>
    /// <param name="sample">The decoded samples.</param>
    /// <param name="leftPan">Left channel scale.</param>
    /// <param name="rightPan">Right channel scale.</param>
    /// <param name="gain">Volume.</param>
    /// <param name="pitch">Playback rate.</param>
    /// <param name="entity">Which entity it belongs to.</param>
    /// <param name="channel">Which channel of that entity.</param>
    /// <param name="place">Its origin, the listener and whether it is the local player's.</param>
    /// <returns>As <see cref="Play"/>, which a sink that steals no channel is asked instead.</returns>
    public int PlayAt(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel,
        SoundPlace place) =>
        Play(sample, leftPan, rightPan, gain, pitch, entity, channel);

    /// <summary>Moves a voice's channel to where its entity now is, for the channel steal (B730).</summary>
    /// <param name="voice">The handle <see cref="PlayAt"/> returned.</param>
    /// <param name="origin">Where its entity is.</param>
    public void MoveVoice(int voice, (float X, float Y, float Z) origin)
    {
        // A sink that steals no channel has no use for where one is.
    }

    /// <summary>Changes a playing sound's volume, for a fade.</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="gain">The new volume.</param>
    /// <returns>Whether anything was playing there to change.</returns>
    public bool SetGain(int entity, int channel, float gain);

    /// <summary>Whether <see cref="Play"/> hands out voice handles, so that 0 from it means "refused".</summary>
    public bool HandsOutVoices => false;

    /// <summary>Changes one voice's volume — one engine channel, so overlapping CHAN_AUTO sounds stay apart (B545).</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="gain">The new volume.</param>
    /// <returns>Whether that voice is still playing.</returns>
    public bool SetVoiceGain(int voice, float gain);

    /// <summary>Changes one voice's playback rate, as `S_AlterChannel` changes the one channel it matched.</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="pitch">The new rate, 1 unshifted.</param>
    /// <returns>Whether that voice is still playing.</returns>
    public bool SetVoicePitch(int voice, float pitch);

    /// <summary>Re-pans one voice, as each frame's spatialisation re-pans every channel (B724).</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="leftPan">The left ear's share, from <see cref="SpeakerPan"/>.</param>
    /// <param name="rightPan">The right ear's share.</param>
    /// <returns>Whether that voice is still playing; a sink with no ears to move answers false.</returns>
    public bool SetVoicePan(int voice, float leftPan, float rightPan) => false;

    /// <summary>Changes a playing sound's playback rate — `SND_CHANGE_PITCH`.</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="pitch">The new rate, 1 unshifted.</param>
    /// <returns>Whether anything was playing there to change.</returns>
    public bool SetPitch(int entity, int channel, float pitch);

    /// <summary>Silences everything at once, for a seek.</summary>
    /// <remarks>
    /// **A seek silences the world rather than fading it.** Sounds are scheduled from the timeline
    /// by tick, so after a jump the ones in flight belong to a moment the viewer has left, and
    /// letting them finish plays the old place over the new one.
    /// </remarks>
    public void SilenceAll();

    /// <summary>Releases voices that have finished, and says how many.</summary>
    /// <returns>How many were freed.</returns>
    public int Reclaim();

    /// <summary>Silences one channel.</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <remarks>
    /// Not <c>Stop</c>: CA1716 refuses a reserved keyword on an interface member, because it makes
    /// the member awkward to implement from other languages. <see cref="AudioOutput"/> keeps its own
    /// <c>Stop</c> and implements this explicitly.
    /// </remarks>
    public void Silence(int entity, int channel);

    /// <summary>Silences the first voice playing one sound on a channel — `SND_STOP` through `S_AlterChannel` (B416).</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="sample">The sound, matched as the engine matches `ch->sfx`: the cached sample for its name.</param>
    /// <returns>The handle of the voice it stopped, so a caller following it forgets that one; 0 when none.</returns>
    public int Silence(int entity, int channel, SoundSample sample);

    /// <summary>The gain over the whole mix — `volume`, or 0 under `snd_mute_losefocus` — applied after every sound's own.</summary>
    public float MasterGain { get; set; }

    /// <summary>
    /// One frame of <c>S_Update</c> over what this sink plays: each channel's volume ramp (B724; <see cref="SpeakerVolume"/>)
    /// and the speech ducker (B719; <see cref="SpeechDucker"/>).
    /// </summary>
    /// <param name="ducking">The ConVars.</param>
    /// <param name="frameTime">The frame's length in seconds.</param>
    public void Update(SpeechDucking ducking, float frameTime)
    {
        // A sink that mixes nothing has no channel to ramp, measure or duck.
    }

    /// <summary>Fades one voice in its samples, outside its channel's volume ramp — the voice mixer's fade.</summary>
    /// <param name="voice">The handle <see cref="Play"/> returned.</param>
    /// <param name="fade">The fade, 1 for none.</param>
    /// <returns>Whether that voice is still playing; never, from a sink that mixes nothing.</returns>
    public bool SetVoiceFade(int voice, float fade) => false;
}
