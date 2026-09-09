using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One operator, initializer, emitter or renderer in a particle system.</summary>
/// <param name="Function">The class it names, such as <c>Movement Basic</c>.</param>
/// <param name="Name">Its own name in the file, which is editor text.</param>
/// <param name="Parameters">Everything it declares, by attribute name.</param>
/// <remarks>
/// **One record for all four kinds, because the file makes no structural distinction between
/// them** — they differ only by which array of the definition they appear in, and they carry the
/// same shape of parameter bag. Splitting them into four types here would invent a difference the
/// format does not have.
/// </remarks>
public sealed record ParticleFunction(
    string Function,
    string Name,
    IReadOnlyDictionary<string, DmxValue> Parameters)
{
    /// <summary>A declared number, or a default when the operator does not carry it.</summary>
    /// <param name="named">The attribute name, as the file spells it.</param>
    /// <param name="otherwise">What an absent attribute means.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// **Absent means the DEFAULT and never zero**, which is the trap
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md` names: a `.pcf` omits any parameter
    /// left at its default, so reading absence as zero silently rewrites an effect. Every caller
    /// therefore has to say what the default IS, and cannot get one for free.
    /// </remarks>
    public double Number(string named, double otherwise) =>
        Parameters.TryGetValue(named, out DmxValue value) &&
        value.Type is DmxAttributeType.Real or DmxAttributeType.Whole or DmxAttributeType.Boolean
            ? value.Number
            : otherwise;

    /// <summary>A declared vector, or a default when the operator does not carry it.</summary>
    /// <param name="named">The attribute name.</param>
    /// <param name="otherwise">What an absent attribute means.</param>
    /// <returns>The value.</returns>
    public Vector4 Vector(string named, Vector4 otherwise) =>
        Parameters.TryGetValue(named, out DmxValue value) &&
        value.Type is DmxAttributeType.Vector3 or DmxAttributeType.Vector4
            or DmxAttributeType.Colour or DmxAttributeType.Angle
            ? value.Vector
            : otherwise;

    /// <summary>A declared string, or null.</summary>
    /// <param name="named">The attribute name.</param>
    /// <returns>The value, or null when absent.</returns>
    public string? Text(string named) =>
        Parameters.TryGetValue(named, out DmxValue value) ? value.Text : null;
}

/// <summary>One named particle system, as a <c>.pcf</c> declares it.</summary>
/// <param name="Name">Its name, which is what the game asks for — <c>rockettrail</c>.</param>
/// <param name="Emitters">What decides when particles are born.</param>
/// <param name="Initializers">What sets a new particle's attributes.</param>
/// <param name="Operators">What changes a live particle each frame.</param>
/// <param name="Renderers">What draws them.</param>
/// <param name="Children">Systems this one spawns alongside itself, by name.</param>
/// <param name="Parameters">The definition's own attributes, such as the material.</param>
public sealed record ParticleSystem(
    string Name,
    IReadOnlyList<ParticleFunction> Emitters,
    IReadOnlyList<ParticleFunction> Initializers,
    IReadOnlyList<ParticleFunction> Operators,
    IReadOnlyList<ParticleFunction> Renderers,
    IReadOnlyList<string> Children,
    IReadOnlyDictionary<string, DmxValue> Parameters);

/// <summary>
/// The particle systems a <c>.pcf</c> declares, on top of <see cref="DmxFile"/> (B373).
/// </summary>
/// <remarks>
/// **The second of B373's three parts.** `DmxFile` reads the container; this gives the container its
/// meaning, and a simulator consumes the result. Splitting them is not ceremony: the DMX layout was
/// measured from bytes and the PCF schema is read from the ELEMENTS, so the two have different
/// evidence behind them and different ways of being wrong.
///
/// **The shape, measured on `particles/rockettrail.pcf`:** a `DmeParticleSystemDefinition` carries
/// `emitters`, `initializers`, `operators` and `renderers` as element arrays of
/// `DmeParticleOperator`, plus `children` as an array of `DmeParticleChild`. Every function element
/// names its class in `functionName` — `Movement Basic`, `Lifetime Random`,
/// `render_animated_sprites` — and carries that class's parameters beside it.
///
/// **A parameter a system leaves at its default is ABSENT from the file**, which is why
/// <see cref="ParticleFunction.Number"/> makes every caller state the default rather than handing
/// out zero.
/// </remarks>
public static class ParticleSystems
{
    /// <summary>The element type a particle system definition uses.</summary>
    private const string DefinitionType = "DmeParticleSystemDefinition";

    /// <summary>The element type every emitter, initializer, operator and renderer uses.</summary>
    private const string FunctionType = "DmeParticleOperator";

    /// <summary>The element type a child reference uses.</summary>
    private const string ChildType = "DmeParticleChild";

    /// <summary>Reads every particle system a file declares.</summary>
    /// <param name="file">The whole <c>.pcf</c>.</param>
    /// <returns>Its systems, by name.</returns>
    /// <remarks>
    /// **Keyed by name because that is how the game asks**: `ParticleProp()->Create( "rockettrail",
    /// … )` carries a name and nothing else. A file declaring the same name twice keeps the first,
    /// which matches a lookup built by insertion.
    /// </remarks>
    public static IReadOnlyDictionary<string, ParticleSystem> Read(ReadOnlySpan<byte> file)
    {
        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        Dictionary<string, ParticleSystem> systems = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < elements.Count; index++)
        {
            DmxElement element = elements[index];

            if (element.Type != DefinitionType || systems.ContainsKey(element.Name))
            {
                continue;
            }

            systems[element.Name] = new ParticleSystem(
                element.Name,
                Functions(elements, element, "emitters"),
                Functions(elements, element, "initializers"),
                Functions(elements, element, "operators"),
                Functions(elements, element, "renderers"),
                ChildNames(elements, element),
                element.Attributes);
        }

        return systems;
    }

    /// <summary>Births one particle with the defaults its DEFINITION declares.</summary>
    /// <param name="system">The definition, which carries the spawn values.</param>
    /// <param name="into">The store to add to.</param>
    /// <param name="point">Where the system is attached, and which way it faces.</param>
    /// <param name="lives">How long it lives when no initializer says otherwise, in seconds.</param>
    /// <param name="seconds">
    /// How long this step is, which an initializer that gives a particle SPEED needs: Verlet stores
    /// no velocity, so a speed is written as how far behind the particle its previous position is
    /// put. The engine's own initializers read the collection's step for the same reason.
    /// </param>
    /// <returns>Its index, or -1 when the system is already at <c>max_particles</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A system's own attributes ARE the spawn defaults, and seeding a store without them is a
    /// divergence** (B373). `rockettrail` declares `radius 10`; a store defaulting to 1 draws
    /// particles a tenth of the size and no operator corrects it, because `Radius Scale` scales
    /// what it was given. Measured on the shipped file, which is where the number came from:
    ///
    /// <code>
    /// max_particles 170   initial_particles 1   radius 10   rotation 0
    /// material effects\rocketrailsmoke.vmt
    /// </code>
    ///
    /// **`max_particles` is a REFUSAL and not a hint** — the engine sizes its collection from it, so
    /// a system at its cap emits nothing rather than growing. Returning -1 says so; silently adding
    /// would make a trail denser than the effect asks for.
    /// </remarks>
    public static int Spawn(
        ParticleSystem system,
        ParticleStore into,
        ParticleControlPoint point,
        float lives,
        float seconds)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(into);

        int cap = (int)Number(system, "max_particles", int.MaxValue);

        if (into.Count >= cap)
        {
            return -1;
        }

        int index = into.Add(point.At, lives);

        into.Resize(index, (float)Number(system, "radius", 1d));

        // **The initializers run HERE, at birth, and never again.** That is what makes them
        // initializers rather than operators — `Sequence Random` drawn every frame would flicker one
        // particle between four animations, and `Lifetime Random` drawn every frame would mean a
        // particle whose death kept moving.
        foreach (ParticleFunction one in system.Initializers)
        {
            switch (one.Function)
            {
                case "Sequence Random":
                    into.Sequence[index] = ParticleRandom.Whole(
                        into.Id[index],
                        SequenceDraw,
                        (int)one.Number("sequence_min", 0d),
                        (int)one.Number("sequence_max", 0d));

                    break;

                // **A DRAW and not the midpoint the earlier code took.** `rockettrail` declares
                // `lifetime_min 0.8` and `lifetime_max 1.2`, so a midpoint gave every puff in a
                // trail exactly 1.0 seconds — one plume dying all at once instead of thinning out.
                // The comment that justified the midpoint said the two bounds were equal; the file
                // says otherwise, which is `docs/memory/a-valve-comment-can-be-stale.md` applied to
                // our own.
                case "Lifetime Random":
                    float least = (float)one.Number("lifetime_min", lives);

                    into.Lifetime[index] = ParticleRandom.Between(
                        into.Id[index],
                        LifetimeDraw,
                        least,
                        (float)one.Number("lifetime_max", least));

                    break;

                // **A rocket trail is ORANGE, and nothing was reading that.** `rockettrail` declares
                // `color1 = (247 194 117)` and `color2 = (251 142 0)` — firelight on smoke — and the
                // renderer was drawing every particle at full white, so the texture came through
                // untinted. `TINT_RGB` is 0..255 per channel, which is the unit the file states them
                // in.
                //
                // *Interpolated:* that ONE draw lerps the whole colour rather than three
                // independent ones. The initializers ship only in the binary. A single factor keeps
                // every result on the line between the two colours an artist chose, where per
                // channel would put muddy mixes between them; the endpoints are identical either
                // way, so this is falsifiable by disassembling `particles.lib`.
                case "Color Random":
                    Vector4 from = one.Vector("color1", new Vector4(255f, 255f, 255f, 255f));
                    Vector4 to = one.Vector("color2", from);

                    float along = ParticleRandom.Sample(into.Id[index], ColourDraw);

                    into.Tint[index] = new Vector3(
                        from.X + ((to.X - from.X) * along),
                        from.Y + ((to.Y - from.Y) * along),
                        from.Z + ((to.Z - from.Z) * along));

                    into.TintAtBirth[index] = into.Tint[index];

                    break;

                // **`ALPHA` is 0..255 in the file and 0..1 in the store.** `rockettrail` asks for
                // 96..128, which is 0.38..0.50 — and every particle was spawning at 1.0, more than
                // twice as opaque as the effect declares.
                case "Alpha Random":
                    float dimmest = (float)one.Number("alpha_min", 255d);

                    into.Alpha[index] = ParticleRandom.Between(
                        into.Id[index],
                        AlphaDraw,
                        dimmest,
                        (float)one.Number("alpha_max", dimmest)) / 255f;

                    into.AlphaAtBirth[index] = into.Alpha[index];

                    break;

                // **A puff is born somewhere in a ball, moving**, and a trail whose particles all
                // start at one point is a line rather than a plume. `rockettrail` asks for a
                // 1.2-unit sphere, one unit per second outward, and ten units per second down the
                // control point's LOCAL Z — which is why the point carries a basis
                // (<see cref="ParticleControlPoint"/>).
                case "Position Within Sphere Random":
                    Place(one, into, index, point, seconds);
                    break;

                // **`Rotation Random` is what stops a trail looking like a grid.** `rockettrail`
                // gives every puff `rotation_initial -45` plus 0..45 degrees, so no two cards line
                // up. Stored in RADIANS, because that is what the renderer's basis rotation takes
                // and the conversion belongs where the file's own unit is known.
                case "Rotation Random":
                    float initial = (float)one.Number("rotation_initial", 0d);
                    float turnedLeast = (float)one.Number("rotation_offset_min", 0d);

                    into.Rotation[index] = float.DegreesToRadians(
                        initial + ParticleRandom.Between(
                            into.Id[index],
                            RotationDraw,
                            turnedLeast,
                            (float)one.Number("rotation_offset_max", turnedLeast)));

                    break;

                default:
                    break;
            }
        }

        return index;
    }

    /// <summary>Places and launches one particle — <c>Position Within Sphere Random</c>.</summary>
    /// <remarks>
    /// **The distribution is Valve's**, `RandomVectorInUnitSphere` (`mathlib_base.cpp:4203`) via
    /// <see cref="ParticleRandom.InUnitSphere"/>, cube root and all.
    ///
    /// **Velocity is expressed by moving PREV_XYZ backwards**, because Verlet stores no velocity —
    /// `MovementBasic` carries `position - previous` forward as this step's displacement, so a
    /// particle's speed at birth IS how far behind it its previous position is put. That is the
    /// only way to say "moving" in a scheme that records where things WERE
    /// (`particles.h:68`, *"prev coordinates for verlet integration"*).
    ///
    /// **Two speeds are added, and they are in different frames.** `speed_min`/`speed_max` are
    /// along the outward direction the sphere sample gave; `speed_in_local_coordinate_system` is in
    /// the control point's own basis. `rockettrail` uses `(0 0 -10)`, ten units per second down the
    /// rocket's local Z, which reads as the trail being pushed away from the projectile.
    ///
    /// *Interpolated:* that `distance_min` and `distance_max` bound the sample's RADIUS rather than
    /// replacing it — the initializers ship only in the binary. With `distance_min = 0`, which is
    /// what `rockettrail` declares, the two readings are identical, so this cannot be wrong on the
    /// effect it was written for. `distance_bias` is applied per axis to the direction, and is
    /// `(1 1 1)` here.
    /// </remarks>
    private static void Place(
        ParticleFunction one,
        ParticleStore into,
        int index,
        ParticleControlPoint point,
        float seconds)
    {
        (Vector3 sample, float radius) = ParticleRandom.InUnitSphere(into.Id[index], PositionDraw);

        Vector4 bias = one.Vector("distance_bias", new Vector4(1f, 1f, 1f, 0f));

        Vector3 direction = new(sample.X * bias.X, sample.Y * bias.Y, sample.Z * bias.Z);

        // A sample can land on the centre, where there is no direction to speak of.
        Vector3 outward = direction.LengthSquared() > 0f
            ? Vector3.Normalize(direction)
            : Vector3.UnitZ;

        float least = (float)one.Number("distance_min", 0d);
        float most = (float)one.Number("distance_max", least);

        into.Position[index] = point.At + (outward * (least + (radius * (most - least))));

        float speedLeast = (float)one.Number("speed_min", 0d);

        float speed = ParticleRandom.Between(
            into.Id[index],
            SpeedDraw,
            speedLeast,
            (float)one.Number("speed_max", speedLeast));

        Vector4 localLeast = one.Vector("speed_in_local_coordinate_system_min", default);
        Vector4 localMost = one.Vector("speed_in_local_coordinate_system_max", localLeast);

        float along = ParticleRandom.Sample(into.Id[index], LocalSpeedDraw);

        Vector3 local = new(
            localLeast.X + ((localMost.X - localLeast.X) * along),
            localLeast.Y + ((localMost.Y - localLeast.Y) * along),
            localLeast.Z + ((localMost.Z - localLeast.Z) * along));

        Vector3 velocity = (outward * speed)
            + (point.Forward * local.X)
            + (point.Right * local.Y)
            + (point.Up * local.Z);

        into.Previous[index] = into.Position[index] - (velocity * seconds);
    }

    /// <summary>
    /// Which table entry <c>Sequence Random</c> reads — an arbitrary constant that only has to
    /// differ from every other draw's, per <see cref="ParticleRandom.Sample"/>.
    /// </summary>
    private const int SequenceDraw = 0;

    /// <summary>Which table entry <c>Lifetime Random</c> reads.</summary>
    public const int LifetimeDraw = 1024;

    /// <summary>Which table entry <c>Color Random</c> reads.</summary>
    public const int ColourDraw = 2048;

    /// <summary>Which table entry <c>Alpha Random</c> reads.</summary>
    public const int AlphaDraw = 3072;

    /// <summary>Which table entry <c>Rotation Random</c> reads.</summary>
    public const int RotationDraw = 512;

    /// <summary>
    /// Where the sphere sample starts — it takes THIS entry and the two after it, per
    /// <c>RandomVector</c>'s own convention, so nothing else may claim 1537 or 1538.
    /// </summary>
    public const int PositionDraw = 1536;

    /// <summary>Which table entry the outward speed reads.</summary>
    public const int SpeedDraw = 2560;

    /// <summary>Which table entry the local-frame speed reads.</summary>
    public const int LocalSpeedDraw = 3584;

    /// <summary>A number the definition declares, or a default when it does not.</summary>
    private static double Number(ParticleSystem system, string named, double otherwise) =>
        system.Parameters.TryGetValue(named, out DmxValue value) &&
        value.Type is DmxAttributeType.Real or DmxAttributeType.Whole
            ? value.Number
            : otherwise;

    /// <summary>The functions one of a definition's arrays points at.</summary>
    private static List<ParticleFunction> Functions(
        IReadOnlyList<DmxElement> elements, DmxElement definition, string array)
    {
        if (!definition.Attributes.TryGetValue(array, out DmxValue value) ||
            value.Elements is not { Count: > 0 } references)
        {
            return [];
        }

        List<ParticleFunction> functions = new(references.Count);

        foreach (int reference in references)
        {
            if (reference < 0 || reference >= elements.Count)
            {
                continue;
            }

            DmxElement element = elements[reference];

            if (element.Type != FunctionType)
            {
                continue;
            }

            functions.Add(new ParticleFunction(
                element.Attributes.TryGetValue("functionName", out DmxValue named) &&
                    named.Text is { Length: > 0 } function
                    ? function
                    : string.Empty,
                element.Name,
                element.Attributes));
        }

        return functions;
    }

    /// <summary>The systems a definition spawns alongside itself.</summary>
    /// <remarks>
    /// **A child is a `DmeParticleChild` whose own name is the system it refers to**, which is how
    /// `rockettrail` pulls in `rockettrail_fire` and `rockettrail_burst`. The reference is by NAME
    /// rather than by index, so a child can live in another file.
    /// </remarks>
    private static List<string> ChildNames(
        IReadOnlyList<DmxElement> elements, DmxElement definition)
    {
        if (!definition.Attributes.TryGetValue("children", out DmxValue value) ||
            value.Elements is not { Count: > 0 } references)
        {
            return [];
        }

        List<string> names = new(references.Count);

        foreach (int reference in references)
        {
            if (reference >= 0 && reference < elements.Count &&
                elements[reference].Type == ChildType &&
                elements[reference].Name is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }
}
