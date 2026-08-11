using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>What a snapshot says happened to an entity.</summary>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte matches the on-disk field, which is 2 bits wide.")]
public enum EntityUpdateType : byte
{
    /// <summary>Existing entity, properties changed.</summary>
    Delta = 0,

    /// <summary>Entity left the visible set but still exists.</summary>
    Leave = 1,

    /// <summary>Entity became visible; carries its class and serial number.</summary>
    Enter = 2,

    /// <summary>Entity was destroyed.</summary>
    Delete = 3,
}

/// <summary>One property read out of an entity update.</summary>
/// <param name="Index">Position in the class's flattened property list.</param>
/// <param name="Definition">The property that position addresses.</param>
/// <param name="Value">The decoded value.</param>
/// <param name="CoordShape">
/// Which components of a coordinate used the narrow integer field, one bit each. Zero for every
/// other kind of property, and carried for the same reason as the index width: the sender's
/// choice is not recoverable from the value.
/// </param>
/// <param name="IndexPayloadBits">
/// Payload width the property index delta was sent at, or 0 for the narrowest that holds it.
/// Carried for the same reason the entity index's is: the sender does not always choose the
/// narrowest bucket, and both widths decode to the same index (RISKS B25).
/// </param>
/// <param name="ElementShapes">
/// Each array element's coordinate shape, or <c>null</c> for a non-array property.
/// </param>
/// <remarks>
/// **An array's elements each carry their own encoding shape, and it is not recoverable from the
/// values.** A coordinate can arrive with an integer part, a fractional part, both or neither, and
/// the decoded number is identical across several of those forms — so re-encoding without the
/// shape produces a body of a different width that still says the same thing.
///
/// This was missed when shapes were first recorded, because `ReadArray` called the overload of
/// `ReadValue` that discards them. It went unnoticed for as long as the corpus contained no array
/// property whose elements varied: a 16-element `m_trackPoints` on a PASS Time map re-encoded
/// fifteen bits long, and a one-element `m_vecPoints` three bits long (RISKS B27).
/// </remarks>
public readonly record struct DecodedProperty(
    int Index,
    FlatProperty Definition,
    PropertyValue Value,
    int IndexPayloadBits = 0,
    int CoordShape = 0,
    IReadOnlyList<int>? ElementShapes = null);

/// <summary>One entity as a snapshot described it.</summary>
/// <param name="EntityIndex">Slot in the entity table.</param>
/// <param name="ClassId">Networked class, remembered from whichever snapshot it entered on.</param>
/// <param name="SerialNumber">Distinguishes a reused slot from the entity that held it before.</param>
/// <param name="UpdateType">What happened to it.</param>
/// <param name="Properties">Properties this snapshot changed, in wire order.</param>
/// <param name="IndexPayloadBits">
/// Payload width the entity index delta was sent at, or 0 when it was the narrowest that holds
/// the value. Carried because the sender does not always pick the narrowest, and which bucket it
/// used is not recoverable from the index - both decode to the same number (RISKS B25).
/// </param>
public sealed record DecodedEntity(
    int EntityIndex,
    int ClassId,
    int SerialNumber,
    EntityUpdateType UpdateType,
    IReadOnlyList<DecodedProperty> Properties,
    int IndexPayloadBits = 0);

/// <summary>One temp entity — a short-lived effect such as an explosion, tracer or impact.</summary>
/// <param name="ClassId">Networked class, which says what kind of effect it is.</param>
/// <param name="DelaySeconds">How long after the message the effect fires.</param>
/// <param name="Properties">The effect's parameters, read like any entity's.</param>
/// <param name="IsReliable">Whether the message carried this effect as its single reliable event.</param>
/// <remarks>
/// No entity index and no serial number: a temp entity is fire-and-forget and never enters the
/// entity table, which is why it needs a record of its own rather than reusing
/// <see cref="DecodedEntity"/>.
/// </remarks>
public sealed record DecodedTempEntity(
    int ClassId,
    float DelaySeconds,
    IReadOnlyList<DecodedProperty> Properties,
    bool IsReliable = false);

