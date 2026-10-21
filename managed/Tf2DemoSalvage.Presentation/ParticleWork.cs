using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The particle phase's work at one moment, running totals carried from `ParticleEffects` (finding 84).</summary>
/// <param name="Alive">Effects held, summed per frame.</param>
/// <param name="Steps">Steps taken on played frames.</param>
/// <param name="Slept">Frames a burst stood asleep, once per burst.</param>
/// <param name="Gathered">Effects with particles handed to the draw.</param>
public readonly record struct ParticleWork(long Alive, long Steps, long Slept, long Gathered)
{
    /// <summary>The work between an earlier reading and this one, per frame.</summary>
    /// <param name="start">The earlier reading.</param>
    /// <param name="frames">Frames between the two; positive.</param>
    /// <returns>One line.</returns>
    public string Since(ParticleWork start, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);

        double Each(long count) => count / (double)frames;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"particle work a frame: {Each(Alive - start.Alive):0.#} effects alive, {Each(Steps - start.Steps):0.#} steps, " +
            $"{Each(Slept - start.Slept):0.#} asleep, {Each(Gathered - start.Gathered):0.#} handed to the draw");
    }
}
