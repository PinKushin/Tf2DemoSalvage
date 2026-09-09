using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The PCF schema on top of the DMX container (B373).
/// </summary>
/// <remarks>
/// **Synthetic (D38)**, so the expected values are the ones these tests wrote. The SHAPE they
/// encode is measured from `particles/rockettrail.pcf`: a `DmeParticleSystemDefinition` holds
/// `emitters`, `initializers`, `operators` and `renderers` as element arrays of
/// `DmeParticleOperator`, each naming its class in `functionName`.
/// </remarks>
[TestFixture]
public sealed class ParticleSystemsConformanceTests
{
    [Test]
    public void Read_ADefinition_SortsItsFunctionsIntoTheArrayThatNamedThem()
    {
        // The same element TYPE appears in all four arrays — a renderer and an operator are both
        // `DmeParticleOperator` — so which list a function belongs to is decided ONLY by which
        // array pointed at it. A reader keying on the type instead would put all four in one bag.
        byte[] file = Fixture();

        IReadOnlyDictionary<string, ParticleSystem> systems = ParticleSystems.Read(file);

        systems.Count.ShouldBe(1);

        ParticleSystem trail = systems["rockettrail"];

        trail.Emitters.Count.ShouldBe(1);
        trail.Emitters[0].Function.ShouldBe("emit_continuously");

        trail.Operators.Count.ShouldBe(1);
        trail.Operators[0].Function.ShouldBe("Movement Basic");

        trail.Renderers.Count.ShouldBe(1);
        trail.Renderers[0].Function.ShouldBe("render_animated_sprites");
    }

    [Test]
    public void Number_AnAbsentParameter_IsTheCallersDefaultAndNeverZero()
    {
        // **The trap this whole accessor exists for.** A `.pcf` omits any parameter left at its
        // default, so reading absence as zero silently rewrites the effect — a `Movement Basic`
        // with no `drag` does not mean drag zero unless zero happens to BE the engine's default.
        // Measured on the real file: `Movement Basic` declares `drag` and `Alpha Fade and Decay`
        // does not, which is exactly this distinction.
        ParticleSystem trail = ParticleSystems.Read(Fixture())["rockettrail"];

        ParticleFunction movement = trail.Operators[0];

        // Declared as 0.25 by the fixture, so it comes back as 0.25 rather than the default.
        movement.Number("drag", 99d).ShouldBe(0.25d, 0.0001d);

        // Not declared at all, so the caller's default survives - and the control is that the
        // default is not zero, which a "return 0 when absent" reader would pass by accident.
        movement.Number("bounce", 7d).ShouldBe(7d);
    }

    [Test]
    public void Spawn_ADefinitionDeclaringARadius_SeedsTheParticleWithIt()
    {
        // **A system's own attributes are the spawn defaults, and ignoring them is a divergence.**
        // `rockettrail` declares `radius 10`; a store defaulting to 1 draws particles a tenth of
        // the size, and no operator corrects it because `Radius Scale` scales what it was given.
        // Measured end to end on the shipped file: the radius went from 1.178 to 11.78 when this
        // was honoured.
        ParticleSystem system = new(
            "trail", [], [], [], [], [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["radius"] = new DmxValue(DmxAttributeType.Real, 10d),
            });

        ParticleStore store = new();

        int index = ParticleSystems.Spawn(
            system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: 1f / 66f);

        index.ShouldBe(0);
        store.RadiusOf(0).ShouldBe(10f);
    }

    [Test]
    public void Spawn_AtMaxParticles_RefusesRatherThanGrowing()
    {
        // **`max_particles` is a refusal, not a hint** - the engine sizes its collection from it, so
        // a system at its cap emits nothing. Silently adding would make a trail denser than the
        // effect asks for, which reads as an art choice rather than a bug.
        ParticleSystem system = new(
            "trail", [], [], [], [], [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["max_particles"] = new DmxValue(DmxAttributeType.Whole, 2d),
            });

        ParticleStore store = new();

        Spawn(system, store).ShouldBe(0);
        Spawn(system, store).ShouldBe(1);

        // The third is refused, and the store is unchanged by the refusal.
        Spawn(system, store).ShouldBe(-1);
        store.Count.ShouldBe(2);
    }

