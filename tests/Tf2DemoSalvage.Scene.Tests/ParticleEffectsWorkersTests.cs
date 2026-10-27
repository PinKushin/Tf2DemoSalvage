using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `r_threaded_particles`: the played steps on worker threads give the serial frames byte for byte (findings 94, 103).
/// </summary>
/// <remarks>
/// **The control is the schedule.** A worker may write only its own slot and the write-back walks the slots in offer
/// order, so a schedule that runs them backwards or shuffled must give the same bytes; a write-back that relied on
/// completion order turns those red. **And every threaded run asserts it forked** (<see cref="WorkerLane.Forks"/>,
/// counted where the fork is taken): a threshold above the fixture's size would run both sides serially and pass by
/// comparing the serial path with itself.
///
/// The fixture covers what the serial order decides: several materials, a child, a duplicate offer, a dormant gap, a stop,
/// a seek that rebuilds every burst and drops one while inserting another (dictionary order, which the build walks), and
/// a material first seen part way through.
/// </remarks>
[TestFixture]
public sealed class ParticleEffectsWorkersTests
{
    [TestCase(2)]
    [TestCase(8)]
    public void Frames_OnThreads_AreTheSerialFramesByteForByte(int degree)
    {
        WorkerLane threaded = Lane(degree);

        Run(threaded).ShouldBe(Run(Serial()));
        threaded.Forks.ShouldBeGreaterThan(0, "the control: the steps were forked");
    }

    [Test]
    public void Frames_SlotsRunBackwards_AreTheSerialFramesByteForByte()
    {
        WorkerLane backwards = new(new GameWorkers(static (count, body) =>
        {
            for (int index = count - 1; index >= 0; index--)
            {
                body(index);
            }
        }));

        Run(backwards).ShouldBe(Run(Serial()));
        backwards.Forks.ShouldBeGreaterThan(0, "the control: the steps were forked");
    }

    [Test]
    public void Frames_SlotsRunShuffled_AreTheSerialFramesByteForByte()
    {
        // Odd indices first, then the even ones from the top: neither forwards nor backwards.
        WorkerLane shuffled = new(new GameWorkers(static (count, body) =>
        {
            foreach (int index in Enumerable.Range(0, count).OrderBy(index => index % 2 == 0 ? (count * 2) - index : index))
            {
                body(index);
            }
        }));

        Run(shuffled).ShouldBe(Run(Serial()));
        shuffled.Forks.ShouldBeGreaterThan(0, "the control: the steps were forked");
    }

    [Test]
    public void For_ThreadedOff_RunsInOrderOnTheCallingThreadAndNeverForks()
    {
        WorkerLane off = Lane(8);

        off.Threaded = false;
        List<int> order = [];
        int thread = Environment.CurrentManagedThreadId;

        off.For(5, index =>
        {
            Environment.CurrentManagedThreadId.ShouldBe(thread);
            order.Add(index);
        });

        order.ShouldBe([0, 1, 2, 3, 4]);
        off.Forks.ShouldBe(0);
    }

    [TestCase(4, 4, 0, TestName = "For_AListAtTheThreshold_RunsSerially")]
    [TestCase(5, 4, 1, TestName = "For_AListPastTheThreshold_Forks")]
    public void For_AroundTheThreshold_ForksOnlyPastIt(int count, int threshold, int forks)
    {
        WorkerLane workers = Lane(4, threshold);
        int[] ran = new int[count];

        workers.For(count, index => ran[index]++);

        ran.ShouldAllBe(times => times == 1);
        workers.Forks.ShouldBe(forks);
    }

    [Test]
    public void For_ADegreeOfOne_NeverForks()
    {
        WorkerLane one = Lane(1);

        one.For(10, static _ => { });

        one.Forks.ShouldBe(0);
    }

