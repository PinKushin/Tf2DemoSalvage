using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// **The face's vertex work after the flex-cost rewrite is byte-identical to before** (docs/measurements/2026-10-30-flex-for-cost.md).
/// </summary>
/// <remarks>
/// <see cref="FaceFlexReference"/> is the code as it stood; the production sequence is <c>FlexFor</c>'s miss — clear the
/// model's moved vertices, accumulate, write the flexed slots — on one scratch shared by two models of different sizes,
/// as the set shares it. Every frame's stream is compared bit for bit, the applied count exactly. The models are
/// hand-built from a fixed seed: stereo pairs, all four target ramps, a vertex animation past the model's own vertices and
/// one past the scratch, corners that name no vertex, a corner out of range, and duplicate vertices across flexes.
/// </remarks>
public sealed class FaceFlexIdentityTests
{
    private const int SmallVertices = 120;
    private const int LargeVertices = 300;

    [Test]
    public void Face_ManyWeightSetsOnTwoModelsSharingTheScratch_IsByteIdenticalToTheReference()
    {
        Model small = Build(seed: 7, SmallVertices, descriptors: 12, flexes: 40, asArrays: true);
        Model large = Build(seed: 11, LargeVertices, descriptors: 20, flexes: 70, asArrays: true);
        Model listed = Build(seed: 13, SmallVertices, descriptors: 12, flexes: 40, asArrays: false);

        // The scratch is sized for the larger model plus a margin, as a grown-only scratch is.
        float[] positions = new float[(LargeVertices + 5) * 3];
        float[] normals = new float[(LargeVertices + 5) * 3];
        float[] refPositions = new float[positions.Length];
        float[] refNormals = new float[normals.Length];

        Dictionary<Model, (float[] Now, float[] Reference)> streams = new()
        {
            [small] = (new float[small.Order.Length * 6], new float[small.Order.Length * 6]),
            [large] = (new float[large.Order.Length * 6], new float[large.Order.Length * 6]),
            [listed] = (new float[listed.Order.Length * 6], new float[listed.Order.Length * 6]),
        };

        // The production factory builds the corner list the reference's own copy of the old factory builds.
        foreach (Model model in streams.Keys)
        {
            model.Corners.At.ShouldBe(model.ReferenceCorners.At);
            model.Corners.From.ShouldBe(model.ReferenceCorners.From);
        }

        int frames = 0;
        int moved = 0;

        foreach ((Model model, float[] weights, float[] delayed) in Frames(small, large, listed))
        {
            (float[] now, float[] reference) = streams[model];

            int expected = FaceFlexReference.Face(
                model.Data, weights, delayed, refPositions, refNormals, model.ReferenceCorners, reference);

            FaceFlex.ClearMoved(model.Corners, positions, normals);
            int applied = StudioFlexRules.Accumulate(model.Data, weights, delayed, positions, normals);

            if (applied > 0)
            {
                FaceFlex.IntoFlexedSlots((positions, normals), model.Corners, now);
            }

            applied.ShouldBe(expected, $"frame {frames}");
            Bits(now).ShouldBe(Bits(reference), $"frame {frames}");
            frames++;
            moved += applied > 0 ? 1 : 0;
        }

        frames.ShouldBe(3 * 65, "the control: every frame was compared, the model switches included");
        moved.ShouldBeGreaterThan(100, "the control: most frames move the face");
        moved.ShouldBeLessThan(frames, "the control: some frames move nothing");
    }

    /// <summary>
    /// The offsets the clear walks are the ones the slot write reads, each once, ascending: vertices 4 and 1 are read by
    /// a corner; vertex 9 is named by a flex and read by none, so it is not cleared (the accumulation still writes it, and
    /// nothing reads it for this model).
    /// </summary>
    [Test]
    public void FlexedCorners_FlexesNamingAVertexTwice_ClearEachReadOffsetOnce()
    {
        StudioFlexData data = new(
            ["a", "b"],
            [],
            [],
            [
                new StudioMeshFlex(0, 0, 0f, 1f, 1f, 2f, [Anim(4), Anim(1)]),
                new StudioMeshFlex(1, 0, 0f, 1f, 1f, 2f, [Anim(1), Anim(9)]),
            ]);

        // Corners 1 and 3 both read vertex 1, so From holds offset 3 twice and Moved must hold it once.
        FlexedCorners corners = FaceFlex.FlexedCorners(data, [0, 1, 4, 1], [2, 1, 0, 3]);

        corners.From.ShouldBe([12, 3, 3]);
        corners.Moved.ShouldBe([3, 12]);
    }