    [Test]
    public void Read_AChild_IsNamedRatherThanInlined()
    {
        // `rockettrail` pulls in `rockettrail_fire` by NAME, and a child can live in another file —
        // so a reader resolving children to indices would be unable to express the real thing.
        ParticleSystems.Read(Fixture())["rockettrail"].Children.ShouldBe(["rockettrail_fire"]);
    }

    /// <summary>
    /// One definition with an emitter, an operator, a renderer and a child.
    /// </summary>
    /// <remarks>
    /// Element order matters, because an element array holds INDICES: 0 is the definition, 1-3 the
    /// functions, 4 the child.
    /// </remarks>
    private static byte[] Fixture()
    {
        string[] strings =
        [
            "DmeParticleSystemDefinition",  // 0
            "DmeParticleOperator",          // 1
            "DmeParticleChild",             // 2
            "emitters",                     // 3
            "operators",                    // 4
            "renderers",                    // 5
            "children",                     // 6
            "functionName",                 // 7
            "drag",                         // 8
        ];

        (int Type, string Name, (int Named, byte Kind, byte[] Payload)[] Attributes)[] elements =
        [
            (0, "rockettrail",
            [
                (3, Array(DmxAttributeType.Element), Indices(1)),
                (4, Array(DmxAttributeType.Element), Indices(2)),
                (5, Array(DmxAttributeType.Element), Indices(3)),
                (6, Array(DmxAttributeType.Element), Indices(4)),
            ]),
            (1, "emit", [(7, (byte)DmxAttributeType.Text, Text("emit_continuously"))]),
            (1, "move",
            [
                (7, (byte)DmxAttributeType.Text, Text("Movement Basic")),
                (8, (byte)DmxAttributeType.Real, BitConverter.GetBytes(0.25f)),
            ]),
            (1, "draw", [(7, (byte)DmxAttributeType.Text, Text("render_animated_sprites"))]),
            (2, "rockettrail_fire", []),
        ];

        return Build(strings, elements);
    }

    /// <summary>An array attribute's type: its element type plus <c>AT_FIRST_ARRAY_TYPE</c>.</summary>
    private static byte Array(DmxAttributeType of) => (byte)((int)of + 14);

    /// <summary>An element-array payload: a count then that many indices.</summary>
    private static byte[] Indices(params int[] references)
    {
        List<byte> payload = [.. BitConverter.GetBytes(references.Length)];

        foreach (int one in references)
        {
            payload.AddRange(BitConverter.GetBytes(one));
        }

        return [.. payload];
    }

    /// <summary>A string payload: null-terminated, inline.</summary>
    private static byte[] Text(string value) => [.. Encoding.UTF8.GetBytes(value), 0];

    /// <summary>Writes the binary DMX layout measured from TF2's own particle files.</summary>
    private static byte[] Build(
        string[] strings,
        (int Type, string Name, (int Named, byte Kind, byte[] Payload)[] Attributes)[] elements)
    {
        List<byte> file =
        [
            .. Encoding.ASCII.GetBytes("<!-- dmx encoding binary 2 format pcf 1 -->\n"), 0,
        ];

        file.AddRange(BitConverter.GetBytes((ushort)strings.Length));

        foreach (string one in strings)
        {
            file.AddRange(Encoding.UTF8.GetBytes(one));
            file.Add(0);
        }

        file.AddRange(BitConverter.GetBytes(elements.Length));

        foreach ((int type, string name, _) in elements)
        {
            file.AddRange(BitConverter.GetBytes((ushort)type));
            file.AddRange(Encoding.UTF8.GetBytes(name));
            file.Add(0);
            file.AddRange(new byte[16]);
        }

        foreach ((_, _, (int Named, byte Kind, byte[] Payload)[] attributes) in elements)
        {
            file.AddRange(BitConverter.GetBytes(attributes.Length));

            foreach ((int named, byte kind, byte[] payload) in attributes)
            {
                file.AddRange(BitConverter.GetBytes((ushort)named));
                file.Add(kind);
                file.AddRange(payload);
            }
        }

        return [.. file];
    }

    /// <summary>A spawn at the origin, for the tests that are about the CAP rather than placement.</summary>
    private static int Spawn(ParticleSystem system, ParticleStore store) =>
        ParticleSystems.Spawn(
            system, store, ParticleControlPoint.Unoriented(Vector3.Zero), lives: 1f, seconds: 1f / 66f);
}
