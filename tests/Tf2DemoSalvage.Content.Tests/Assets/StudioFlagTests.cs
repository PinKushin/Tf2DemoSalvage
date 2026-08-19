using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every studio flag bit this project acts on, checked against <c>studio.h</c>.
/// </summary>
/// <remarks>
/// **The animation bits select a decoder, so a wrong one desynchronises a whole track.**
/// <c>STUDIO_ANIM_RAWROT</c> and <c>STUDIO_ANIM_RAWROT2</c> describe the same field at different
/// precisions — six bytes against eight — and sit four bits apart. Reading one as the other
/// consumes the wrong width and decodes every bone after it in that track from the wrong offset.
/// The result is a complete pose, in the wrong shape.
///
/// **The sequence bits are quieter than that.** A missed <c>STUDIO_LOOPING</c> stops an animation on
/// its last frame instead of repeating, which reads as a player freezing mid-stride.
/// </remarks>
public sealed class StudioFlagTests
{
    /// <summary>Where the engine declares them.</summary>
    private const string Studio = "src/public/studio.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryFlagWeActOn_HasTheEnginesValue()
    {
        IReadOnlyDictionary<string, int> engine = Declared();

        (string Name, int Ours)[] claims =
        [
            ("STUDIO_ANIM_RAWPOS", StudioFlags.AnimationRawPosition),
            ("STUDIO_ANIM_RAWROT", StudioFlags.AnimationRawRotation),
            ("STUDIO_ANIM_ANIMPOS", StudioFlags.AnimationAnimatedPosition),
            ("STUDIO_ANIM_ANIMROT", StudioFlags.AnimationAnimatedRotation),
            ("STUDIO_ANIM_DELTA", StudioFlags.AnimationDelta),
            ("STUDIO_ANIM_RAWROT2", StudioFlags.AnimationRawRotation64),
            ("STUDIO_LOOPING", StudioFlags.SequenceLooping),
            ("STUDIO_OVERRIDE", StudioFlags.SequenceForwardDeclared),
        ];

        List<string> wrong = [];

        foreach ((string name, int ours) in claims)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not declared by the engine at all");
            }
            else if (theirs != ours)
            {
                wrong.Add($"{name}: we use 0x{ours:X2}, the engine declares 0x{theirs:X2}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void StudioFlags_TheTwoRotationPrecisions_AreDistinctBits()
    {
        // **Stated separately because they mean the same thing at different widths.** Both say "a
        // raw quaternion follows"; only their values say how many bytes it is. Anything that folded
        // them together — a mask, a range test, a helper that took "raw rotation" as one concept —
        // would read Quaternion64 tracks two bytes short per bone.
        StudioFlags.AnimationRawRotation.ShouldNotBe(StudioFlags.AnimationRawRotation64);

        (StudioFlags.AnimationRawRotation & StudioFlags.AnimationRawRotation64)
            .ShouldBe(0, "they must be testable independently");
    }

    [Test]
    public void StudioFlags_TheAnimationFlags_AreSingleDistinctBitsInOneByte()
    {
        // The control. These six live in a byte-wide field in mstudioanim_t, so a value that needed
        // more than eight bits would mean the field itself was misread — and two sharing a bit
        // would test fine individually and misbehave in combination.
        int[] animation =
        [
            StudioFlags.AnimationRawPosition,
            StudioFlags.AnimationRawRotation,
            StudioFlags.AnimationAnimatedPosition,
            StudioFlags.AnimationAnimatedRotation,
            StudioFlags.AnimationDelta,
            StudioFlags.AnimationRawRotation64,
        ];

        int seen = 0;

        foreach (int flag in animation)
        {
            System.Numerics.BitOperations.PopCount((uint)flag).ShouldBe(1);
            (seen & flag).ShouldBe(0, $"0x{flag:X2} reuses a bit already taken");
            seen |= flag;
        }

        seen.ShouldBeLessThanOrEqualTo(0xFF, "the flags field is one byte");
    }

    /// <summary>Every studio flag the engine declares.</summary>
    private static IReadOnlyDictionary<string, int> Declared()
    {
        IReadOnlyDictionary<string, int> values = SourceSdk.Constants(Studio);

        // The instrument before its answer.
        values.Keys.Count(name => name.StartsWith("STUDIO_", StringComparison.Ordinal))
            .ShouldBeGreaterThan(15, $"no studio flags were extracted from {Studio}");

        return values;
    }
}
