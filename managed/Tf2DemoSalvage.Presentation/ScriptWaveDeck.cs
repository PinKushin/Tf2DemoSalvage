using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Each soundscript entry's per-wave <c>available</c> flags — how the client deals an <c>rndwave</c> (B503).</summary>
/// <remarks>
/// **Disassembly, x64 <c>soundemittersystem.dll</c>.** FUN_180005680 lists, in ascending order, the entry's waves whose
/// gender matches and whose <c>available</c> byte is set; when none is, it sets every matching wave's byte again and
/// lists them all; it returns <c>list[ RandomInt( 0, count - 1 ) ]</c>. Its caller <c>GetParametersForSound</c>
/// (FUN_180003370) clears the picked wave's byte only when <c>isbeingemitted</c> is set. Gender is moot here: TF2's
/// client sounds are <c>GENDER_NONE</c>, which every wave matches.
///
/// **One instance for everything the client emits**, because the flags live on the entry: an explosion, an impact and
/// an animation event naming one script draw from one deck, in the order the client makes them.
/// </remarks>
public sealed class ScriptWaveDeck
{
    private readonly Dictionary<string, bool[]> _available = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Picks a wave of a script, as FUN_180005680 does, and clears it when the sound is being emitted.</summary>
    /// <param name="script">The entry's name.</param>
    /// <param name="waveCount">How many waves it has; at least one.</param>
    /// <param name="draw">The generator's number, non-negative; the pick is it modulo the waves available.</param>
    /// <param name="emitted">`isbeingemitted`.</param>
    /// <returns>The index of the wave picked.</returns>
    public int Pick(string script, int waveCount, int draw, bool emitted)
    {
        if (!_available.TryGetValue(script, out bool[]? available) || available.Length != waveCount)
        {
            available = new bool[waveCount];
            Array.Fill(available, true);
            _available[script] = available;
        }

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

        int wanted = draw % count;
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

    /// <summary>Every wave available again — the state of a client that has emitted nothing.</summary>
    public void Clear() => _available.Clear();
}
