using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One rigid body of a model's collision definition, as its <c>.phy</c> declares it.</summary>
/// <param name="Index">Its position in the solid list, which the constraints refer to it by.</param>
/// <param name="Name">The BONE it is attached to — the field that maps the graph onto a skeleton.</param>
/// <param name="Parent">The parent bone the exporter recorded, empty for most.</param>
/// <param name="SurfaceProperty">Its material, e.g. <c>flesh</c>, which decides sounds and friction.</param>
/// <param name="Mass">Its mass in kilograms.</param>
/// <param name="Inertia">Valve's inertia SCALE, not a tensor — see the remarks on the type.</param>
/// <param name="Damping">Linear damping.</param>
/// <param name="RotationDamping">Angular damping.</param>
/// <param name="Volume">The hull's volume, which is not a shape but is not nothing.</param>
/// <param name="DragCoefficient">Air drag, defaulted by the engine when the file omits it.</param>
public readonly record struct PhysicsSolid(
    int Index,
    string Name,
    string Parent,
    string SurfaceProperty,
    float Mass,
    float Inertia,
    float Damping,
    float RotationDamping,
    float Volume,
    float DragCoefficient);

/// <summary>One ragdoll joint, limiting how two solids may turn relative to each other.</summary>
/// <param name="Parent">The solid index this joint hangs from.</param>
/// <param name="Child">The solid index it moves.</param>
/// <param name="X">Limits about the first axis.</param>
/// <param name="Y">Limits about the second.</param>
/// <param name="Z">Limits about the third.</param>
public readonly record struct RagdollConstraint(
    int Parent,
    int Child,
    ConstraintAxis X,
    ConstraintAxis Y,
    ConstraintAxis Z);

/// <summary>One axis of a ragdoll joint.</summary>
/// <param name="Minimum">Least rotation, in degrees; negative.</param>
/// <param name="Maximum">Greatest rotation, in degrees.</param>
/// <param name="Friction">
/// Resistance to turning. **This is Valve's <c>torque</c>**, which
/// <c>constraint_axislimit_t::SetAxisFriction( rmin, rmax, friction )</c> assigns straight into the
/// torque field with the angular velocity left at zero (<c>constraints.h:68-74</c>) — a torque that
/// opposes motion is friction, and the file spells it that way.
/// </param>
public readonly record struct ConstraintAxis(float Minimum, float Maximum, float Friction);

/// <summary>
/// A model's <c>.phy</c>: its rigid bodies and the joints between them (B58).
/// </summary>
/// <remarks>
/// **A `.phy` is two halves and only one of them is closed.** The file opens with `phyheader_t` —
/// four ints, sixteen bytes —
///
/// <code>
/// typedef struct phyheader_s
/// {
///     DECLARE_BYTESWAP_DATADESC();
///     int    size;
///     int    id;
///     int    solidCount;
///     int32  checkSum;   // checksum of source .mdl file
/// } phyheader_t;
/// </code>
///
/// `phyfile.h:14-21` — then `solidCount` collision hulls in Havok's `IVPS` format, which is closed,
/// and then **a plain-text KeyValues block carrying everything else**. This type reads the header
/// and that text; the hulls are skipped over, and the reasons that is not fatal are recorded in
/// B58. Masses, inertias, damping, per-axis joint limits, joint friction, surface properties and —
/// crucially — the solid-to-BONE mapping are all in the text.
///
/// **The engine reads the same two block names this does**, dispatching on them by name:
///
/// <code>
/// if ( !strcmpi( pBlock, "solid" ) )                  { … ParseSolid( &amp;solid, &amp;g_SolidSetup ); … }
/// else if ( !strcmpi( pBlock, "ragdollconstraint" ) ) { … ParseRagdollConstraint( &amp;constraint, NULL ); … }
/// </code>
///
/// `ragdoll_shared.cpp:283-293`. Read-from-source. `collisionrules` is the third block Valve handles
/// and is not read here; it turns collision between specific pairs off, which needs a collision
/// system to mean anything.
///
/// **`"name"` is the load-bearing field**, because the constraints refer to solids by INDEX and an
/// index without a bone name is a number about an unknown ordering.
///
/// **Every class model carries a complete definition, and always a tree** — one fewer constraint
/// than solids: demo and pyro 15/14, heavy 16/15, scout and sniper 17/16, engineer 18/17, medic
/// 24/23.
/// </remarks>
public sealed class PhysicsModel
{
    /// <summary>The solids, in the order the file declares them.</summary>
    public IReadOnlyList<PhysicsSolid> Solids { get; }