    /// <summary>The instrument's control: the serial frames draw every material, and the seek dropped the undrawn burst.</summary>
    [Test]
    public void Frames_Serial_DrawEveryMaterialAndDropTheUndrawnBurst()
    {
        ParticleEffects effects = new() { Workers = Serial() };
        HashSet<SpriteBlend> drawn = [];

        for (int tick = First; tick <= Last; tick++)
        {
            Frame(effects, tick);

            foreach (ParticleBatch batch in effects.Build(Eye, Vector3.UnitX, Vector3.UnitZ, Materials))
            {
                drawn.Add(batch.Material.Blend);
            }
        }

        drawn.ShouldBe([SpriteBlend.Translucent, SpriteBlend.Additive, SpriteBlend.AddOver], ignoreOrder: true);
        effects.BurstSteps(3).ShouldBe(-1, "the seek rebuilt key 3 undrawn, which removes it");
        effects.BurstSteps(6).ShouldBeGreaterThan(0, "and inserted key 6 in the same frame");
    }

    /// <remarks>
    /// **The serial order of a remove and an insert**, which the comparisons above cannot see because every run shares
    /// the write-back. At the seek key 6 is offered before key 3, which the seek drops: serially 6 is inserted while 3
    /// still holds its place, so 6 goes last, and the build draws material `a` as keys 1, 4, 5, 6. Removing 3 first would
    /// put 6 in its place, between 1 and 4.
    /// </remarks>
    [Test]
    public void Build_AtTheSeek_DrawsTheInsertedBurstAfterTheOnesAlreadyRunning()
    {
        ParticleEffects effects = new() { Workers = Lane(4) };

        for (int tick = First; tick <= Seek; tick++)
        {
            Frame(effects, tick);
        }

        ParticleBatch translucent = effects.Build(Eye, Vector3.UnitX, Vector3.UnitZ, Materials)
            .Single(batch => batch.Material.Blend == SpriteBlend.Translucent);
        List<float> centres = [];

        for (int first = 0; first < translucent.Corners.Count; first += ParticleSprites.CornersPerParticle)
        {
            centres.Add(Enumerable.Range(first, ParticleSprites.CornersPerParticle).Average(at => translucent.Corners[at].X));
        }

        centres[0].ShouldBeLessThan(150f, "key 1, first in the dictionary, is drawn first");
        centres[^1].ShouldBeGreaterThan(240f, "key 6, inserted at the seek, is drawn last");
    }

    [TestCase("", true, TestName = "Parse_NoThreadedParticles_IsOnAsTheEngineDefaultsIt")]
    [TestCase("r_threaded_particles 0\n", false, TestName = "Parse_ThreadedParticlesZero_IsOff")]
    [TestCase("r_threaded_particles 2\n", true, TestName = "Parse_ThreadedParticlesTwo_IsOnAsGetBoolReadsIt")]
    [TestCase("r_threaded_particles 0.5\n", false, TestName = "Parse_ThreadedParticlesAHalf_IsOffAsGetIntTruncatesIt")]
    public void Parse_ThreadedParticles_IsReadAsTheEnginesGetBool(string config, bool expected) =>
        ViewerSettings.Parse(config).ThreadedParticles.ShouldBe(expected);

    private const int First = 100;
    private const int Last = 145;
    private const int Seek = 130;
    private const float Seconds = 1f / 66f;

    private static readonly Vector3 Eye = new(0f, -1000f, 0f);

    private static WorkerLane Serial() => new() { Threaded = false };

    /// <summary>A lane over a pool of its own of <paramref name="degree"/> threads.</summary>
    private static WorkerLane Lane(int degree, int threshold = 0) => new(new GameWorkers { Degree = degree }) { Threshold = threshold };

    /// <remarks>
    /// **The duplicate offer, which the comparisons above cannot see** for the reason the seek test gives. Serially a
    /// second offer of a key in one frame finds it already stepped to the frame's moment and steps nothing, so the frames
    /// are those of one offer; an offer that read the state from before the first offer's write would step it twice.
    /// </remarks>
    [Test]
    public void Frames_AKeyOfferedTwiceInAFrame_AreTheFramesOfOneOffer()
    {
        WorkerLane threaded = Lane(4);

        Run(threaded).ShouldBe(Run(Serial(), duplicate: false));
        threaded.Forks.ShouldBeGreaterThan(0, "the control: the steps were forked");
    }

