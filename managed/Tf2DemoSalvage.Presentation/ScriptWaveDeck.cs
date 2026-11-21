using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Each soundscript entry's per-wave <c>available</c> flags — how the client deals an <c>rndwave</c> (B503) — and the
/// `GetParametersForSound` that deals it, drawn when the sound plays (B782).
/// </summary>
/// <remarks>
/// **Disassembly, x64 <c>soundemittersystem.dll</c>.** FUN_180005680 lists, in ascending order, the entry's waves whose
/// gender matches and whose <c>available</c> byte is set; when none is, it sets every matching wave's byte again and
/// lists them all; it returns <c>list[ RandomInt( 0, count - 1 ) ]</c>. Its caller <c>GetParametersForSound</c>
/// (FUN_180003370) draws the volume and the pitch before it and the soundlevel after it, and clears the picked wave's byte
/// only when <c>isbeingemitted</c> is set. Gender is moot here: TF2's client sounds are <c>GENDER_NONE</c>, which every
/// wave matches.
///
/// **The deal and the draws are one call, so both happen here, when the sound plays** (B782). `RandomInt` takes no number
/// for a span of one and draws again on a refusal, so how many numbers the wave takes — and with it the soundlevel and
/// every later draw on the stream — is known only against the flags at that moment. Each sound carries its stream
/// (<see cref="ScriptWaveDraw.Stream"/>) instead of its numbers.
///
/// **One instance for everything the client emits**, because the flags live on the entry: an explosion, an impact and
/// an animation event naming one script draw from one deck, in the order the client makes them.
/// </remarks>
public sealed class ScriptWaveDeck
{
    private readonly Dictionary<string, bool[]> _available = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What each draw was dealt, for the stop or change that names it and the draw that follows it.</summary>
    private readonly Dictionary<ScriptWaveDraw, Dealt> _dealt = [];

    /// <summary>The stream a deal draws from, moved to the sound's position — so dealing again draws the same numbers.</summary>
    private readonly UniformRandomStream _stream = new();

    /// <summary>How many of a script's waves are available — FUN_180005680's list, reset when it would be empty.</summary>
    /// <param name="script">The entry's name.</param>
    /// <param name="waveCount">How many waves it has; at least one.</param>
    /// <returns>The count, at least one.</returns>
    public int Available(string script, int waveCount)
    {
        bool[] available = Flags(script, waveCount);
        int count = 0;

        foreach (bool one in available)
        {
            count += one ? 1 : 0;
        }

        if (count == 0)
        {
            Array.Fill(available, true);
            count = waveCount;
        }

        return count;
    }

    /// <summary>Picks a wave of a script, as FUN_180005680 does, and clears it when the sound is being emitted.</summary>
    /// <param name="script">The entry's name.</param>
    /// <param name="waveCount">How many waves it has; at least one.</param>
    /// <param name="wanted">`RandomInt( 0, count - 1 )`: the place in the list of available waves.</param>
    /// <param name="emitted">`isbeingemitted`.</param>
    /// <returns>The index of the wave picked.</returns>
    public int Pick(string script, int waveCount, int wanted, bool emitted)
    {
        Available(script, waveCount);

        bool[] available = Flags(script, waveCount);
        int picked = 0;

        for (int index = 0; index < waveCount; index++)
        {
            if (available[index] && wanted-- == 0)
            {
                picked = index;
                break;
            }
        }

        if (emitted)
        {
            available[picked] = false;
        }

        return picked;
    }

    /// <summary>A script sound with what its `GetParametersForSound` draws now; anything else as it is.</summary>
    /// <param name="sound">The sound, as its producer made it: drawn against a full deck.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>The sound with its wave and soundlevel, and — for a draw after another on its stream — its volume and pitch.</returns>
    /// <remarks>
    /// A first draw's volume and pitch come before any wave, so its producer already has them, and a producer may have
    /// replaced them (a footstep's volume, a patch's pitch): those are left alone.
    /// </remarks>
    public SceneSound Deal(SceneSound sound, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        if (sound.WaveDraw is not { } draw || !scripts.TryGetValue(draw.Script, out SoundScriptEntry entry) || entry.Waves.Count == 0)
        {
            return sound;
        }

        StartAt(draw, scripts);

        Parameters drawn = Draw(entry, draw.Emitted, known: null);
        string wave = entry.Waves[drawn.Wave];
        _dealt[draw] = new Dealt(wave, drawn.Available, drawn.Volume);

        SceneSound dealt = sound with { Name = wave, SoundLevel = drawn.SoundLevel };

        if (draw.After is null)
        {
            return dealt;
        }

        return draw.Scaled ? dealt with { Volume = sound.Volume * drawn.Volume } : dealt with { Volume = drawn.Volume, Pitch = drawn.Pitch };
    }