    /// <summary>The joints between them, empty for a model that is not a ragdoll.</summary>
    public IReadOnlyList<RagdollConstraint> Constraints { get; }

    /// <summary>The header's <c>solidCount</c> — how many HULLS the binary section holds.</summary>
    /// <remarks>
    /// **Read separately from <see cref="Solids"/> and compared against it deliberately.** The
    /// header counts hulls in the closed binary section; the text counts `solid` blocks. They are
    /// the same number in every file measured, and a disagreement would mean this reader had found
    /// the text at the wrong offset — which is exactly the failure that produces a plausible,
    /// wrong answer rather than an error.
    /// </remarks>
    public int DeclaredSolidCount { get; }

    /// <summary>The source <c>.mdl</c>'s checksum, which ties this file to that model.</summary>
    public int Checksum { get; }

    private PhysicsModel(
        IReadOnlyList<PhysicsSolid> solids,
        IReadOnlyList<RagdollConstraint> constraints,
        int declaredSolidCount,
        int checksum)
    {
        Solids = solids;
        Constraints = constraints;
        DeclaredSolidCount = declaredSolidCount;
        Checksum = checksum;
    }

    /// <summary>Reads a <c>.phy</c>.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>Its solids and constraints, both possibly empty.</returns>
    /// <exception cref="InvalidOperationException">The header is short or malformed.</exception>
    /// <remarks>
    /// **The text is found by scanning for the first block name rather than by arithmetic**, and
    /// that is a deliberate choice against the tidier one. `phyheader_t.size` is the size of the
    /// HEADER (16), not an offset to the text, and the `IVPS` section's length is the sum of the
    /// solids' own leading sizes — walkable, but only by trusting a closed format's internals to
    /// find something that announces itself in ASCII. A scan cannot be thrown off by a hull layout
    /// this project has no other reason to understand.
    /// </remarks>
    public static PhysicsModel Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderSize)
        {
            throw new InvalidOperationException(
                $"a .phy is at least {HeaderSize} bytes of header; this one is {bytes.Length}");
        }

        int size = BitConverter.ToInt32(bytes[..4]);
        int solidCount = BitConverter.ToInt32(bytes[8..12]);
        int checksum = BitConverter.ToInt32(bytes[12..16]);

        // **Valve writes `sizeof(phyheader_t)` here**, so anything else means this is not one — a
        // guard rather than a use, since nothing below needs the value.
        if (size != HeaderSize)
        {
            throw new InvalidOperationException(
                $"a .phy header declares size {HeaderSize}; this one declares {size}");
        }

        int text = FindText(bytes);

        if (text < 0)
        {
            return new PhysicsModel([], [], solidCount, checksum);
        }

        return Parse(file[text..], solidCount, checksum);
    }

    /// <summary>Where the KeyValues section starts, or -1.</summary>
    /// <remarks>
    /// **Anchored on a block NAME at the start of a line**, because the bytes before it are
    /// arbitrary and could contain anything. `solid` and `ragdollconstraint` are the only two names
    /// a ragdoll's text begins with, and a model with neither has no text worth finding.
    /// </remarks>
    private static int FindText(ReadOnlySpan<byte> bytes)
    {
        int solid = IndexOf(bytes, "solid"u8);
        int joint = IndexOf(bytes, "ragdollconstraint"u8);

        if (solid < 0)
        {
            return joint;
        }

        return joint < 0 ? solid : Math.Min(solid, joint);
    }

    private static int IndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> marker)
    {
        for (int at = HeaderSize; at + marker.Length <= bytes.Length; at++)
        {
            if (bytes.Slice(at, marker.Length).SequenceEqual(marker))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>Reads the KeyValues half.</summary>
    private static PhysicsModel Parse(ReadOnlyMemory<byte> text, int solidCount, int checksum)
    {
        List<PhysicsSolid> solids = [];
        List<RagdollConstraint> constraints = [];

        // The block being read, and the keys gathered for it so far. A block's fields are flat, so
        // one dictionary per block is the whole state.
        string block = string.Empty;
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);

        void Close()
        {
            if (block.Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                solids.Add(SolidFrom(fields));
            }
            else if (block.Equals("ragdollconstraint", StringComparison.OrdinalIgnoreCase))
            {
                constraints.Add(ConstraintFrom(fields));
            }

            block = string.Empty;
            fields.Clear();
        }

        KeyValuesReader.Read(text.Span, (key, value, depth) =>
        {
            if (value is null)
            {
                // A new block opens: whatever was being gathered is finished.
                Close();

                block = key;
            }
            else if (depth > 0)
            {
                fields[key] = value;
            }

            return true;
        });

        // **The last block has no successor to close it.** Every `.phy` TF2 ships happens to end
        // with a trailing block — `editparams` — so on those files the last constraint is closed by
        // that one opening, and removing this line changes no measured count. It is kept because
        // that is an accident of Valve's exporter rather than a property of the format: a text
        // ending on its final `ragdollconstraint` would lose it silently. Exercised by an authored
        // specimen, since no shipped file supplies the case
        // (`docs/memory/author-the-specimen-the-corpus-lacks.md`).
        Close();

        return new PhysicsModel(solids, constraints, solidCount, checksum);
    }

    private static PhysicsSolid SolidFrom(Dictionary<string, string> fields) =>
        new(
            Integer(fields, "index"),
            Text(fields, "name"),
            Text(fields, "parent"),
            Text(fields, "surfaceprop"),
            Number(fields, "mass"),

            // **Valve's declared defaults, and they are not zero.** `objectparams_t` carries
            // inertia, damping, rotdamping, volume and dragCoefficient
            // (`vphysics_interface.h:1062-1075`); a file omitting one means "the engine's value",
            // not "none of it". Inertia at 1 is the identity scale and drag at 1 is the engine's
            // own.
            Number(fields, "inertia", 1f),
            Number(fields, "damping"),
            Number(fields, "rotdamping"),
            Number(fields, "volume"),
            Number(fields, "drag", 1f));

    private static RagdollConstraint ConstraintFrom(Dictionary<string, string> fields) =>
        new(
            Integer(fields, "parent", -1),
            Integer(fields, "child", -1),
            Axis(fields, 'x'),
            Axis(fields, 'y'),
            Axis(fields, 'z'));

    /// <summary>One axis's three fields, named <c>xmin</c>, <c>xmax</c> and <c>xfriction</c>.</summary>
    private static ConstraintAxis Axis(Dictionary<string, string> fields, char axis) =>
        new(
            Number(fields, axis + "min"),
            Number(fields, axis + "max"),
            Number(fields, axis + "friction"));

    private static string Text(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) ? value : string.Empty;

    private static int Integer(Dictionary<string, string> fields, string key, int fallback = 0) =>
        fields.TryGetValue(key, out string? value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    /// <remarks>
    /// **Invariant culture, and this is not pedantry.** A `.phy` writes `"-35.000000"` with a full
    /// stop, and a machine whose locale uses a comma would parse every joint limit as zero under
    /// the current culture — every ragdoll perfectly rigid, on somebody else's computer, with no
    /// error anywhere.
    /// </remarks>
    private static float Number(Dictionary<string, string> fields, string key, float fallback = 0f) =>
        fields.TryGetValue(key, out string? value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;

    /// <summary>Bytes of <c>phyheader_t</c> — four ints.</summary>
    private const int HeaderSize = 16;
}
