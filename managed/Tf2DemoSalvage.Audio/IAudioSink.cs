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
    public void Play(
        SoundSample sample,
        float leftPan,
        float rightPan,
        float gain,
        float pitch,
        int entity,
        int channel);

    /// <summary>Changes a playing sound's volume, for a fade.</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="gain">The new volume.</param>
    /// <returns>Whether anything was playing there to change.</returns>
    public bool SetGain(int entity, int channel, float gain);

    /// <summary>Silences one channel.</summary>
    /// <param name="entity">Which entity.</param>
    /// <param name="channel">Which channel.</param>
    /// <remarks>
    /// Not <c>Stop</c>: CA1716 refuses a reserved keyword on an interface member, because it makes
    /// the member awkward to implement from other languages. <see cref="AudioOutput"/> keeps its own
    /// <c>Stop</c> and implements this explicitly.
    /// </remarks>
    public void Silence(int entity, int channel);
}
