using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One joint axis as the SOLVER holds it: radians, in IVP's axis order.</summary>
/// <param name="Minimum">Least rotation, in radians.</param>
/// <param name="Maximum">Greatest rotation, in radians.</param>
public readonly record struct IvpAxisLimit(float Minimum, float Maximum);

/// <summary>
/// Turning a <c>.phy</c>'s joint limits into the three the solver reads (B58, D142).
/// </summary>
/// <remarks>
/// **Read out of `vphysics.dll`, in `CPhysicsConstraint`'s constructor** — the function that takes a
/// `constraint_ragdollparams_t` and fills the constraint's per-axis records. Nothing here is a
/// design decision; every line is a transcription, and four separate things happen to a limit on the
/// way in. Each is invisible if guessed, and each produces a corpse that settles smoothly into the
/// wrong shape rather than an error.
///
/// **One — the axes are REMAPPED.** Each Source axis index goes through a four-byte table at
/// <c>18011f014</c>, which reads <c>00 02 01 03</c>:
///
/// <code>
///   int FUN_180002bb0(int axis)
///   {
///       if (axis &lt; 4) { return (int)(char)(&amp;DAT_18011f014)[axis]; }
///       return 0;
///   }
/// </code>
///
/// So **Y and Z are exchanged** — Source 1 lands in IVP slot 2 and Source 2 in slot 1 — which is the
/// Z-up/Y-up difference between the two engines. A joint built without the swap has its twist and
/// its swing on each other's axes: limits of the right size about the wrong bones.
///
/// **Two — degrees become radians, and SOURCE AXIS 2 is negated.** Two constants sit beside each
/// other, <c>1800eb764</c> = +0.0174532925 and <c>1800eb770</c> = −0.0174532925, and the loop hands
/// the negative one to exactly one axis. Which one is not a guess: axes 0 and 1 are written with
/// <c>fVar5</c> (+π/180) and axis 2 with <c>fVar14</c> (−π/180), in all three of the loop's
/// branches.
///
/// **Three — the negated axis reads its two fields in the OPPOSITE order**, which is the necessary
/// consequence rather than a second quirk: negating <c>[a, b]</c> gives <c>[−b, −a]</c>. The
/// decompilation shows it as the loads being swapped before the same two stores:
///
/// <code>
///   // axes 0 and 1
///   fVar1 = pfVar12[-4];  fVar15 = pfVar12[-5];
///   dest[i*2] = fVar15 * fVar5;  dest[i*2+1] = fVar1 * fVar5;
///
///   // axis 2
///   fVar1 = pfVar12[-5];  fVar15 = pfVar12[-4];
///   dest[i*2] = fVar15 * fVar14;  dest[i*2+1] = fVar1 * fVar14;
/// </code>
///
/// A transcription that negated without exchanging the pair produces an inverted, empty range — a
/// joint that either locks solid or flails, depending which side the solver clamps first.
///
/// **Four — <c>useClockwiseRotations</c> negates every limit and exchanges every pair.** The SDK
/// declares it with only *"HACKHACK: Did this wrong in version one. Fix in the future."*; the binary
/// says what it does, at <c>constraint_ragdollparams_t</c> offset <c>0xB2</c>, by XORing the sign
/// bit of each stored pair and swapping them.
///
/// **It never fires on TF2 content, and that is measured rather than assumed.** In the whole
/// published SDK the field appears exactly twice — its declaration and <c>Defaults()</c> setting it
/// <c>false</c> — and nothing anywhere sets it true. In <c>vphysics.dll</c> the string occurs once,
/// as <c>onlyAngularLimits\0…\0useClockwiseRotations\0</c>: adjacent entries in the save-game
/// datadesc for <c>vphysics_save_constraintragdoll_t</c>, which is a serialisation name rather than
/// a <c>.phy</c> key. The keys the parser really reads are present as controls (<c>xmin</c>,
/// <c>xmax</c>, <c>zfriction</c>); lower-case <c>clockwise</c> does not occur at all.
///
/// **So it is transcribed, dead, and it stays.** It is INPUT rather than a decision of ours —
/// choosing its value would be choosing the meaning of somebody's <c>.phy</c> — and it costs nothing
/// downstream, because it is applied here, once, and the solver never sees it.
/// </remarks>
public static class RagdollJointLimits
{
    /// <summary>Degrees to radians — <c>1800eb764</c>, and its negative at <c>1800eb770</c>.</summary>
    /// <remarks>
    /// **0.0174532925 exactly, as the binary stores it**, rather than <c>MathF.PI / 180f</c>. They
    /// agree to seven digits and this is the number the engine multiplies by.
    /// </remarks>
    public const float DegreesToRadians = 0.0174532925f;

