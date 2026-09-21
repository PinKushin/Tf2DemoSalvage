using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>A `random->RandomFloat` that gives the same draws for the same seed (B415).</summary>
/// <remarks>
/// **The trade, named:** the engine draws from the client's global uniform stream, whose state depends on every draw
/// before it and cannot be reproduced from a demo. A stream seeded by what it is drawing for — a shot and a bullet —
/// gives the same answer every time that thing is replayed, which a seek needs, at the cost of a different, equally
/// valid draw.
/// </remarks>
public static class SeededDraw
{
    /// <summary>A stream of draws for one seed: xorshift32, one step per draw.</summary>
    /// <param name="seed">What the draws are for.</param>
    /// <returns>`RandomFloat( least, most )`.</returns>
    public static Func<float, float, float> For(int seed)
    {
        uint state = unchecked((uint)seed * 2654435761u);

        return (least, most) =>
        {
            // Never zero, or xorshift stays there.
            state = state == 0 ? 1 : state;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            return least + ((state >> 8) * (1f / (1 << 24)) * (most - least));
        };
    }

    /// <summary>The seed for one bullet of one shot.</summary>
    /// <param name="shot">The shot's index.</param>
    /// <param name="bullet">The bullet.</param>
    /// <param name="salt">Which use, so two uses of one bullet draw differently.</param>
    /// <returns>The seed.</returns>
    public static int Of(int shot, int bullet, int salt = 0) => unchecked((((shot * 64) + bullet) * 31) + salt);
}