    /// <summary>
    /// A stop, a change, or a re-established loop, named as the start it belongs to was dealt — `CSoundPatch` and
    /// `S_AlterChannel` act on the wave already playing, and pick nothing. A patch's new volume multiplies the script volume
    /// its start drew.
    /// </summary>
    /// <param name="sound">The sound.</param>
    /// <returns>It, named.</returns>
    public SceneSound Named(SceneSound sound)
    {
        if (sound.WaveDraw is not { } draw || !_dealt.TryGetValue(draw, out Dealt dealt))
        {
            return sound;
        }

        return draw.Scaled ? sound with { Name = dealt.Wave, Volume = sound.Volume * dealt.Volume } : sound with { Name = dealt.Wave };
    }

    /// <summary>Every wave available again — the state of a client that has emitted nothing.</summary>
    public void Clear() => _available.Clear();

    /// <summary>Puts <see cref="_stream"/> where <paramref name="draw"/>'s numbers start.</summary>
    /// <remarks>
    /// A draw after another starts where that one ended, which is that one's draws over again: with the count it was dealt
    /// against when it was dealt (a shrapnel before its impact), or — when it was never a sound of its own (`PhysFrictionSound`'s
    /// draw before its patch) — against the flags now, which a read does not change.
    /// </remarks>
    private void StartAt(ScriptWaveDraw draw, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        if (draw.After is not { } before)
        {
            _stream.MoveTo(draw.Stream);
            return;
        }

        StartAt(before, scripts);

        if (scripts.TryGetValue(before.Script, out SoundScriptEntry entry) && entry.Waves.Count > 0)
        {
            Draw(entry, emitted: false, _dealt.TryGetValue(before, out Dealt dealt) ? dealt.Available : null);
        }
    }

    /// <summary>`GetParametersForSound` (FUN_180003370) from <see cref="_stream"/>, in its order.</summary>
    /// <param name="entry">The script entry, which has at least one wave.</param>
    /// <param name="emitted">`isbeingemitted`, which clears the wave picked.</param>
    /// <param name="known">The count of available waves a draw being replayed saw, which leaves the flags alone; null to deal.</param>
    private Parameters Draw(SoundScriptEntry entry, bool emitted, int? known)
    {
        // `CSoundParameters::pitch` and `soundlevel` are ints, so those float draws truncate on assignment.
        float volume = _stream.RandomFloat(entry.Volume.Low, entry.Volume.High);
        int pitch = (int)_stream.RandomFloat(entry.Pitch.Low, entry.Pitch.High);

        // FUN_180005680's `RandomInt( 0, count - 1 )`, through an `IUniformRandomStream` whose slot is a thunk to vstdlib's
        // export (`0x180003c90`): no number for one wave available, another number for a refused one.
        int available = known ?? Available(entry.Name, entry.Waves.Count);
        int wanted = _stream.RandomInt(0, available - 1);
        int wave = known is null ? Pick(entry.Name, entry.Waves.Count, wanted, emitted) : -1;

        int soundLevel = (int)_stream.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);

        return new Parameters(volume, pitch, wave, soundLevel, available);
    }

    private bool[] Flags(string script, int waveCount)
    {
        if (!_available.TryGetValue(script, out bool[]? available) || available.Length != waveCount)
        {
            available = new bool[waveCount];
            Array.Fill(available, true);
            _available[script] = available;
        }

        return available;
    }

    /// <summary>One `CSoundParameters`, and how many waves its pick chose among.</summary>
    private readonly record struct Parameters(float Volume, int Pitch, int Wave, int SoundLevel, int Available);

    /// <summary>A dealt draw: its wave, how many waves were available to it, and the script volume it drew.</summary>
    private readonly record struct Dealt(string Wave, int Available, float Volume);
}
