using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One animation's request that a limb be solved — <c>mstudioikrule_t</c>.
/// </summary>
/// <param name="Type">Which kind of target this is: <see cref="StudioIkRuleType"/>.</param>
/// <param name="Chain">Which of the model's IK chains it drives.</param>
/// <param name="Bone">The bone the target is measured against, for the types that use one.</param>
/// <param name="Slot">Which target slot it occupies, usually the same as the chain.</param>
/// <param name="Height">How high the foot lifts, for a ground rule.</param>
/// <param name="Radius">How wide the contact is, for a ground rule.</param>
/// <param name="Floor">Where the ground is taken to be, relative to the rule's own origin.</param>
/// <param name="Position">The target's position in the rule's own space.</param>
/// <param name="Rotation">The target's rotation there.</param>
/// <param name="CompressedError">Offset to the compressed per-frame error, or zero.</param>
/// <param name="FirstFrame">Which frame the error track starts at — <c>iStart</c>.</param>
/// <param name="ErrorIndex">Offset to an uncompressed error track, or zero.</param>
/// <param name="Start">Where influence begins, in the animation's cycle.</param>
/// <param name="Peak">Where full influence begins.</param>
/// <param name="Tail">Where full influence ends.</param>
/// <param name="End">Where influence ends.</param>
/// <param name="Contact">The cycle at which a footstep makes ground contact.</param>
/// <param name="Drop">How far the foot may drop when reaching.</param>
/// <param name="Top">The top of the foot box.</param>
/// <param name="AttachmentName">Offset to the world attachment's name, for an attachment rule.</param>
/// <remarks>
/// **A rule is what ASKS; a chain is only permission.** Every TF2 player model declares four chains,
/// and that says nothing about whether anything wants them solved — which is why the rules were
/// counted before the solver was written. **705 of the scout's 1012 animations carry rules**, 2035
/// of them (B296).
///
/// **The envelope is the same four numbers an autolayer uses and it is NOT the same mechanism.**
/// `Studio_IKRuleWeight` (<c>bone_setup.cpp:2875</c>) ramps between them exactly as
/// `AddSequenceLayers` does, splined, but it also returns WHICH FRAME of the error track to read —
/// so the same call answers two questions and a port that split them would read the track at the
/// wrong frame.
///
/// **<c>end</c> may exceed one, and that is what lets a rule wrap the loop.** The weight function
/// opens with <c>if (ikRule.end &gt; 1.0f &amp;&amp; flCycle &lt; ikRule.start) flCycle += 1.0f;</c>
/// — a footstep beginning near the end of a walk cycle and finishing after it has looped.
/// </remarks>
public readonly record struct StudioIkRule(
    int Type,
    int Chain,
    int Bone,
    int Slot,
    float Height,
    float Radius,
    float Floor,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z, float W) Rotation,
    int CompressedError,
    int FirstFrame,
    int ErrorIndex,
    float Start,
    float Peak,
    float Tail,
    float End,
    float Contact,
    float Drop,
    float Top,
    int AttachmentName)
{
    /// <summary>Whether this rule carries per-frame error data at all.</summary>
    /// <remarks>
    /// **Without it the rule is disabled**, and Valve asserts on the case:
    /// <c>// no data, disable IK rule / Assert( 0 ); flWeight = 0.0f; return false;</c>
    /// (<c>bone_setup.cpp:3030</c>). Either an uncompressed track or a compressed one will do; a
    /// rule with neither is a model-compiler fault rather than a state to honour.
    /// </remarks>
    public bool HasError => ErrorIndex != 0 || CompressedError != 0;
}

/// <summary>
/// What a rule's target is measured against — the <c>IK_*</c> constants.
/// </summary>
/// <remarks>
/// **<c>studio.h:522</c>.** The type decides where the target comes from, and they are not
/// interchangeable: a `SELF` rule places a hand relative to another bone on the same model, where a
/// `GROUND` rule places a foot in the world and has to survive the model moving under it.
/// </remarks>
public static class StudioIkRuleType
{
    /// <summary><c>IK_SELF</c> — the target is a bone on this same model.</summary>
    /// <remarks>This is what holds a hand on a weapon's grip.</remarks>
    public const int Self = 1;

    /// <summary><c>IK_WORLD</c> — the target is a fixed point in the world.</summary>
    public const int World = 2;

    /// <summary><c>IK_GROUND</c> — the target is the ground under the foot.</summary>
    /// <remarks>
    /// **The only type `Studio_IKAnimationError` will read at zero weight**:
    /// <c>if (pRule->type != IK_GROUND &amp;&amp; flWeight &lt; 0.0001) return false;</c>
    /// (<c>bone_setup.cpp:3005</c>). A ground rule has to keep tracking where the foot was planted
    /// even while it has no influence, because that is the position it will latch back onto.
    /// </remarks>
    public const int Ground = 3;