    /// <summary>Source axis index to IVP slot — the table at <c>18011f014</c>, <c>00 02 01 03</c>.</summary>
    /// <param name="axis">The Source axis, 0 to 3.</param>
    /// <returns>The IVP slot it is stored in.</returns>
    /// <remarks>
    /// **Anything outside 0..3 answers 0, which is the engine's own fallback** rather than a guard
    /// invented here: <c>if (axis &lt; 4) { … } return 0;</c>. A negative index reaches the same
    /// answer, because the engine's test is one-sided and this reproduces that.
    /// </remarks>
    public static int Slot(int axis) => axis is >= 0 and < 4 ? Table[axis] : 0;

    /// <summary>The remap table itself, as four bytes in the binary.</summary>
    private static readonly int[] Table = [0, 2, 1, 3];

    /// <summary>Whether Source axis 2 is the one written with the NEGATIVE conversion.</summary>
    /// <param name="axis">The Source axis.</param>
    /// <returns>Whether the negative constant applies.</returns>
    public static bool Negated(int axis) => axis == 2;

    /// <summary>Converts one axis's degree limits into the solver's radians.</summary>
    /// <param name="axis">The SOURCE axis index, which decides both the slot and the sign.</param>
    /// <param name="minimum">The <c>.phy</c>'s <c>xmin</c> for it, in degrees.</param>
    /// <param name="maximum">Its <c>xmax</c>, in degrees.</param>
    /// <param name="clockwise">
    /// <c>constraint_ragdollparams_t::useClockwiseRotations</c>, which negates the result and
    /// exchanges the pair again.
    /// </param>
    /// <returns>The limit as the solver holds it, and the slot it belongs in.</returns>
    public static (int Slot, IvpAxisLimit Limit) Convert(
        int axis, float minimum, float maximum, bool clockwise = false)
    {
        float scale = Negated(axis) ? -DegreesToRadians : DegreesToRadians;

        // **The negated axis takes its two fields the other way round**, which is what keeps the
        // range the right way up once the sign has flipped.
        IvpAxisLimit limit = Negated(axis)
            ? new IvpAxisLimit(maximum * scale, minimum * scale)
            : new IvpAxisLimit(minimum * scale, maximum * scale);

        if (clockwise)
        {
            // Named locals rather than `new IvpAxisLimit(-limit.Maximum, -limit.Minimum)`: the
            // exchange is deliberate, and written inline it trips S2234 — an analyser rule whose
            // whole purpose is to catch arguments passed in the wrong order. It is right to be
            // suspicious of this line; the answer is to say what the values are.
            float negatedLow = -limit.Maximum;
            float negatedHigh = -limit.Minimum;

            limit = new IvpAxisLimit(negatedLow, negatedHigh);
        }

        return (Slot(axis), limit);
    }

    /// <summary>Whether a breakable limit means "no limit".</summary>
    /// <param name="value">A force or torque limit from <c>constraint_breakableparams_t</c>.</param>
    /// <returns>Whether the engine treats it as unlimited.</returns>
    /// <remarks>
    /// **Two spellings of the same thing, and both are in the test.** The constraint counts as
    /// breakable only when a limit is non-zero AND below 1e12 (<c>1800eb76c</c>), so zero means
    /// "no limit" and so does anything at or above 1e12. `constraint_breakableparams_t::Defaults()`
    /// uses the zero one, which is why a reader that only handled the 1e12 spelling would still
    /// look correct on every stock ragdoll.
    /// </remarks>
    public static bool Unlimited(float value) => value == 0f || value >= UnlimitedThreshold;

    /// <summary>The 1e12 at <c>1800eb76c</c>.</summary>
    public const float UnlimitedThreshold = 1e12f;
}