/// <summary>
/// Walks a <c>svc_PacketEntities</c> body, producing entities and their changed properties.
/// </summary>
/// <remarks>
/// **Stateful by necessity, not by preference.** A delta update carries no class id — only an
/// entity index — so the class has to be remembered from the snapshot the entity entered on.
/// Without it there is no flattened property list to index into and no way to know how wide the
/// next value is, which is why an unknown entity is an error here rather than something to skip.
///
/// Two encodings dominate the risk, and they are the same shape: both entity indices and
/// property indices are transmitted as <c>previous + delta + 1</c>. Dropping the <c>+1</c>
/// yields indices that are still monotonic and still address real properties, so the demo
/// decodes into a coherent-looking match that is quietly wrong. The tests use consecutive
/// items throughout for that reason — with a single item the two behaviours agree.
///
/// Flattened property lists are cached per class. A demo has hundreds of classes and tens of
/// thousands of snapshots, and flattening walks the whole table hierarchy every time.
/// </remarks>
public sealed class EntityDecoder
{
    /// <summary><c>MAX_EDICTS</c>: the engine's hard ceiling on entity slots.</summary>
    private const int MaxEntities = 2048;

    /// <summary>Width of the serial number that distinguishes a reused entity slot.</summary>
    private const int SerialNumberBits = 10;

    /// <summary>Width of an entity index in the trailing removal list.</summary>
    private const int RemovedIndexBits = 11;

    private readonly DemoSchema _schema;
    private readonly int _classBits;
    private readonly Dictionary<int, int> _entityClasses = [];
    private readonly Dictionary<int, IReadOnlyList<FlatProperty>> _flattened = [];
    private readonly Dictionary<int, ServerClass> _classesById = [];
    private readonly List<int> _removed = [];

