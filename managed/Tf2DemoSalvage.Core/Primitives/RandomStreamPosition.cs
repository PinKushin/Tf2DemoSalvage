namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>A place in a <see cref="UniformRandomStream"/>: the seed it was given and how many numbers it had made since.</summary>
/// <param name="Seed">The seed, as given to <see cref="UniformRandomStream.SetSeed"/>.</param>
/// <param name="Drawn">How many numbers the generator had made after it.</param>
public readonly record struct RandomStreamPosition(int Seed, int Drawn);
