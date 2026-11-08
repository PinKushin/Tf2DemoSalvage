using System.Collections.Generic;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A hand-built entity table for the readers that take an <see cref="EntityStateTable"/> directly: each entity is a class
/// name, an index and the properties the test chose, so the test knows the right answer because it put it there.
/// </summary>
/// <remarks>
/// A property's key is <c>Table.Name</c>, split at the first dot, which is also the shape an array element takes
/// (<c>m_iOwner.000</c>: no table name, `SendPropArray3` flattened).
/// </remarks>
internal sealed class EntityWorld
{
    private readonly Dictionary<string, int> _classIds = new(System.StringComparer.Ordinal);

    public EntityStateTable Table { get; } = new(EntityBaselines.None);

    /// <summary>A handle as the wire carries it: the serial above the eleven slot bits.</summary>
    public static int Handle(int index, int serial) => (serial << 11) | index;

    public static (string Key, PropertyValue Value) Int(string key, long value) => (key, PropertyValue.FromInt(value));

    public static (string Key, PropertyValue Value) Float(string key, float value) => (key, PropertyValue.FromFloat(value));

    public static (string Key, PropertyValue Value) Str(string key, string value) => (key, PropertyValue.FromString(value));

    public static (string Key, PropertyValue Value) Vector(string key, float x, float y, float z) => (key, PropertyValue.FromVector(x, y, z));

    /// <summary>Enters an entity of a class, naming the class on first use.</summary>
    public EntityState Add(string className, int index, int serial, params (string Key, PropertyValue Value)[] properties)
    {
        if (!_classIds.TryGetValue(className, out int classId))
        {
            classId = _classIds.Count + 1;
            _classIds[className] = classId;
            Table.SetClassName(classId, className);
        }

        List<DecodedProperty> decoded = [];

        foreach ((string key, PropertyValue value) in properties)
        {
            int dot = key.IndexOf('.', System.StringComparison.Ordinal);

            decoded.Add(new DecodedProperty(
                0,
                new FlatProperty(
                    new SendProperty(SendPropType.Int, key[(dot + 1)..], 0, string.Empty, 0f, 0f, 32, 0),
                    key[..dot],
                    null),
                value));
        }

        Table.Apply(new DecodedEntity(index, classId, serial, EntityUpdateType.Enter, decoded));
        Table.TryGet(index, out EntityState? state).ShouldBeTrue();

        return state;
    }
}