    /// <summary><c>IK_RELEASE</c> — let go of whatever this chain was holding.</summary>
    public const int Release = 4;

    /// <summary><c>IK_ATTACHMENT</c> — the target is a named attachment point.</summary>
    public const int Attachment = 5;

    /// <summary><c>IK_UNLATCH</c> — stop latching, without releasing.</summary>
    public const int Unlatch = 6;
}

/// <summary>
/// Reads an animation's IK rules, and weights them across a cycle.
/// </summary>
public static class StudioIkRules
{
    /// <summary>Bytes per <c>mstudioikrule_t</c>.</summary>
    /// <remarks>
    /// **152, and the trailing <c>unused[7]</c> is most of what makes it that.** Counting only the
    /// fields that mean something gives 124; the seven reserved ints Valve leaves at the end are on
    /// disk and a stride that omitted them would read every rule after the first from the wrong
    /// place — springy nonsense rather than an exception, which is the failure mode this format
    /// specialises in.
    /// </remarks>
    public const int Stride = 152;

    /// <summary>The IK rules one animation declares.</summary>
    /// <param name="model">The whole <c>.mdl</c> file.</param>
    /// <param name="animation">Which animation, by index within this file.</param>
    /// <returns>Its rules, or empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="animation"/> is negative.</exception>
    /// <remarks>
    /// **<c>ikruleindex</c> is relative to the ANIMATION description**, as every index in this
    /// format is relative to the structure holding it.
    ///
    /// **<c>animblockikruleindex</c> is a second home for the same data and is not read.** Rules
    /// can live in an external animation block instead of the model; a model whose animations are
    /// blocked answers empty here, which is a fact about where the bytes are rather than about the
    /// animation. TF2's player animations are not blocked — measured, 705 of 1012 read back rules
    /// through this path.
    /// </remarks>
    public static IReadOnlyList<StudioIkRule> Read(ReadOnlyMemory<byte> model, int animation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(animation);

        ReadOnlySpan<byte> bytes = model.Span;

        if (animation >= StudioAnimation.Count(model) ||
            bytes.Length < StudioLayout.HeaderAnimationIndexOffset + sizeof(int))
        {
            return [];
        }

        int start = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderAnimationIndexOffset..]) +
            (animation * StudioLayout.AnimationStride);

        if (start < 0 || start + StudioLayout.AnimationStride > bytes.Length)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.AnimationIkRuleCountOffset)..]);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.AnimationIkRuleIndexOffset)..]);

        if (count <= 0 || count > StudioReaderLimits.MaximumIkRules || index == 0)
        {
            return [];
        }

        long at = (long)start + index;

        if (at < 0 || at + ((long)count * Stride) > bytes.Length)
        {
            return [];
        }

        List<StudioIkRule> rules = new(count);

        for (int rule = 0; rule < count; rule++)
        {
            ReadOnlySpan<byte> entry = bytes.Slice((int)at + (rule * Stride), Stride);

            rules.Add(new StudioIkRule(
                Type: Int(entry, 4),
                Chain: Int(entry, 8),
                Bone: Int(entry, 12),
                Slot: Int(entry, 16),
                Height: Float(entry, 20),
                Radius: Float(entry, 24),
                Floor: Float(entry, 28),
                Position: (Float(entry, 32), Float(entry, 36), Float(entry, 40)),
                Rotation: (Float(entry, 44), Float(entry, 48), Float(entry, 52), Float(entry, 56)),
                CompressedError: Int(entry, 60),
                FirstFrame: Int(entry, 68),
                ErrorIndex: Int(entry, 72),
                Start: Float(entry, 76),
                Peak: Float(entry, 80),
                Tail: Float(entry, 84),
                End: Float(entry, 88),
                Contact: Float(entry, 96),
                Drop: Float(entry, 100),
                Top: Float(entry, 104),
                AttachmentName: Int(entry, 120)));
        }

        return rules;
    }

    /// <summary>A rule's influence at a cycle, and which frame of its error track to read.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="frames">How many frames the animation has.</param>
    /// <param name="cycle">Where the animation's cycle stands.</param>
    /// <param name="frame">Which frame of the error track to read.</param>
    /// <param name="fraction">How far past that frame.</param>
    /// <returns>The rule's weight, zero to one.</returns>
    /// <remarks>
    /// **<c>Studio_IKRuleWeight</c>, <c>bone_setup.cpp:2875</c>**, and it answers two questions at
    /// once:
    ///
    /// <code>
    ///   if (ikRule.end &gt; 1.0f &amp;&amp; flCycle &lt; ikRule.start) flCycle = flCycle + 1.0f;
    ///   fraq = (panim->numframes - 1) * (flCycle - ikRule.start) + ikRule.iStart;
    ///   iFrame = (int)fraq; fraq = fraq - iFrame;
    ///   if (flCycle &lt; ikRule.start) { iFrame = ikRule.iStart; fraq = 0.0f; return 0.0f; }
    ///   else if (flCycle &lt; ikRule.peak) value = (flCycle - start) / (peak - start);
    ///   else if (flCycle &lt; ikRule.tail) return 1.0f;
    ///   else if (flCycle &lt; ikRule.end)  value = 1.0f - ((flCycle - tail) / (end - tail));
    ///   else { fraq = (numframes - 1) * (end - start) + iStart; iFrame = (int)fraq; fraq -= iFrame; }
    ///   return SimpleSpline( value );
    /// </code>
    ///
    /// **The frame is computed BEFORE the branches and overwritten by two of them**, which is the
    /// part a tidier rewrite loses. Below the start it is pinned to the track's first frame; past
    /// the end it is pinned to the frame the end lands on, so a finished rule keeps reading its
    /// last error rather than running off the track.
    ///
    /// **The plateau returns 1.0 without splining**, where the two ramps are splined on the way
    /// out. Only the ramps pass through <c>SimpleSpline</c>, because at value one the spline is one
    /// anyway — reproduced as written rather than unified.
    /// </remarks>
    public static float Weight(
        in StudioIkRule rule, int frames, float cycle, out int frame, out float fraction)
    {
        // A rule whose end passes one wraps the loop: a footstep that begins near the end of a walk
        // cycle and finishes after it has come round again.
        if (rule.End > 1f && cycle < rule.Start)
        {
            cycle += 1f;
        }

        float exact = ((frames - 1) * (cycle - rule.Start)) + rule.FirstFrame;

        frame = (int)exact;
        fraction = exact - frame;

        float value;

        if (cycle < rule.Start)
        {
            frame = rule.FirstFrame;
            fraction = 0f;

            return 0f;
        }

        if (cycle < rule.Peak)
        {
            value = (cycle - rule.Start) / (rule.Peak - rule.Start);
        }
        else if (cycle < rule.Tail)
        {
            return 1f;
        }
        else if (cycle < rule.End)
        {
            value = 1f - ((cycle - rule.Tail) / (rule.End - rule.Tail));
        }
        else
        {
            exact = ((frames - 1) * (rule.End - rule.Start)) + rule.FirstFrame;

            frame = (int)exact;
            fraction = exact - frame;

            value = 0f;
        }

        return (3f * value * value) - (2f * value * value * value);
    }

    /// <summary>Where a rule wants its chain's end to be, at one frame.</summary>
    /// <param name="model">The whole <c>.mdl</c> file.</param>
    /// <param name="animation">Which animation, by index within this file.</param>
    /// <param name="rule">Which of its rules.</param>
    /// <param name="frame">Which frame of the error track, from <see cref="Weight"/>.</param>
    /// <param name="fraction">How far past that frame, from <see cref="Weight"/>.</param>
    /// <param name="position">The positional error.</param>
    /// <param name="rotation">The rotational error.</param>
    /// <returns>Whether a track was found and read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative.</exception>
    /// <remarks>
    /// **<c>CalcDecompressedAnimation</c>, <c>bone_setup.cpp:618</c>**, reached through
    /// `Studio_IKAnimationError`. Six channels — three of position, three of Euler angle — each a
    /// run-length track of the same shape a bone's animation uses, with its own scale:
    ///
    /// <code>
    ///   ExtractAnimValue( iFrame, pCompressed->pAnimvalue( 0 ), pCompressed->scale[0], p1.x, p2.x );
    ///   ...
    ///   pos = p1 * (1 - fraq) + p2 * fraq;
    ///   if (angle1.x != angle2.x || …) { AngleQuaternion( angle1, q1 ); … QuaternionBlend( q1, q2, fraq, q ); }
    ///   else AngleQuaternion( angle1, q );
    /// </code>
    ///
    /// **The angle comparison is Valve's own shortcut and it is EXACT.** Three float equalities
    /// decide whether to build two quaternions and blend them or build one; when the two frames
    /// hold the same angle the blend would be a no-op, and the branch exists to skip two
    /// `AngleQuaternion` calls rather than to guard anything. Reproduced.
    ///
    /// **Below a fraction of 0.0001 the engine reads ONE frame rather than blending toward the
    /// next**, which is the same threshold and the same reason.
    ///
    /// **The Euler order is roll-pitch-yaw**, not a `QAngle`'s — these are `RadianEuler`, so
    /// <see cref="StudioAnimation.FromEulerRadians"/> is the conversion, the same one the bone
    /// tracks and the bone controllers use.
    /// </remarks>
    public static bool Error(
        ReadOnlyMemory<byte> model,
        int animation,
        int rule,
        int frame,
        float fraction,
        out (float X, float Y, float Z) position,
        out (float X, float Y, float Z, float W) rotation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(animation);
        ArgumentOutOfRangeException.ThrowIfNegative(rule);

        position = default;
        rotation = (0f, 0f, 0f, 1f);

        if (Located(model, animation, rule) is not { } at)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = model.Span;

        int compressed = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 60)..]);

        if (compressed == 0)
        {
            return false;
        }

        long start = (long)at + compressed;

        if (start < 0 || start + CompressedErrorStride > bytes.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> track = bytes[(int)start..];

        int clamped = Math.Max(0, frame);

        if (fraction > 0.0001f)
        {
            (float firstX, float nextX) = Pair(track, 0, clamped);
            (float firstY, float nextY) = Pair(track, 1, clamped);
            (float firstZ, float nextZ) = Pair(track, 2, clamped);

            position = (
                (firstX * (1f - fraction)) + (nextX * fraction),
                (firstY * (1f - fraction)) + (nextY * fraction),
                (firstZ * (1f - fraction)) + (nextZ * fraction));

            (float firstPitch, float nextPitch) = Pair(track, 3, clamped);
            (float firstYaw, float nextYaw) = Pair(track, 4, clamped);
            (float firstRoll, float nextRoll) = Pair(track, 5, clamped);

#pragma warning disable S1244 // Valve's own exact comparison; see the remarks above.
            rotation =
                firstPitch != nextPitch || firstYaw != nextYaw || firstRoll != nextRoll
                    ? StudioBones.Slerp(
                        StudioAnimation.FromEulerRadians(firstPitch, firstYaw, firstRoll),
                        StudioAnimation.FromEulerRadians(nextPitch, nextYaw, nextRoll),
                        fraction)
                    : StudioAnimation.FromEulerRadians(firstPitch, firstYaw, firstRoll);
#pragma warning restore S1244

            return true;
        }

        position = (
            Channel(track, 0, clamped), Channel(track, 1, clamped), Channel(track, 2, clamped));

        rotation = StudioAnimation.FromEulerRadians(
            Channel(track, 3, clamped), Channel(track, 4, clamped), Channel(track, 5, clamped));

        return true;
    }

    /// <summary>Bytes per <c>mstudiocompressedikerror_t</c>: six scales then six offsets.</summary>
    private const int CompressedErrorStride = 36;

    /// <summary>Where a channel's offset table sits within the compressed error.</summary>
    private const int CompressedErrorTableOffset = 24;

    /// <summary>One channel at one frame, scaled.</summary>
    private static float Channel(ReadOnlySpan<byte> track, int channel, int frame) =>
        StudioAnimation.Value(track, CompressedErrorTableOffset, channel, frame) *
        BinaryPrimitives.ReadSingleLittleEndian(track[(channel * sizeof(float))..]);

    /// <summary>One channel at a frame and the one after it, for blending.</summary>
    /// <remarks>
    /// **Valve's `ExtractAnimValue` writes BOTH out of one walk**, since the second value usually
    /// sits beside the first in the same run-length block. Walking twice is the same answer for
    /// more work, and is what this does — a faithful single-walk version would need the block
    /// bookkeeping exposed, and the cost is one extra traversal of a track a few bytes long.
    /// </remarks>
    private static (float First, float Next) Pair(
        ReadOnlySpan<byte> track, int channel, int frame) =>
        (Channel(track, channel, frame), Channel(track, channel, frame + 1));

    /// <summary>Where one rule's bytes begin, or null when it names nothing.</summary>
    private static int? Located(ReadOnlyMemory<byte> model, int animation, int rule)
    {
        ReadOnlySpan<byte> bytes = model.Span;

        if (animation >= StudioAnimation.Count(model) ||
            bytes.Length < StudioLayout.HeaderAnimationIndexOffset + sizeof(int))
        {
            return null;
        }

        int start = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderAnimationIndexOffset..]) +
            (animation * StudioLayout.AnimationStride);

        if (start < 0 || start + StudioLayout.AnimationStride > bytes.Length)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.AnimationIkRuleCountOffset)..]);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.AnimationIkRuleIndexOffset)..]);

        if (rule >= count || count <= 0 || index == 0)
        {
            return null;
        }

        long at = (long)start + index + ((long)rule * Stride);

        return at >= 0 && at + Stride <= bytes.Length ? (int)at : null;
    }

    /// <summary>The nth four-byte int of a rule.</summary>
    private static int Int(ReadOnlySpan<byte> rule, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(rule[offset..]);

    /// <summary>The nth four-byte float of a rule.</summary>
    private static float Float(ReadOnlySpan<byte> rule, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(rule[offset..]);
}