    private static byte[] Run(WorkerLane workers, bool duplicate = true)
    {
        ParticleEffects effects = new() { Workers = workers };
        List<byte> bytes = [];

        for (int tick = First; tick <= Last; tick++)
        {
            Frame(effects, tick, duplicate);

            foreach (ParticleBatch batch in effects.Build(Eye, Vector3.UnitX, Vector3.UnitZ, Materials))
            {
                bytes.Add((byte)batch.Material.Blend);
                bytes.AddRange(MemoryMarshal.AsBytes(batch.Corners.ToArray().AsSpan()).ToArray());
            }

            StringBuilder text = new();

            foreach ((string name, int live, int unoffered, int spent) in effects.Census())
            {
                text.Append(CultureInfo.InvariantCulture, $"{name} {live} {unoffered} {spent};");
            }

            text.Append(CultureInfo.InvariantCulture,
                $"{effects.Steps} {effects.Slept} {effects.ReplaySteps} {effects.BurstBuilds} {effects.Alive}");
            bytes.AddRange(Encoding.UTF8.GetBytes(text.ToString()));
        }

        return [.. bytes];
    }

    private static void Frame(ParticleEffects effects, int tick, bool duplicate = true)
    {
        bool seeked = tick == Seek;
        List<ParticleBurst> live =
        [
            Burst(1, A, 100, tick),
            Burst(3, A, 101, tick) with { Undrawn = seeked },
            Burst(2, B, 102, tick),
            Burst(4, A, 110, tick, stop: 120) with { DormantAt = at => at is >= 115 and < 118 },
        ];

        if (duplicate && tick is 105 or 106)
        {
            // Offered twice in one frame: the second must see the first's write, as the serial loop does.
            live.Add(Burst(2, B, 102, tick));
        }

        if (tick >= 125)
        {
            live.Add(Burst(5, C, 125, tick));
        }

        if (seeked)
        {
            // New, and ahead of the one the seek drops: serially it is inserted before key 3 frees its slot.
            live.Insert(0, Burst(6, A, 129, tick));
        }
        else if (tick > Seek)
        {
            live.RemoveAt(1);
            live.Add(Burst(6, A, 129, tick));
        }

        effects.Bursts(live, Seconds, Others, tick, tick, seeked);
    }

    private static ParticleBurst Burst(long key, ParticleSystem definition, int fired, int tick, int? stop = null) =>
        new(key, definition, At((key * 40f) + ((tick - fired) * 3f)), fired, null, stop);

    private static ParticleControlPoint At(float x) =>
        new(new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    private static readonly ParticleSystem A = System("a", 66d);
    private static readonly ParticleSystem Kid = System("kid", 33d);
    private static readonly ParticleSystem B = System("b", 99d) with { Children = ["kid"] };

    // Draws with A's material, first offered at 125: the batch order must still be A's first appearance.
    private static readonly ParticleSystem C = System("a", 50d) with { Name = "c" };

    private static readonly Dictionary<string, ParticleSystem> Others = new(StringComparer.Ordinal) { ["kid"] = Kid };

    private static readonly Dictionary<string, ParticleMaterial> Materials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["effects/a"] = new ParticleMaterial(null, [], SpriteBlend.Translucent),
        ["effects/b"] = new ParticleMaterial(null, [], SpriteBlend.Additive),
        ["effects/kid"] = new ParticleMaterial(null, [], SpriteBlend.AddOver),
    };

    private static ParticleSystem System(string name, double perSecond)
    {
        Dictionary<string, DmxValue> none = new(StringComparer.Ordinal);

        return new ParticleSystem(
            name,
            [
                new ParticleFunction("emit_continuously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["emission_rate"] = new DmxValue(DmxAttributeType.Real, perSecond),
                        ["emission_duration"] = new DmxValue(DmxAttributeType.Real, 0.5d),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 0.3d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 0.6d),
                    }),
            ],
            [new ParticleFunction("Lifespan Decay", "decay", none)],
            [new ParticleFunction("render_animated_sprites", "draw", none)],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["material"] = new DmxValue(DmxAttributeType.Text, Text: $"effects\\{name}.vmt"),
            });
    }
}