    /// <summary>Creates a decoder bound to one demo's schema.</summary>
    /// <param name="schema">The schema the demo carries.</param>
    /// <param name="classBits">
    /// Width of a class id, derived from the class count rather than transmitted.
    /// </param>
    public EntityDecoder(DemoSchema schema, int classBits)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _schema = schema;
        _classBits = classBits;

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            _classesById[serverClass.Id] = serverClass;
        }
    }

    /// <summary>
    /// Bits the most recent snapshot's entity section occupied, before any removals.
    /// </summary>
    /// <remarks>
    /// Exposed so a re-encoder can be compared against what the decoder consumed. Comparing
    /// against the body's stated length instead conflates three different things: the entities,
    /// the removal list, and whatever the sender left after them.
    /// </remarks>
    public int EntitySectionBits { get; private set; }

    /// <summary>Bit offset each entity finished at, parallel to the decoded entities.</summary>
    /// <remarks>
    /// **Attribution, for a mismatch too small to find any other way.** When the encoder and the
    /// decoder disagree about a snapshot's width, the difference is a single number for the whole
    /// section — and a snapshot can hold three hundred entities. Subtracting consecutive entries
    /// here gives each entity's decoded width, which can be compared against re-encoding a prefix,
    /// so the disagreement narrows to one entity and then to one property.
    ///
    /// Reported as data rather than logged, like every other diagnostic in this parser: a caller
    /// that wants it can assert on it, and one that does not pays an int per entity.
    /// </remarks>
    public IReadOnlyList<int> EntityEndBits => _entityEndBits;

    private readonly List<int> _entityEndBits = [];

    /// <summary>
    /// Entity indices the most recent delta snapshot reported as removed.
    /// </summary>
    public IReadOnlyList<int> RemovedEntities => _removed;

    /// <summary>Bits a class id occupies, given how many classes the server declared.</summary>
    /// <param name="classCount">Number of networked classes.</param>
    /// <returns>The width used for class ids on the wire.</returns>
    /// <remarks>
    /// Derived, never transmitted. A wrong width here does not fail — it misreads the class of
    /// every entering entity, and therefore every property they carry.
    ///
    /// It is <c>floor(log2(count)) + 1</c>, not <c>ceil</c>. The two agree except on exact powers
    /// of two, which is why fixtures with a handful of classes cannot tell them apart — the first
    /// evidence came from a real demo, where 362 classes must be 9 bits and the ceiling form said
    /// 10. Kept as the entity decoder's own entry point because that is where callers look for it,
    /// but the implementation lives in <see cref="WireWidths.ClassId"/> so it cannot drift from
    /// the copy <c>svc_ClassInfo</c> needs.
    /// </remarks>
    public static int ClassIdBits(int classCount) => WireWidths.ClassId(classCount);

    /// <summary>Decodes one snapshot body.</summary>
    /// <param name="body">Buffer holding the body's bits, starting at bit zero.</param>
    /// <param name="header">The message header, which says how many entities to expect.</param>
    /// <param name="lengthBits">How many bits of <paramref name="body"/> the body occupies.</param>
    /// <returns>The entities this snapshot described.</returns>
    /// <exception cref="InvalidDataException">
    /// The stream desynchronised: an impossible entity index, a property index past the end of
    /// its class, or a delta for an entity that never entered.
    /// </exception>
    public IReadOnlyList<DecodedEntity> Decode(
        ReadOnlySpan<byte> body, PacketEntitiesMessage header, int lengthBits)
    {
        ArgumentNullException.ThrowIfNull(header);

        BitReader reader = new(body);
        List<DecodedEntity> entities = new(header.UpdatedEntries);
        _removed.Clear();
        _entityEndBits.Clear();

        int entityIndex = -1;

        for (int i = 0; i < header.UpdatedEntries; i++)
        {
            entityIndex += (int)UBitVar.Read(ref reader, out int indexPayloadBits) + 1;

            if (entityIndex is < 0 or >= MaxEntities)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Entity index {entityIndex} is outside 0..{MaxEntities - 1}, so the entity " +
                    $"stream has desynchronised."));
            }

            entities.Add(ReadEntity(ref reader, entityIndex, indexPayloadBits));
            _entityEndBits.Add(reader.BitsRead);
        }

        // Where the entity section ended, so an encoder can be checked against what the decoder
        // actually consumed rather than against the whole body.
        EntitySectionBits = reader.BitsRead;

        // Removals are listed only on a delta. Reading them unconditionally would consume bits
        // belonging to whatever follows a full snapshot.
        if (header.IsDelta)
        {
            ReadRemovals(ref reader, lengthBits);
        }

        return entities;
    }

    /// <summary>Re-encodes a decoded snapshot back into a <c>svc_PacketEntities</c> body.</summary>
    /// <param name="entities">The entities, in the order they were decoded.</param>
    /// <param name="removed">Entity indices the snapshot reported as removed.</param>
    /// <param name="isDelta">Whether the snapshot was a delta, which is what carries removals.</param>
    /// <param name="lengthBits">The body length to pad out to.</param>
    /// <returns>The body's bits.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entities"/> or <paramref name="removed"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// **Here rather than in a class of its own, because the flattened schema is what both
    /// directions need** and a second copy of the flattening would be free to disagree with the
    /// first — which is the one mistake this code cannot afford, since the flattening order is
    /// what decides which value lands in which field.
    ///
    /// Nothing here is a guess. An entity index is a delta from the previous one, a property index
    /// likewise, and both are determined by the indices themselves; the update type, the class and
    /// the serial number are all recorded. That is what makes comparing the result against the
    /// demo a real experiment rather than a tautology: every bit written comes from a value the
    /// decoder produced, so a value it got wrong cannot come back right.
    ///
    /// The trailing padding matters more than it looks. A body is measured in bits and stated in
    /// bits, but the sender rounds, so a snapshot routinely ends a few bits early. Those bits are
    /// zeros, and the removal loop reads a clear bit as "no more removals" — which is exactly why
    /// they can be padding at all.
    /// </remarks>
    public byte[] EncodeEntities(
        IReadOnlyList<DecodedEntity> entities,
        IReadOnlyList<int> removed,
        bool isDelta,
        int lengthBits) =>
        EncodeEntities(entities, removed, isDelta, lengthBits, out _);

    /// <summary>Re-encodes a snapshot, reporting the exact bit count.</summary>
    /// <param name="entities">The entities, in the order they were decoded.</param>
    /// <param name="removed">Entity indices the snapshot reported as removed.</param>
    /// <param name="isDelta">Whether the snapshot was a delta, which is what carries removals.</param>
    /// <param name="lengthBits">The body length to pad out to.</param>
    /// <param name="bitCount">Bits written, before any padding to a byte boundary.</param>
    /// <returns>The body's bits.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entities"/> or <paramref name="removed"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// The count is not recoverable from the returned array: the last byte is padded to a
    /// boundary, so a caller comparing against an original would be comparing up to seven bits of
    /// slack it invented. That slack is enough to attribute a mismatch to the wrong entity.
    /// </remarks>
    public byte[] EncodeEntities(
        IReadOnlyList<DecodedEntity> entities,
        IReadOnlyList<int> removed,
        bool isDelta,
        int lengthBits,
        out int bitCount)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(removed);

        BitWriter writer = new();
        int previousIndex = -1;

        foreach (DecodedEntity entity in entities)
        {
            UBitVar.Write(
                writer,
                (uint)(entity.EntityIndex - previousIndex - 1),
                entity.IndexPayloadBits);
            previousIndex = entity.EntityIndex;

            writer.Write((uint)entity.UpdateType, 2);

            if (entity.UpdateType == EntityUpdateType.Enter)
            {
                writer.Write((uint)entity.ClassId, _classBits)
                    .Write((uint)entity.SerialNumber, SerialNumberBits);
            }
            else if (entity.UpdateType != EntityUpdateType.Delta)
            {
                // Leave and Delete carry nothing beyond the update type itself.
                continue;
            }

            WriteProperties(writer, entity.Properties);
        }

        if (isDelta)
        {
            // Mirrors bf_write, including its refusal to write a field that will not fit. A demo
            // whose sender overflowed here has a set flag bit and nothing behind it, and
            // reproducing that is the difference between a byte-exact re-encode and one that is
            // two bits long.
            foreach (int index in removed)
            {
                writer.WriteBit(true);
                writer.Write((uint)index, RemovedIndexBits);
            }

            if (lengthBits <= 0 || writer.BitCount < lengthBits)
            {
                writer.WriteBit(false);
            }
        }

        bitCount = writer.BitCount;

        for (int bit = writer.BitCount; bit < lengthBits; bit++)
        {
            writer.WriteBit(false);
        }

        return writer.Build();
    }

    private static void WriteProperties(
        BitWriter writer, IReadOnlyList<DecodedProperty> properties)
    {
        int previous = -1;

        // A one-bit continuation flag before each property rather than a count, so the list ends
        // with a clear bit rather than being sized up front.
        foreach (DecodedProperty property in properties)
        {
            writer.WriteBit(true);
            UBitVar.Write(
                writer, (uint)(property.Index - previous - 1), property.IndexPayloadBits);
            previous = property.Index;

            WriteValue(
                writer, property.Definition, property.Value, property.CoordShape,
                property.ElementShapes);
        }

        writer.WriteBit(false);
    }

    private static void WriteValue(
        BitWriter writer, FlatProperty flat, PropertyValue value, int shape = 0,
        IReadOnlyList<int>? elementShapes = null)
    {
        SendProperty property = flat.Property;

        switch (property.Type)
        {
            case SendPropType.Int:
                SendPropEncoder.WriteInt(writer, property, value.AsInt);
                break;

            case SendPropType.Float:
                SendPropEncoder.WriteFloat(writer, property, value.AsFloat, (shape & 1) != 0);
                break;

            case SendPropType.Vector:
                SendPropEncoder.WriteVector(writer, property, value.AsVector, shape);
                break;

            case SendPropType.VectorXY:
                SendPropEncoder.WriteVectorXY(writer, property, value.AsVectorXY, shape);
                break;

            case SendPropType.String:
                SendPropEncoder.WriteString(writer, value.AsString);
                break;

            case SendPropType.Array:
                WriteArray(writer, flat, value, elementShapes);
                break;

            default:
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Property '{property.Name}' has type {property.Type}, which never appears " +
                    $"in a flattened list - DataTable properties are structure, not values."));
        }
    }

    private static void WriteArray(
        BitWriter writer, FlatProperty flat, PropertyValue value,
        IReadOnlyList<int>? elementShapes)
    {
        if (flat.ArrayElement is not SendProperty element)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Array property '{flat.Property.Name}' has no element template, so its " +
                $"elements cannot be encoded."));
        }

        IReadOnlyList<PropertyValue> values = value.AsArray;

        // Sized from the declared maximum, exactly as on the way in - a fixed width here would be
        // right for one array and wrong for every other.
        writer.Write((uint)values.Count, ClassIdBits(flat.Property.ElementCount));

        FlatProperty elementFlat = new(element, flat.OwnerTable, null);
        for (int i = 0; i < values.Count; i++)
        {
            // **Each element has its own coordinate shape, and it must be carried.** Writing
            // shape 0 for every element re-encodes a float that arrived in one coordinate form
            // as another, which is the same width only by luck - RISKS B27, where a 16-element
            // array came out fifteen bits long.
            WriteValue(
                writer, elementFlat, values[i],
                elementShapes is not null && i < elementShapes.Count ? elementShapes[i] : 0);
        }
    }

    private DecodedEntity ReadEntity(ref BitReader reader, int entityIndex, int indexPayloadBits)
    {
        EntityUpdateType updateType = (EntityUpdateType)reader.ReadUInt32(2);

        if (updateType == EntityUpdateType.Enter)
        {
            int classId = (int)reader.ReadUInt32(_classBits);
            int serial = (int)reader.ReadUInt32(SerialNumberBits);
            _entityClasses[entityIndex] = classId;

            return new DecodedEntity(
                entityIndex, classId, serial, updateType,
                ReadProperties(ref reader, classId), indexPayloadBits);
        }

        if (updateType != EntityUpdateType.Delta)
        {
            // Leave and Delete carry nothing beyond the update type itself.
            _entityClasses.TryGetValue(entityIndex, out int knownClass);
            return new DecodedEntity(
                entityIndex, knownClass, 0, updateType, [], indexPayloadBits);
        }

        if (!_entityClasses.TryGetValue(entityIndex, out int existingClass))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Entity {entityIndex} was updated without ever entering, so its class is " +
                $"unknown and its properties cannot be sized."));
        }

        return new DecodedEntity(
            entityIndex, existingClass, 0, updateType,
            ReadProperties(ref reader, existingClass), indexPayloadBits);
    }

    /// <summary>Decodes a <c>svc_TempEntities</c> body into its effects.</summary>
    /// <param name="body">The message's body bytes.</param>
    /// <param name="count">How many effects the message declares.</param>
    /// <param name="lengthBits">The body's stated length in bits.</param>
    /// <returns>The effects, in order.</returns>
    /// <remarks>
    /// **Temp entities are entities, which is why this lives here.** Each effect is a class id and
    /// a property list read exactly as a <c>svc_PacketEntities</c> update reads one, against the
    /// same flattened schema. Explosions, tracers, impacts and shell casings all arrive this way,
    /// and until now the whole body was consumed by length and discarded — 761,828 of z1800's
    /// 1,226,354 opaque payload bits, and the single largest undeciphered part of the codec.
    ///
    /// Layout taken from <c>demostf/parser</c> rather than guessed. Two details are the ones a
    /// guess gets wrong:
    ///
    /// * the class id is stored **one higher than it is**, so a raw zero means "no class"
    /// * an effect may omit the class entirely and **repeat the previous effect's**, so a decoder
    ///   that treats each effect independently desynchronises at the second one
    ///
    /// The delay is eight bits of hundredths of a second, not a float.
    /// </remarks>
    public IReadOnlyList<DecodedTempEntity> DecodeTempEntities(
        ReadOnlySpan<byte> body, int count, int lengthBits)
    {
        BitReader reader = new(body);

        // A count byte of zero means one effect, sent reliably - not an empty message. The engine
        // spends the unused zero on the case it can infer, so a decoder that loops `count` times
        // drops a real effect and leaves the body unread with nothing reporting a problem.
        bool reliable = count == 0;
        int effectCount = reliable ? 1 : count;

        List<DecodedTempEntity> effects = new(effectCount);
        int classId = -1;

        for (int i = 0; i < effectCount; i++)
        {
            float delay = reader.ReadBit()
                ? reader.ReadUInt32(DelayBits) / DelayScale
                : 0f;

            if (reader.ReadBit())
            {
                // Stored one higher than the real id, so that zero can mean "unset" on the wire.
                classId = (int)reader.ReadUInt32(_classBits) - 1;
            }

            if (classId < 0)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Temp entity {i} carries no class and none preceded it, so there is no " +
                    $"schema to read its properties against."));
            }

            effects.Add(new DecodedTempEntity(
                classId, delay, ReadProperties(ref reader, classId), reliable));
        }

        // The message states its own body length, so a correct reading lands on it. Anything else
        // means the layout above is wrong for this demo rather than the demo being damaged.
        if (reader.BitsRead > lengthBits)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Decoding {effectCount} temp entities consumed {reader.BitsRead} bits of a stated " +
                $"{lengthBits}."));
        }

        return effects;
    }

    /// <summary>Re-encodes decoded temp entities back into a <c>svc_TempEntities</c> body.</summary>
    /// <param name="effects">The effects, in order.</param>
    /// <param name="reliable">Whether the message carried a single reliable effect.</param>
    /// <param name="lengthBits">The body length to pad out to.</param>
    /// <returns>The body's bits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is <c>null</c>.</exception>
    /// <remarks>
    /// One guess here, and it is checked rather than trusted: the class id is written only when it
    /// differs from the previous effect's, because that is what makes a burst of the same effect
    /// cheap on the wire. Whether the sender always omits a repeat is not something the decoded
    /// values can say - a repeated class and an omitted class look identical afterwards - so the
    /// assembly writer compares the result against the demo and falls back to raw if it differs.
    /// </remarks>
    public byte[] EncodeTempEntities(
        IReadOnlyList<DecodedTempEntity> effects, bool reliable, int lengthBits)
    {
        ArgumentNullException.ThrowIfNull(effects);

        BitWriter writer = new();
        int previousClass = -1;

        foreach (DecodedTempEntity effect in effects)
        {
            if (effect.DelaySeconds == 0f)
            {
                writer.WriteBit(false);
            }
            else
            {
                writer.WriteBit(true)
                    .Write((uint)MathF.Round(effect.DelaySeconds * DelayScale), DelayBits);
            }

            if (effect.ClassId == previousClass)
            {
                writer.WriteBit(false);
            }
            else
            {
                // Stored one higher than it is, so that a raw zero can mean "no class".
                writer.WriteBit(true).Write((uint)(effect.ClassId + 1), _classBits);
                previousClass = effect.ClassId;
            }

            WriteProperties(writer, effect.Properties);
        }

        _ = reliable;

        for (int bit = writer.BitCount; bit < lengthBits; bit++)
        {
            writer.WriteBit(false);
        }

        return writer.Build();
    }

    /// <summary>Width of a temp entity's fire delay.</summary>
    private const int DelayBits = 8;

    /// <summary>The delay is sent in hundredths of a second.</summary>
    private const float DelayScale = 100f;

    private List<DecodedProperty> ReadProperties(ref BitReader reader, int classId)
    {
        IReadOnlyList<FlatProperty> flat = FlattenedFor(classId);
        List<DecodedProperty> properties = [];
        int index = -1;

        // A one-bit continuation flag before each property, rather than a count. The list ends
        // when the flag is clear.
        while (reader.ReadBit())
        {
            index += (int)UBitVar.Read(ref reader, out int indexPayloadBits) + 1;

            if (index < 0 || index >= flat.Count)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Property index {index} is past the {flat.Count} properties of class " +
                    $"{classId}, so the entity stream has desynchronised."));
            }

            properties.Add(new DecodedProperty(
                index,
                flat[index],
                ReadValue(ref reader, flat[index], out int coordShape, out int[]? elementShapes),
                indexPayloadBits,
                coordShape,
                elementShapes));
        }

        return properties;
    }

    private static PropertyValue ReadValue(
        ref BitReader reader, FlatProperty flat, out int coordShape) =>
        ReadValue(ref reader, flat, out coordShape, out _);

    private static PropertyValue ReadValue(
        ref BitReader reader, FlatProperty flat, out int coordShape, out int[]? elementShapes)
    {
        coordShape = 0;
        elementShapes = null;
        SendProperty property = flat.Property;

        switch (property.Type)
        {
            case SendPropType.Int:
                return PropertyValue.FromInt(SendPropDecoder.ReadInt(ref reader, property));

            case SendPropType.Float:
            {
                float value = SendPropDecoder.ReadFloat(ref reader, property, out bool inBounds);
                coordShape = inBounds ? 1 : 0;
                return PropertyValue.FromFloat(value);
            }

            case SendPropType.Vector:
            {
                (float x, float y, float z) =
                    SendPropDecoder.ReadVector(ref reader, property, out coordShape);
                return PropertyValue.FromVector(x, y, z);
            }

            case SendPropType.VectorXY:
            {
                (float x, float y) =
                    SendPropDecoder.ReadVectorXY(ref reader, property, out coordShape);
                return PropertyValue.FromVectorXY(x, y);
            }

            case SendPropType.String:
                return PropertyValue.FromString(SendPropDecoder.ReadString(ref reader));

            case SendPropType.Array:
                return ReadArray(ref reader, flat, out elementShapes);

            default:
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Property '{property.Name}' has type {property.Type}, which never appears " +
                    $"in a flattened list - DataTable properties are structure, not values."));
        }
    }

    private static PropertyValue ReadArray(
        ref BitReader reader, FlatProperty flat, out int[] elementShapes)
    {
        if (flat.ArrayElement is not SendProperty element)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Array property '{flat.Property.Name}' has no element template, so its " +
                $"elements cannot be decoded."));
        }

        // The count is sized from the declared maximum, not transmitted at a fixed width.
        int countBits = ClassIdBits(flat.Property.ElementCount);
        int count = (int)reader.ReadUInt32(countBits);

        if (count > flat.Property.ElementCount)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Array property '{flat.Property.Name}' declares {count} elements, more than " +
                $"the {flat.Property.ElementCount} its definition allows."));
        }

        List<PropertyValue> values = new(count);
        int[] shapes = new int[count];
        FlatProperty elementFlat = new(element, flat.OwnerTable, null);

        for (int i = 0; i < count; i++)
        {
            values.Add(ReadValue(ref reader, elementFlat, out shapes[i]));
        }

        elementShapes = shapes;
        return PropertyValue.FromArray(values);
    }

    private void ReadRemovals(ref BitReader reader, int lengthBits)
    {
        // **The sender's buffer can run out mid-list, and the flag is written before the index.**
        // bf_write::WriteUBitLong refuses to write when fewer bits remain than the field needs:
        // it consumes the rest of the buffer, sets an overflow flag and writes nothing. So a body
        // can legitimately end with a set flag bit and no index behind it - the engine intended a
        // removal, ran out of room, and gave up.
        //
        // Reading an index there invents one out of unwritten buffer. It is silent: the value is
        // a plausible entity number, nothing overruns, and the only visible trace is that
        // re-encoding the message comes out two bits longer than the demo. That is how this was
        // found, and the engine's own guard says the same thing: when the bits remaining are
        // fewer than the field is wide, the write position jumps to the end of the buffer, an
        // overflow flag is raised, and no value is stored.
        //
        // The order matters and is why an earlier guard here was wrong: refusing to read the flag
        // unless a whole entry fits also discards the last legitimate removal of a body that ends
        // exactly after it.
        while (reader.BitsRead < lengthBits && reader.ReadBit())
        {
            if (lengthBits - reader.BitsRead < RemovedIndexBits)
            {
                break;
            }

            _removed.Add((int)reader.ReadUInt32(RemovedIndexBits));
        }
    }

    /// <summary>Raw baseline bits per class, exactly as the string table carried them.</summary>
    private readonly Dictionary<int, byte[]> _rawBaselines = [];

    /// <summary>Decoded baselines, dropped whenever the raw bits are rewritten.</summary>
    private readonly Dictionary<int, IReadOnlyList<DecodedProperty>> _decodedBaselines = [];

    /// <summary>Records a class's baseline, as carried by the <c>instancebaseline</c> table.</summary>
    /// <param name="classId">The networked class the baseline belongs to.</param>
    /// <param name="raw">The entry's user data — an encoded property list.</param>
    /// <remarks>
    /// **Stored raw and decoded on demand**, because a demo carries a baseline for every class
    /// while a match instantiates only some of them, and one of them ran to 7,669 bytes in the
    /// corpus. The reference implementation makes the same choice for the same reason.
    ///
    /// Rewriting a baseline drops its decoded copy. Baselines are updated mid-match through
    /// <c>svc_UpdateStringTable</c>, so a memo kept against a class id would otherwise serve a
    /// stale parse for the rest of the demo.
    /// </remarks>
    public void SetBaseline(int classId, ReadOnlySpan<byte> raw)
    {
        _rawBaselines[classId] = raw.ToArray();
        _decodedBaselines.Remove(classId);

    }

    /// <summary>A class's baseline properties, or <c>null</c> if it has none.</summary>
    /// <param name="classId">The networked class.</param>
    /// <returns>The decoded properties, or <c>null</c>.</returns>
    /// <remarks>
    /// Null rather than an empty list, deliberately: "this class has no baseline" and "this
    /// class's baseline is empty" are different facts, and an entity seeded from silence that
    /// looks like a decoded answer is the harder of the two to notice.
    /// </remarks>
    public IReadOnlyList<DecodedProperty>? Baseline(int classId)
    {
        if (_decodedBaselines.TryGetValue(classId, out IReadOnlyList<DecodedProperty>? cached))
        {
            return cached;
        }

        if (!_rawBaselines.TryGetValue(classId, out byte[]? raw))
        {
            return null;
        }

        // A baseline is encoded exactly like an entity delta, so the ordinary property loop
        // reads it - no separate codec, which is the whole reason this was cheap to add.
        BitReader reader = new(raw);
        IReadOnlyList<DecodedProperty> decoded = ReadProperties(ref reader, classId);
        _decodedBaselines[classId] = decoded;
        return decoded;
    }

    /// <summary>The flattened property list a class's updates index into.</summary>
    /// <param name="classId">The networked class.</param>
    /// <returns>Its properties, in the order the wire numbers them.</returns>
    /// <remarks>
    /// Public because the assembly text addresses properties by index, so reading that text back
    /// means resolving an index against the same list the decoder used. Recomputing the
    /// flattening on the reading side would be a second implementation of the one thing that
    /// cannot be allowed to differ: the order decides which value lands in which field.
    /// </remarks>
    public IReadOnlyList<FlatProperty> FlattenedFor(int classId)
    {
        // Stryker disable once Block: this is a cache. Removing it recomputes the same list
        // and returns the same answer, only slower - nothing observable changes.
        if (_flattened.TryGetValue(classId, out IReadOnlyList<FlatProperty>? cached))
        {
            return cached;
        }

        IReadOnlyList<FlatProperty> flat = _classesById.TryGetValue(classId, out ServerClass found)
            ? SchemaFlattener.Flatten(_schema, found)
            : [];

        _flattened[classId] = flat;
        return flat;
    }

    /// <summary>The networked class's name, or an empty string if the schema has no such id.</summary>
    /// <param name="classId">The class id read from an entering entity.</param>
    /// <returns>The name, e.g. <c>CTFPlayer</c>.</returns>
    /// <remarks>
    /// Lives here rather than being threaded through every caller because the decoder already
    /// holds the schema — a class id is only ever meaningful against the schema that produced it,
    /// and passing a separate lookup alongside the decoder invites the two coming from different
    /// demos.
    ///
    /// Empty rather than a placeholder for an unknown id: the caller prints the number too, so
    /// there is nothing to invent, and a fabricated name would be indistinguishable from a real
    /// one in a trace.
    /// </remarks>
    public string ClassName(int classId) =>
        _classesById.TryGetValue(classId, out ServerClass found) ? found.ClassName : string.Empty;
}