    /// <summary>The clear writes the read offsets' three floats and nothing else.</summary>
    [Test]
    public void ClearMoved_ADirtyScratch_ZeroesOnlyTheReadOffsets()
    {
        FlexedCorners corners = new([0, 1], [3, 9]);
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f];

        FaceFlex.ClearMoved(corners, positions, normals);

        positions.ShouldBe([9f, 9f, 9f, 0f, 0f, 0f, 9f, 9f, 9f, 0f, 0f, 0f]);
        normals.ShouldBe([8f, 8f, 8f, 0f, 0f, 0f, 8f, 8f, 8f, 0f, 0f, 0f]);
    }

    /// <summary>An offset past the scratch is skipped, as the accumulation skips it, rather than thrown on.</summary>
    [Test]
    public void ClearMoved_AnOffsetPastTheScratch_IsSkipped()
    {
        FlexedCorners corners = new([0, 1], [0, 6]);
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f];

        FaceFlex.ClearMoved(corners, positions, normals);

        positions.ShouldBe([0f, 0f, 0f, 9f, 9f, 9f]);
        normals.ShouldBe([0f, 0f, 0f, 8f, 8f, 8f]);
    }

    /// <summary>
    /// A slot whose vertex is past the deltas fails the one-time range check, so the checked loop runs: it skips that
    /// slot and still writes the slots around it. (Only the checked loop runs here.)
    /// </summary>
    [Test]
    public void IntoFlexedSlots_ASlotWhoseVertexIsPastTheDeltas_TakesTheCheckedLoopAndSkipsOnlyThatSlot()
    {
        FlexedCorners corners = new([0, 1, 2], [0, 6, 3]);
        float[] stream = new float[18];
        stream.AsSpan().Fill(5f);

        FaceFlex.IntoFlexedSlots(([1f, 2f, 3f, 4f, 5f, 6f], [7f, 8f, 9f, 10f, 11f, 12f]), corners, stream);

        stream.ShouldBe([1f, 2f, 3f, 7f, 8f, 9f, 5f, 5f, 5f, 5f, 5f, 5f, 4f, 5f, 6f, 10f, 11f, 12f]);
    }

    /// <summary>
    /// **Corners that fail the one-time range check take the checked loop, and it matches the reference bit for bit**:
    /// slots whose vertex lies past the deltas, over dirty streams, against the code as it stood.
    /// </summary>
    [Test]
    public void IntoFlexedSlots_CornersThatFailTheRangeCheck_MatchTheReferenceByteForByte()
    {
        Seeded random = new(99);

        for (int round = 0; round < 50; round++)
        {
            float[] positions = new float[30];
            float[] normals = new float[30];

            for (int at = 0; at < positions.Length; at++)
            {
                positions[at] = Value(random);
                normals[at] = Value(random);
            }

            // Ascending slots; every third vertex offset past the ten vertices the deltas hold.
            int[] slots = [.. Enumerable.Range(0, 12).Select(slot => slot * 2)];
            int[] from = [.. slots.Select((_, index) => index % 3 == 2 ? (10 + random.Next(5)) * 3 : random.Next(10) * 3)];
            FlexedCorners corners = new([.. slots], [.. from]);

            float[] now = [.. Enumerable.Range(0, 24 * 6).Select(_ => Value(random))];
            float[] reference = (float[])now.Clone();

            FaceFlex.IntoFlexedSlots((positions, normals), corners, now);
            FaceFlexReference.IntoFlexedSlots((positions, normals), corners, reference);

            Bits(now).ShouldBe(Bits(reference), $"round {round}");
        }
    }

    /// <summary>
    /// A vertex offset near <see cref="int.MaxValue"/> must not pass the range check by wrapping: the checked loop
    /// runs, and throws exactly as it always did, rather than an unchecked read gigabytes past the array.
    /// </summary>
    [Test]
    public void IntoFlexedSlots_AVertexOffsetThatWrapsTheCheck_ThrowsAsTheCheckedLoopDoes()
    {
        FlexedCorners corners = new([0], [int.MaxValue - 1]);
        (float[], float[]) deltas = (new float[6], new float[6]);

        Should.Throw<IndexOutOfRangeException>(() => FaceFlexReference.IntoFlexedSlots(deltas, corners, new float[6]));
        Should.Throw<IndexOutOfRangeException>(() => FaceFlex.IntoFlexedSlots(deltas, corners, new float[6]));
    }

    /// <summary>
    /// Vertex 715,827,882's offset is 2,147,483,646: its last float is past int, so it is skipped without a throw, as the
    /// whole-array clear it replaces never threw (the accumulation still throws there, as it always did).
    /// </summary>
    [Test]
    public void ClearMoved_AnOffsetWhoseLastFloatIsPastInt_IsSkipped()
    {
        FlexedCorners corners = new([0, 1], [2_147_483_646, 3]);
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f];

        FaceFlex.ClearMoved(corners, positions, normals);

        positions.ShouldBe([9f, 9f, 9f, 0f, 0f, 0f]);
        normals.ShouldBe([8f, 8f, 8f, 0f, 0f, 0f]);
    }

    /// <summary>
    /// **The clear and the accumulation agree on a vertex whose offset wraps**: vertex 1,431,655,766 times three wraps in
    /// int to 2, which the accumulation writes (floats 2 to 4). The clear takes the same int offset from the corners, so
    /// those three floats are cleared each miss rather than summed across frames.
    /// </summary>
    [Test]
    public void ClearMoved_AVertexWhoseOffsetWrapsSmall_ClearsWhatTheAccumulationWrites()
    {
        const int Wrapping = 1_431_655_766;
        StudioFlexData data = new(["a"], [], [], [new StudioMeshFlex(0, 0, 0f, 1f, 1f, 2f, [Anim(Wrapping)])]);
        FlexedCorners corners = FaceFlex.FlexedCorners(data, [Wrapping], [0]);
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f];

        float[] cleanPositions = new float[9];
        float[] cleanNormals = new float[9];
        StudioFlexRules.Accumulate(data, [1f], [1f], cleanPositions, cleanNormals).ShouldBe(1, "the control: it writes at 2");

        FaceFlex.ClearMoved(corners, positions, normals);
        StudioFlexRules.Accumulate(data, [1f], [1f], positions, normals);

        positions.ShouldBe([9f, 9f, cleanPositions[2], cleanPositions[3], cleanPositions[4], 9f, 9f, 9f, 9f]);
        normals.ShouldBe([8f, 8f, cleanNormals[2], cleanNormals[3], cleanNormals[4], 8f, 8f, 8f, 8f]);
        cleanPositions[2].ShouldNotBe(0f, "the control: the accumulation moved it");
    }

    /// <summary>A negative offset is skipped, never thrown on, as is one whose int vertex-times-three wraps negative.</summary>
    [Test]
    public void ClearMoved_ANegativeOffset_IsSkipped()
    {
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f];

        FaceFlex.ClearMoved(new FlexedCorners([0], [-3]), positions, normals);

        positions.ShouldBe([9f, 9f, 9f, 9f, 9f, 9f]);
        normals.ShouldBe([8f, 8f, 8f, 8f, 8f, 8f]);
    }

    /// <summary>Vertex 715,827,883 times three is 2,147,483,649, which wraps in int to a negative offset: skipped.</summary>
    [Test]
    public void ClearMoved_AVertexWhoseOffsetWrapsNegative_IsSkipped()
    {
        const int Wrapping = 715_827_883;
        StudioFlexData data = new(["a"], [], [], [new StudioMeshFlex(0, 0, 0f, 1f, 1f, 2f, [Anim(Wrapping)])]);
        FlexedCorners corners = FaceFlex.FlexedCorners(data, [Wrapping], [0]);
        float[] positions = [9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f];

        corners.Moved.ShouldBe([int.MinValue + 1], "the control: the offset did wrap negative");

        FaceFlex.ClearMoved(corners, positions, normals);

        positions.ShouldBe([9f, 9f, 9f]);
        normals.ShouldBe([8f, 8f, 8f]);
    }

    /// <summary>
    /// A negative vertex offset must not take the unchecked fast path: the checked loop reads at it and throws, as the
    /// reference does, rather than reading memory before the array. (A negative slot is the same hazard on the write, but
    /// breaking its gate would write over the array header and crash the host, so it has no test.)
    /// </summary>
    [Test]
    public void IntoFlexedSlots_ANegativeVertexOffset_ThrowsAsTheCheckedLoopDoes()
    {
        FlexedCorners corners = new([0], [-3]);
        (float[], float[]) deltas = (new float[6], new float[6]);

        Should.Throw<IndexOutOfRangeException>(() => FaceFlexReference.IntoFlexedSlots(deltas, corners, new float[6]));
        Should.Throw<IndexOutOfRangeException>(() => FaceFlex.IntoFlexedSlots(deltas, corners, new float[6]));
    }

    /// <summary>A copy made with new offsets clears them: the cleared set cannot omit an offset the slot write reads.</summary>
    [Test]
    public void With_NewOffsets_ClearsThem()
    {
        FlexedCorners further = new FlexedCorners([0], [0]) with { From = [6] };
        float[] positions = [9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f];
        float[] normals = [8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f];

        FaceFlex.ClearMoved(further, positions, normals);

        positions.ShouldBe([9f, 9f, 9f, 9f, 9f, 9f, 0f, 0f, 0f]);
        normals.ShouldBe([8f, 8f, 8f, 8f, 8f, 8f, 0f, 0f, 0f]);
    }

    /// <summary>
    /// **A copy made with new slots carries their bounds, not the original's**, so the unchecked write cannot be handed
    /// a stale range: the wider copy fails the check and the checked loop throws as it always did.
    /// </summary>
    [Test]
    public void With_NewSlotsOrOffsets_RederivesTheBounds()
    {
        FlexedCorners narrow = new([0], [0]);
        FlexedCorners wider = narrow with { At = [5] };
        FlexedCorners further = narrow with { From = [99] };

        (wider.MinAt, wider.MaxAt).ShouldBe((5, 5));
        (further.MinFrom, further.MaxFrom).ShouldBe((99, 99));
        Should.Throw<IndexOutOfRangeException>(() => FaceFlex.IntoFlexedSlots((new float[6], new float[6]), wider, new float[6]));
    }

    private static IEnumerable<(Model Model, float[] Weights, float[] Delayed)> Frames(Model small, Model large, Model listed)
    {
        Seeded random = new(1234);

        foreach (Model model in new[] { small, large, listed, })
        {
            int count = model.Data.Descriptors.Count;
            float[] delayed = new float[count];
            double time = 0d;

            for (int frame = 0; frame < 64; frame++)
            {
                float[] weights = new float[count];

                switch (frame % 8)
                {
                    case 0:
                        break; // all zero
                    case 1:
                        weights[frame % count] = 1f; // one flex
                        break;
                    case 2:
                        weights.AsSpan().Fill(1f); // all flexes
                        break;
                    case 3:
                        // The signed zero and NaN: what a bitwise shortcut would read differently.
                        weights[0] = -0f;
                        weights[1] = float.NaN;
                        weights[2] = 0.0005f;
                        weights[3] = -0.0005f;
                        break;
                    default:
                        // A talking face: a smooth random walk, the delay following it as RunFlexDelay does.
                        for (int index = 0; index < count; index++)
                        {
                            weights[index] = (float)((random.NextDouble() * 2.4d) - 0.2d);
                        }

                        break;
                }

                FaceFlex.Delay(weights, delayed, ref time, (frame + 1) * 0.015d, 0.015d);

                // A frame that switches model in the middle of a talking sequence: the shared scratch is dirty from it.
                if (frame == 37)
                {
                    yield return (model == small ? large : small, Fit(weights, model == small ? large : small),
                        Fit((float[])delayed.Clone(), model == small ? large : small));
                }

                yield return (model, weights, (float[])delayed.Clone());
            }
        }
    }

    private static float[] Fit(float[] values, Model model)
    {
        float[] fitted = new float[model.Data.Descriptors.Count];
        values.AsSpan(0, Math.Min(values.Length, fitted.Length)).CopyTo(fitted);
        return fitted;
    }

    private static int[] Bits(float[] values) => MemoryMarshal.Cast<float, int>(values).ToArray();

    private static StudioVertAnim Anim(int vertex) => new(vertex, 255, 0, (1f, 0f, 0f), (0f, 1f, 0f));

    private sealed record Model(StudioFlexData Data, int[] Order, FlexedCorners Corners, FlexedCorners ReferenceCorners);

    private static Model Build(int seed, int vertices, int descriptors, int flexes, bool asArrays)
    {
        Seeded random = new(seed);
        List<StudioMeshFlex> list = [];

        for (int index = 0; index < flexes; index++)
        {
            int count = random.Next(1, 25);
            StudioVertAnim[] anims = new StudioVertAnim[count];

            for (int at = 0; at < count; at++)
            {
                int vertex = random.Next(vertices);

                // One past the model's own vertices (inside the larger scratch), one past any scratch.
                vertex = (index, at) switch
                {
                    (3, 0) => vertices + 2,
                    (5, 0) => vertices + 1000,
                    (7, 0) => vertices + 3, // inside the scratch, and no corner names it: accumulated, never read
                    _ => vertex,
                };

                anims[at] = new StudioVertAnim(
                    vertex,
                    (byte)random.Next(256),
                    (byte)random.Next(256),
                    (Value(random), Value(random), Value(random)),
                    (Value(random), Value(random), Value(random)));
            }

            // On the large model a ramp may start below zero, so even zero weights move it (B513's resting case).
            float t0 = (float)(random.NextDouble() * 0.3d) - (seed == 11 ? 0.1f : 0f);
            float t1 = t0 + (float)(random.NextDouble() * 0.6d);
            float t2 = t1 + (float)(random.NextDouble() * 0.6d);
            float t3 = t2 + (float)(random.NextDouble() * 0.6d) + 0.01f;

            list.Add(new StudioMeshFlex(
                random.Next(descriptors),
                index % 3 == 0 ? random.Next(1, descriptors) : 0,
                t0,
                t1,
                t2,
                t3,
                asArrays ? anims : FaceFlexReference.AsList(anims)));
        }

        StudioFlexData data = new([.. Enumerable.Range(0, descriptors).Select(d => $"d{d}")], [], [], list);

        // Corners: each vertex about twice, one naming no vertex (-1), one past the vertices.
        List<int> cornerVertex = [];

        for (int vertex = 0; vertex < vertices; vertex++)
        {
            cornerVertex.Add(vertex);
            cornerVertex.Add(random.Next(vertices));
        }

        cornerVertex.Add(-1);
        cornerVertex.Add(vertices + 2);

        int[] order = [.. Enumerable.Range(0, cornerVertex.Count + 2).Select(at => at < cornerVertex.Count ? at : cornerVertex.Count + at)];
        random.Shuffle(order);

        return new Model(
            data, order, FaceFlex.FlexedCorners(data, cornerVertex, order), FaceFlexReference.Corners(data, cornerVertex, order));
    }

    private static float Value(Seeded random) => (float)((random.NextDouble() * 4d) - 2d);

    /// <summary>A seeded xorshift32, so a failure names a reproducible case (CA5394 forbids <c>Random</c> here).</summary>
    private sealed class Seeded(int seed)
    {
        private uint _state = (uint)seed | 1u;

        public int Next(int below) => (int)(NextUInt() % (uint)below);

        public int Next(int from, int below) => from + Next(below - from);

        public double NextDouble() => (NextUInt() >> 8) / (double)(1 << 24);

        public void Shuffle(int[] values)
        {
            for (int at = values.Length - 1; at > 0; at--)
            {
                int other = Next(at + 1);
                (values[at], values[other]) = (values[other], values[at]);
            }
        }

        private uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }
    }
}
