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
public readonly record struct DecodedProperty(
    int Index,
    FlatProperty Definition,
    PropertyValue Value);

/// <summary>One entity as a snapshot described it.</summary>
/// <param name="EntityIndex">Slot in the entity table.</param>
/// <param name="ClassId">Networked class, remembered from whichever snapshot it entered on.</param>
/// <param name="SerialNumber">Distinguishes a reused slot from the entity that held it before.</param>
/// <param name="UpdateType">What happened to it.</param>
/// <param name="Properties">Properties this snapshot changed, in wire order.</param>
public sealed record DecodedEntity(
    int EntityIndex,
    int ClassId,
    int SerialNumber,
    EntityUpdateType UpdateType,
    IReadOnlyList<DecodedProperty> Properties);

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
    /// It is <c>floor(log2(count)) + 1</c>, not <c>ceil</c>. The two agree on exact powers of
    /// two and on small counts, which is why fixtures with a handful of classes cannot tell
    /// them apart — the first evidence came from a real demo, where 362 classes must be 9 bits
    /// and the ceiling form said 10.
    /// </remarks>
    public static int ClassIdBits(int classCount)
    {
        int bits = 0;
        while (classCount > 1)
        {
            // Stryker disable once Assignment: >>> differs from >> only for a negative value,
            // and the loop condition means a negative never reaches here. Equivalent mutant.
            classCount >>= 1;
            bits++;
        }

        return bits + 1;
    }

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

        int entityIndex = -1;

        for (int i = 0; i < header.UpdatedEntries; i++)
        {
            entityIndex += (int)UBitVar.Read(ref reader) + 1;

            if (entityIndex is < 0 or >= MaxEntities)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Entity index {entityIndex} is outside 0..{MaxEntities - 1}, so the entity " +
                    $"stream has desynchronised."));
            }

            entities.Add(ReadEntity(ref reader, entityIndex));
        }

        // Removals are listed only on a delta. Reading them unconditionally would consume bits
        // belonging to whatever follows a full snapshot.
        if (header.IsDelta)
        {
            ReadRemovals(ref reader, lengthBits);
        }

        return entities;
    }

    private DecodedEntity ReadEntity(ref BitReader reader, int entityIndex)
    {
        EntityUpdateType updateType = (EntityUpdateType)reader.ReadUInt32(2);

        if (updateType == EntityUpdateType.Enter)
        {
            int classId = (int)reader.ReadUInt32(_classBits);
            int serial = (int)reader.ReadUInt32(SerialNumberBits);
            _entityClasses[entityIndex] = classId;

            return new DecodedEntity(
                entityIndex, classId, serial, updateType,
                ReadProperties(ref reader, classId));
        }

        if (updateType != EntityUpdateType.Delta)
        {
            // Leave and Delete carry nothing beyond the update type itself.
            _entityClasses.TryGetValue(entityIndex, out int knownClass);
            return new DecodedEntity(entityIndex, knownClass, 0, updateType, []);
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
            ReadProperties(ref reader, existingClass));
    }

    private List<DecodedProperty> ReadProperties(ref BitReader reader, int classId)
    {
        IReadOnlyList<FlatProperty> flat = FlattenedFor(classId);
        List<DecodedProperty> properties = [];
        int index = -1;

        // A one-bit continuation flag before each property, rather than a count. The list ends
        // when the flag is clear.
        while (reader.ReadBit())
        {
            index += (int)UBitVar.Read(ref reader) + 1;

            if (index < 0 || index >= flat.Count)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Property index {index} is past the {flat.Count} properties of class " +
                    $"{classId}, so the entity stream has desynchronised."));
            }

            properties.Add(new DecodedProperty(
                index, flat[index], ReadValue(ref reader, flat[index])));
        }

        return properties;
    }

    private static PropertyValue ReadValue(ref BitReader reader, FlatProperty flat)
    {
        SendProperty property = flat.Property;

        switch (property.Type)
        {
            case SendPropType.Int:
                return PropertyValue.FromInt(SendPropDecoder.ReadInt(ref reader, property));

            case SendPropType.Float:
                return PropertyValue.FromFloat(SendPropDecoder.ReadFloat(ref reader, property));

            case SendPropType.Vector:
            {
                (float x, float y, float z) = SendPropDecoder.ReadVector(ref reader, property);
                return PropertyValue.FromVector(x, y, z);
            }

            case SendPropType.VectorXY:
            {
                (float x, float y) = SendPropDecoder.ReadVectorXY(ref reader, property);
                return PropertyValue.FromVectorXY(x, y);
            }

            case SendPropType.String:
                return PropertyValue.FromString(SendPropDecoder.ReadString(ref reader));

            case SendPropType.Array:
                return ReadArray(ref reader, flat);

            default:
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Property '{property.Name}' has type {property.Type}, which never appears " +
                    $"in a flattened list - DataTable properties are structure, not values."));
        }
    }

    private static PropertyValue ReadArray(ref BitReader reader, FlatProperty flat)
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
        FlatProperty elementFlat = new(element, flat.OwnerTable, null);

        for (int i = 0; i < count; i++)
        {
            values.Add(ReadValue(ref reader, elementFlat));
        }

        return PropertyValue.FromArray(values);
    }

    private void ReadRemovals(ref BitReader reader, int lengthBits)
    {
        // Bounded by the body length as well as by the flag, so a corrupt stream cannot spin
        // here reading whatever follows the message.
        while (reader.BitsRead < lengthBits && reader.ReadBit())
        {
            _removed.Add((int)reader.ReadUInt32(RemovedIndexBits));
        }
    }

    private IReadOnlyList<FlatProperty> FlattenedFor(int classId)
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
}
